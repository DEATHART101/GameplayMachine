using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlayerController = GMCore.StateSync.PlayerController;
using LockstepPlayerController = GMCore.Lockstep.PlayerController;

namespace GMCore
{
    public readonly struct PartitionID : IEquatable<PartitionID>
    {
        public int Value { get; }
        public PartitionID(int value) { Value = value; }
        public bool Equals(PartitionID other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PartitionID other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }

    public readonly struct SceneInstanceID : IEquatable<SceneInstanceID>
    {
        public int Value { get; }
        public SceneInstanceID(int value) { Value = value; }
        public bool Equals(SceneInstanceID other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SceneInstanceID other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }

    public enum PartitionKind { Persistent, Scene, Chunk, Transient }

    public sealed class GameplayPartition
    {
        public PartitionID ID { get; internal set; }
        public SceneInstanceID SceneID { get; internal set; }
        public PartitionKind Kind { get; internal set; }
        public string Key { get; internal set; }
        public bool IsLoaded { get; internal set; } = true;
        public bool IsDirty { get; internal set; } = true;
        public GameplayPartitionCoordinate? Coordinate { get; internal set; }
        public IReadOnlyCollection<GObjectID> ObjectIDs => Members.ToArray();
        internal readonly HashSet<GObjectID> Members = new HashSet<GObjectID>();
        internal byte[] Archive;
    }

    public struct GameplayObjectAvailabilityChangedParam
    {
        public GObjectID ObjectID;
        public bool IsAvailable;
    }

    public struct GameplayObjectPartitionChangedParam
    {
        public GObjectID ObjectID;
        public PartitionID Previous;
        public PartitionID Current;
    }

    public partial class GameplayMachine
    {
        private readonly Dictionary<PartitionID, GameplayPartition> m_partitions = new Dictionary<PartitionID, GameplayPartition>();
        private readonly Dictionary<SceneInstanceID, GameplaySceneInstance> m_scenes = new Dictionary<SceneInstanceID, GameplaySceneInstance>();
        private readonly Dictionary<GObjectID, PartitionID> m_objectPartitions = new Dictionary<GObjectID, PartitionID>();
        private readonly Dictionary<GObjectID, HashSet<PartitionID>> m_partitionInterest = new Dictionary<GObjectID, HashSet<PartitionID>>();
        private readonly Dictionary<GObjectID, GObjectID> m_partitionOwners = new Dictionary<GObjectID, GObjectID>();
        private readonly Dictionary<GameplayPartitionCoordinate, PartitionID> m_partitionsByCoordinate =
            new Dictionary<GameplayPartitionCoordinate, PartitionID>();
        private readonly Dictionary<(GameplaySpatialSpaceID Space, int X, int Z), HashSet<PartitionID>>
            m_partitionsByColumn =
                new Dictionary<(GameplaySpatialSpaceID Space, int X, int Z), HashSet<PartitionID>>();
        private int m_partitionAllocator = 1;
        private int m_sceneAllocator;
        private PartitionID m_creationPartition = new PartitionID(1);
        private int m_partitionMutationDepth;

        public PartitionID PersistentPartitionID => new PartitionID(1);
        public IEnumerable<GameplayPartition> Partitions { get { EnsurePersistentPartition(); return m_partitions.Values.ToArray(); } }
        public IEnumerable<GameplaySceneInstance> Scenes => m_scenes.Values.ToArray();

        private void EnsurePersistentPartition()
        {
            if (!m_partitions.ContainsKey(PersistentPartitionID))
                m_partitions.Add(PersistentPartitionID, new GameplayPartition { ID = PersistentPartitionID, Kind = PartitionKind.Persistent, Key = "Persistent" });
        }

        public GameplayPartition GetPartition(PartitionID id)
        {
            EnsurePersistentPartition();
            return m_partitions.TryGetValue(id, out var partition) ? partition : throw new KeyNotFoundException($"Partition {id} does not exist");
        }

        public GameplaySceneInstance GetScene(SceneInstanceID id) =>
            m_scenes.TryGetValue(id, out var scene) ? scene : throw new KeyNotFoundException($"Scene {id} does not exist");

        public PartitionID GetGameplayObjectPartition(GObjectID id) =>
            m_objectPartitions.TryGetValue(id, out var partition) ? partition : PersistentPartitionID;

        public bool ExistsGameplayObject(GObjectID id) => ContainsGameplayObject(id) || m_objectPartitions.ContainsKey(id);

        private void CheckPartitionMutation()
        {
            if (Role != GameplayMachineRole.Authority)
                throw new InvalidOperationException("Only Authority can change partitions and scenes");
            CheckDataModifiedOutsideExecution();
            if (m_cacheMode)
                throw new InvalidOperationException("Partition operations cannot run in a speculative execution cache");
        }

        public PartitionID CreatePartition(PartitionKind kind, string key, SceneInstanceID sceneID = default,
            GameplayPartitionCoordinate? coordinate = null)
        {
            CheckPartitionMutation();
            EnsurePersistentPartition();
            if (kind == PartitionKind.Persistent || !Enum.IsDefined(typeof(PartitionKind), kind))
                throw new ArgumentException("Use the machine's existing Persistent partition", nameof(kind));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A partition key is required", nameof(key));
            if (sceneID.Value != 0) GetScene(sceneID);
            if (m_partitions.Values.Any(p => p.SceneID.Equals(sceneID) && p.Key == key))
                throw new ArgumentException("Partition key already exists in this scene", nameof(key));
            var id = new PartitionID(checked(++m_partitionAllocator));
            if (coordinate.HasValue && m_partitionsByCoordinate.ContainsKey(coordinate.Value))
                throw new ArgumentException("Partition coordinate already exists", nameof(coordinate));
            m_partitions.Add(id, new GameplayPartition
            {
                ID = id,
                Kind = kind,
                Key = key,
                SceneID = sceneID,
                Coordinate = coordinate,
            });
            if (coordinate.HasValue)
            {
                AddPartitionCoordinate(id, coordinate.Value);
                m_partitionInterestDirty = true;
            }
            RecordPartitionChange();
            return id;
        }

        public IDisposable UsePartition(PartitionID id)
        {
            CheckPartitionMutation();
            if (!GetPartition(id).IsLoaded) throw new InvalidOperationException("Cannot create objects in an unloaded partition");
            var previous = m_creationPartition;
            m_creationPartition = id;
            return new PartitionScope(() => m_creationPartition = previous);
        }

        private sealed class PartitionScope : IDisposable
        {
            private Action m_end;
            public PartitionScope(Action end) { m_end = end; }
            public void Dispose() { var end = m_end; m_end = null; end?.Invoke(); }
        }

        internal void RegisterGameplayObjectPartition(GObjectID id, PartitionID partition)
        {
            EnsurePersistentPartition();
            if (m_objectPartitions.TryGetValue(id, out var old) && m_partitions.TryGetValue(old, out var oldPartition))
                oldPartition.Members.Remove(id);
            GetPartition(partition).Members.Add(id);
            m_objectPartitions[id] = partition;
        }

        private void MarkPartitionDirty(GObjectID id)
        {
            if (!m_applyingReplication) GetPartition(GetGameplayObjectPartition(id)).IsDirty = true;
        }

        private void ForgetGameplayObjectPartition(GObjectID id)
        {
            MarkPartitionDirty(id);
            if (m_objectPartitions.TryGetValue(id, out var partition) && m_partitions.TryGetValue(partition, out var container)) container.Members.Remove(id);
            m_objectPartitions.Remove(id);
            m_partitionOwners.Remove(id);
            foreach (var child in m_partitionOwners.Where(p => p.Value.Equals(id)).Select(p => p.Key).ToArray())
                m_partitionOwners.Remove(child);
        }

        public void MoveGameplayObject(GObjectID id, PartitionID destination)
        {
            CheckPartitionMutation();
            ValidatePartitionMove(id, destination, false);
            MoveGameplayObjectCore(id, destination);
        }

        private void ValidatePartitionMove(GObjectID id, PartitionID destination, bool followingOwner)
        {
            if (!ContainsGameplayObject(id)) throw new InvalidOperationException("Load an object before moving it");
            if (!GetPartition(destination).IsLoaded) throw new InvalidOperationException("Destination partition is unloaded");
            ODClassName objectClass = GetGameplayObjectClass(id);
            if ((id.Equals(RootObjectID) || objectClass.Equals(PlayerController.ClassName) ||
                 objectClass.Equals(LockstepPlayerController.ClassName)) &&
                !destination.Equals(PersistentPartitionID))
                throw new InvalidOperationException("Machine root and PlayerControllers must remain persistent");
            if (!followingOwner && m_partitionOwners.TryGetValue(id, out var owner) && !GetGameplayObjectPartition(owner).Equals(destination))
                throw new InvalidOperationException("Clear or change the partition owner before moving its child");
            foreach (var scene in m_scenes.Values)
                if (scene.RootObjectID.Equals(id) && !GetPartition(destination).SceneID.Equals(scene.ID))
                    throw new InvalidOperationException("A scene root cannot leave its scene");
            foreach (var child in m_partitionOwners.Where(p => p.Value.Equals(id)).Select(p => p.Key))
                ValidatePartitionMove(child, destination, true);
        }

        private void MoveGameplayObjectCore(GObjectID id, PartitionID destination)
        {
            var previous = GetGameplayObjectPartition(id);
            if (previous.Equals(destination)) return;
            m_partitionMutationDepth++;
            try
            {
                GetPartition(previous).IsDirty = true;
                GetPartition(destination).IsDirty = true;
                RegisterGameplayObjectPartition(id, destination);
                foreach (var child in m_partitionOwners.Where(p => p.Value.Equals(id)).Select(p => p.Key).ToArray())
                    MoveGameplayObjectCore(child, destination);
                BroadCastMachineEvent(new GameplayObjectPartitionChangedParam { ObjectID = id, Previous = previous, Current = destination });
                RecordPartitionChange(id);
            }
            finally { m_partitionMutationDepth--; FlushNetworkChangesOutsideExecution(); }
        }

        // Explicit lifetime ownership; ordinary references never move their targets.
        public void SetPartitionOwner(GObjectID child, GObjectID owner)
        {
            CheckPartitionMutation();
            if (!ContainsGameplayObject(child) || (owner.IsValid && !ContainsGameplayObject(owner)))
                throw new InvalidOperationException("Partition owners and children must be loaded");
            for (var current = owner; current.IsValid; current = m_partitionOwners.TryGetValue(current, out var parent) ? parent : default)
                if (current.Equals(child)) throw new InvalidOperationException("Partition ownership cannot contain a cycle");
            if (owner.IsValid) ValidatePartitionMove(child, GetGameplayObjectPartition(owner), true);
            m_partitionOwners.Remove(child);
            if (owner.IsValid)
            {
                MoveGameplayObjectCore(child, GetGameplayObjectPartition(owner));
                m_partitionOwners[child] = owner;
            }
        }

        public void SetPartitionInterest(PlayerController player, IEnumerable<PartitionID> partitions)
        {
            CheckPartitionMutation();
            CheckValid(player);
            if (SetPartitionInterestCore(player.ObjectID, partitions))
                RecordPartitionChange();
        }

        private bool SetPartitionInterestCore(GObjectID player, IEnumerable<PartitionID> partitions)
        {
            if (partitions == null)
                return m_partitionInterest.Remove(player);
            var selected = new HashSet<PartitionID>(partitions);
            foreach (PartitionID id in selected) GetPartition(id);
            selected.Add(PersistentPartitionID);
            if (m_partitionInterest.TryGetValue(player, out HashSet<PartitionID> previous) &&
                previous.SetEquals(selected))
                return false;
            m_partitionInterest[player] = selected;
            return true;
        }

        public bool IsPartitionInInterest(PlayerController player, PartitionID partition)
        {
            CheckValid(player);
            GetPartition(partition);
            return !m_partitionInterest.TryGetValue(player.ObjectID, out HashSet<PartitionID> selected) ||
                   selected.Contains(partition);
        }

        public bool IsPartitionInLocalInterest(PartitionID partition)
        {
            GetPartition(partition);
            PlayerController? local = LocalPlayerController;
            return !local.HasValue || IsPartitionInInterest(local.Value, partition);
        }

        public bool TryGetPartitionAtCoordinate(GameplayPartitionCoordinate coordinate, out PartitionID partition) =>
            m_partitionsByCoordinate.TryGetValue(coordinate, out partition);

        internal IEnumerable<PartitionID> GetPartitionsAtColumn(
            GameplaySpatialSpaceID space, int x, int z) =>
            m_partitionsByColumn.TryGetValue((space, x, z), out HashSet<PartitionID> partitions)
                ? partitions.ToArray()
                : Array.Empty<PartitionID>();

        internal bool IsPartitionVisibleTo(GObjectID id, GObjectID player)
        {
            var partition = GetGameplayObjectPartition(id);
            return GetPartition(partition).IsLoaded &&
                (!m_partitionInterest.TryGetValue(player, out var selected) || selected.Contains(partition));
        }

        internal void RecordPartitionChange(GObjectID objectID = default)
        {
            if (!m_applyingReplication) m_networkSession?.OnPartitionsChanged(objectID);
            FlushNetworkChangesOutsideExecution();
        }

        public void UnloadPartition(PartitionID id)
        {
            CheckPartitionMutation();
            var partition = GetPartition(id);
            if (id.Equals(PersistentPartitionID)) throw new InvalidOperationException("Persistent partition cannot be unloaded");
            if (!partition.IsLoaded) return;
            var objects = partition.Members.ToArray();
            var affected = CaptureResolutionFields(objects);
            partition.Archive = SavePartition(id);
            m_partitionMutationDepth++;
            try
            {
                foreach (var objectID in objects)
                {
                    m_objectManager.RemoveGameplayObject(objectID);
                    m_eventSystemManager.ClearEvent(objectID);
                }
                partition.IsLoaded = false;
                NotifyResolutionFields(affected);
                foreach (var objectID in objects) NotifyAvailability(objectID, false);
                RecordPartitionChange();
            }
            finally { m_partitionMutationDepth--; FlushNetworkChangesOutsideExecution(); }
        }

        public void LoadPartition(PartitionID id)
        {
            CheckPartitionMutation();
            var partition = GetPartition(id);
            if (partition.IsLoaded) return;
            byte[] payload = ReadPartitionPayload(partition);
            var affected = CaptureResolutionFields(partition.Members);
            m_partitionMutationDepth++;
            try
            {
                try
                {
                    GameplayMachineSaveFormat.Load(this, m_objectManager, payload, Serializers, merge: true, expectedObjects: partition.Members);
                }
                catch
                {
                    foreach (var objectID in partition.Members) m_objectManager.RemoveGameplayObject(objectID);
                    throw;
                }
                partition.IsLoaded = true;
                partition.Archive = null;
                NotifyResolutionFields(affected);
                foreach (var objectID in partition.Members) NotifyAvailability(objectID, true);
                RecordPartitionChange();
            }
            finally { m_partitionMutationDepth--; FlushNetworkChangesOutsideExecution(); }
        }

        public byte[] SavePartition(PartitionID id)
        {
            var partition = GetPartition(id);
            if (!partition.IsLoaded) return (byte[])ReadPartitionPayload(partition).Clone();
            return GameplayMachineSaveFormat.Save(this, m_objectManager, Serializers,
                objectID => partition.Members.Contains(objectID), rootObjectIDOverride: default(GObjectID), includePartitions: false);
        }

        public void DeletePartition(PartitionID id)
        {
            CheckPartitionMutation();
            if (id.Equals(PersistentPartitionID)) throw new InvalidOperationException("Persistent partition cannot be deleted");
            var partition = GetPartition(id);
            m_partitionMutationDepth++;
            try
            {
                LoadPartition(id);
                foreach (var objectID in partition.Members.ToArray()) DeleteGameplayObject(objectID);
                m_partitions.Remove(id);
                if (partition.Coordinate.HasValue)
                    RemovePartitionCoordinate(id, partition.Coordinate.Value);
                m_partitionUnusedSince?.Remove(id);
                m_partitionInterestDirty = true;
                RecordPartitionChange();
            }
            finally { m_partitionMutationDepth--; FlushNetworkChangesOutsideExecution(); }
        }

        public void DestroyScene(SceneInstanceID id)
        {
            CheckPartitionMutation();
            GetScene(id);
            m_partitionMutationDepth++;
            try
            {
                foreach (var partition in Partitions.Where(p => p.SceneID.Equals(id)).ToArray()) DeletePartition(partition.ID);
                m_scenes.Remove(id);
                RecordPartitionChange();
            }
            finally { m_partitionMutationDepth--; FlushNetworkChangesOutsideExecution(); }
        }

        public GameplaySceneInstance CreateScene(IODScene template, string key)
        {
            CheckPartitionMutation();
            if (template == null) throw new ArgumentNullException(nameof(template));
            var id = new SceneInstanceID(checked(++m_sceneAllocator));
            var scene = new GameplaySceneInstance(this, id, key ?? template.GetType().FullName);
            m_scenes.Add(id, scene);
            m_partitionMutationDepth++;
            try
            {
                scene.MainPartitionID = CreatePartition(PartitionKind.Scene, "Main", id);
                PopulatePartition(template, scene.MainPartitionID, scene);
                RecordPartitionChange();
                return scene;
            }
            catch { DestroyScene(id); throw; }
            finally { m_partitionMutationDepth--; FlushNetworkChangesOutsideExecution(); }
        }

        internal void SetSceneRoot(GameplaySceneInstance scene, GObjectID objectID)
        {
            CheckPartitionMutation();
            if (!ContainsGameplayObject(objectID)) throw new InvalidOperationException("Scene root must be loaded");
            if (scene.ID.Value != 0 && !GetPartition(GetGameplayObjectPartition(objectID)).SceneID.Equals(scene.ID))
                throw new InvalidOperationException("Scene root must belong to the scene");
            scene.RootObjectID = objectID;
            RecordPartitionChange();
        }

        public GameplaySceneInstance PopulatePartition(IODScene template, PartitionID partitionID)
        {
            var partition = GetPartition(partitionID);
            return PopulatePartition(template, partitionID, new GameplaySceneInstance(this, partition.SceneID, template.GetType().FullName));
        }

        private GameplaySceneInstance PopulatePartition(IODScene template, PartitionID id, GameplaySceneInstance instance)
        {
            using (UsePartition(id)) template.Create(Proxy, instance);
            return instance;
        }

        private HashSet<GameplayReferenceField> CaptureResolutionFields(IEnumerable<GObjectID> targets) =>
            new HashSet<GameplayReferenceField>(targets.SelectMany(id => m_referenceIndex.GetIncoming(id)));

        private void NotifyResolutionFields(IEnumerable<GameplayReferenceField> fields)
        {
            foreach (var field in fields)
                if (ContainsGameplayObject(field.ObjectID))
                    BroadCastFieldChangedEvent(field.ObjectID, field.FieldName, ObjectEventTypes.Changed);
        }

        private void NotifyAvailability(GObjectID id, bool available)
        {
            RefreshGameplaySpatialAvailability(id, available);
            BroadCastMachineEvent(new GameplayObjectAvailabilityChangedParam { ObjectID = id, IsAvailable = available });
        }

        private IDisposable LoadIncomingPartitionsForDeletion(GObjectID target)
        {
            if (Role != GameplayMachineRole.Authority) return new PartitionScope(null);
            var ids = m_referenceIndex.GetIncoming(target).Select(f => GetGameplayObjectPartition(f.ObjectID))
                .Concat(m_objectsByNetworkOwner.TryGetValue(target, out var owned) ? owned.Select(GetGameplayObjectPartition) : Enumerable.Empty<PartitionID>())
                .Append(GetGameplayObjectPartition(target)).Distinct()
                .Where(id => m_partitions.TryGetValue(id, out var p) && !p.IsLoaded).ToArray();
            foreach (var id in ids) LoadPartition(id);
            return new PartitionScope(() =>
            {
                foreach (var id in ids)
                    if (m_partitions.TryGetValue(id, out var p) && p.IsLoaded) UnloadPartition(id);
            });
        }

        internal void WritePartitionManifest(GameplaySaveWriter writer, bool includeArchives, bool embedPayloads = true, GObjectID recipient = default)
        {
            EnsurePersistentPartition();
            writer.WriteInt32(m_partitionAllocator);
            writer.WriteInt32(m_sceneAllocator);
            writer.WriteCount(m_scenes.Count);
            foreach (var scene in m_scenes.Values.OrderBy(s => s.ID.Value))
            {
                writer.WriteInt32(scene.ID.Value); writer.WriteString(scene.Key);
                writer.WriteInt32(scene.MainPartitionID.Value);
                writer.WriteObjectID(includeArchives || ContainsGameplayObject(scene.RootObjectID) && IsObjectVisibleTo(scene.RootObjectID, recipient) ? scene.RootObjectID : default);
                var sceneObjects = scene.Objects.Where(item => includeArchives || ContainsGameplayObject(item.Value.ObjectID) && IsObjectVisibleTo(item.Value.ObjectID, recipient)).ToArray();
                writer.WriteCount(sceneObjects.Length);
                foreach (var item in sceneObjects)
                { writer.WriteResourceID(new ODResourceID(item.Key)); writer.WriteObjectID(item.Value.ObjectID); }
            }
            writer.WriteCount(m_partitions.Count);
            foreach (var partition in m_partitions.Values.OrderBy(p => p.ID.Value))
            {
                writer.WriteInt32(partition.ID.Value); writer.WriteInt32(partition.SceneID.Value);
                writer.WriteByte((byte)partition.Kind); writer.WriteString(partition.Key);
                writer.WriteBoolean(partition.Coordinate.HasValue);
                if (partition.Coordinate is GameplayPartitionCoordinate coordinate)
                {
                    writer.WriteString(coordinate.Space.Value);
                    writer.WriteInt32(coordinate.X);
                    writer.WriteInt32(coordinate.Y);
                    writer.WriteInt32(coordinate.Z);
                }
                writer.WriteBoolean(includeArchives && partition.Kind == PartitionKind.Transient || partition.IsLoaded);
                // Network membership is sent only with visible objects, never leak hidden IDs.
                var members = includeArchives && partition.Kind != PartitionKind.Transient ? partition.Members.ToArray() : Array.Empty<GObjectID>();
                writer.WriteCount(members.Length);
                foreach (var id in members) writer.WriteObjectID(id);
                var archive = includeArchives && embedPayloads && !partition.IsLoaded && partition.Kind != PartitionKind.Transient ? ReadPartitionPayload(partition) : null;
                writer.WriteCount(archive?.Length ?? 0);
                if (archive != null) foreach (byte b in archive) writer.WriteByte(b);
                var references = includeArchives && (!partition.IsLoaded || !embedPayloads) && partition.Kind != PartitionKind.Transient
                    ? m_referenceIndex.GetReferences(partition.Members).ToArray()
                    : Array.Empty<(GameplayReferenceField Field, GObjectID Target)>();
                writer.WriteCount(references.Length);
                foreach (var reference in references)
                {
                    writer.WriteObjectID(reference.Field.ObjectID);
                    writer.WriteUInt64(GameplayMachineBase.GetODFieldSaveMeta(reference.Field.FieldName).StableID);
                    writer.WriteObjectID(reference.Target);
                }
            }
            var owners = includeArchives ? m_partitionOwners.Where(pair =>
                GetPartition(GetGameplayObjectPartition(pair.Key)).Kind != PartitionKind.Transient).ToArray()
                : Array.Empty<KeyValuePair<GObjectID, GObjectID>>();
            writer.WriteCount(owners.Length);
            foreach (var pair in owners)
            { writer.WriteObjectID(pair.Key); writer.WriteObjectID(pair.Value); }
        }

        internal void ReadPartitionManifest(GameplaySaveReader reader, bool replace)
        {
            if (replace) { m_objectPartitions.Clear(); m_partitionOwners.Clear(); m_partitionInterest.Clear(); m_referenceIndex.Clear(); }
            m_partitionAllocator = reader.ReadInt32(); m_sceneAllocator = reader.ReadInt32();
            m_scenes.Clear();
            int scenes = ReadPartitionCount(reader);
            for (int i = 0; i < scenes; i++)
            {
                var id = new SceneInstanceID(reader.ReadInt32());
                var scene = new GameplaySceneInstance(this, id, reader.ReadString());
                scene.MainPartitionID = new PartitionID(reader.ReadInt32());
                var root = reader.ReadObjectID();
                int count = ReadPartitionCount(reader);
                for (int j = 0; j < count; j++) scene.Register(reader.ReadResourceID().Value,
                    new CommonGameplayObject { Machine = this, ObjectID = reader.ReadObjectID() });
                scene.RootObjectID = root;
                m_scenes.Add(id, scene);
            }
            var previous = m_partitions.ToDictionary(p => p.Key, p => p.Value);
            m_partitions.Clear();
            m_partitionsByCoordinate.Clear();
            m_partitionsByColumn.Clear();
            int partitions = ReadPartitionCount(reader);
            for (int i = 0; i < partitions; i++)
            {
                var id = new PartitionID(reader.ReadInt32());
                var partition = new GameplayPartition
                {
                    ID = id,
                    SceneID = new SceneInstanceID(reader.ReadInt32()),
                    Kind = (PartitionKind)reader.ReadByte(),
                    Key = reader.ReadString(),
                    IsDirty = false,
                };
                if (reader.ReadBoolean())
                    partition.Coordinate = new GameplayPartitionCoordinate(
                        new GameplaySpatialSpaceID(reader.ReadString()),
                        reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                partition.IsLoaded = reader.ReadBoolean();
                int count = ReadPartitionCount(reader);
                for (int j = 0; j < count; j++) { var member = reader.ReadObjectID(); partition.Members.Add(member); m_objectPartitions[member] = id; }
                int bytes = ReadPartitionCount(reader);
                if (bytes > 0) { partition.Archive = new byte[bytes]; for (int j = 0; j < bytes; j++) partition.Archive[j] = reader.ReadByte(); }
                int references = ReadPartitionCount(reader);
                for (int j = 0; j < references; j++)
                {
                    var owner = reader.ReadObjectID();
                    if (!GameplayMachineBase.TryGetODFieldSaveMeta(reader.ReadUInt64(), out var meta))
                        throw new InvalidDataException("Unknown archived reference field");
                    var field = meta.FieldName;
                    m_referenceIndex.RestoreReference(new GameplayReferenceField(owner, field), reader.ReadObjectID());
                }
                if (!replace && previous.TryGetValue(id, out var old)) partition.Members.UnionWith(old.Members);
                m_partitions.Add(id, partition);
                if (partition.Coordinate.HasValue)
                {
                    if (m_partitionsByCoordinate.ContainsKey(partition.Coordinate.Value))
                        throw new InvalidDataException("Partition manifest contains duplicate coordinates");
                    AddPartitionCoordinate(id, partition.Coordinate.Value);
                }
            }
            int owners = ReadPartitionCount(reader);
            for (int i = 0; i < owners; i++) m_partitionOwners[reader.ReadObjectID()] = reader.ReadObjectID();
            EnsurePersistentPartition();
        }

        private void AddPartitionCoordinate(PartitionID id, GameplayPartitionCoordinate coordinate)
        {
            m_partitionsByCoordinate.Add(coordinate, id);
            var key = (coordinate.Space, coordinate.X, coordinate.Z);
            if (!m_partitionsByColumn.TryGetValue(key, out HashSet<PartitionID> partitions))
            {
                partitions = new HashSet<PartitionID>();
                m_partitionsByColumn.Add(key, partitions);
            }
            partitions.Add(id);
        }

        private void RemovePartitionCoordinate(PartitionID id, GameplayPartitionCoordinate coordinate)
        {
            m_partitionsByCoordinate.Remove(coordinate);
            var key = (coordinate.Space, coordinate.X, coordinate.Z);
            if (!m_partitionsByColumn.TryGetValue(key, out HashSet<PartitionID> partitions))
                return;
            partitions.Remove(id);
            if (partitions.Count == 0)
                m_partitionsByColumn.Remove(key);
        }

        private static int ReadPartitionCount(GameplaySaveReader reader)
        {
            int count = reader.ReadCount();
            if (count < 0 || count > reader.Length) throw new InvalidDataException("Invalid partition manifest count");
            return count;
        }
    }
}
