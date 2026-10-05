using System;
using System.Collections.Generic;
using System.Linq;
using PlayerController = GMCore.StateSync.PlayerController;

namespace GMCore
{
    public readonly struct GameplaySpatialSpaceID : IEquatable<GameplaySpatialSpaceID>
    {
        private readonly string m_value;

        public string Value => m_value ?? string.Empty;
        public static GameplaySpatialSpaceID Default => default;

        public GameplaySpatialSpaceID(string value)
        {
            m_value = value ?? string.Empty;
        }

        public bool Equals(GameplaySpatialSpaceID other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is GameplaySpatialSpaceID other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(GameplaySpatialSpaceID left, GameplaySpatialSpaceID right) => left.Equals(right);
        public static bool operator !=(GameplaySpatialSpaceID left, GameplaySpatialSpaceID right) => !left.Equals(right);
    }

    internal readonly struct GameplaySpatialCell : IEquatable<GameplaySpatialCell>
    {
        public readonly GameplaySpatialSpaceID SpaceID;
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public GameplaySpatialCell(GameplaySpatialSpaceID spaceID, int x, int y, int z)
        {
            SpaceID = spaceID;
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(GameplaySpatialCell other) =>
            SpaceID.Equals(other.SpaceID) && X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is GameplaySpatialCell other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SpaceID, X, Y, Z);
    }

    internal sealed class GameplaySpatialIndex
    {
        private sealed class Entry
        {
            public Position Location;
            public GameplaySpatialCell Cell;
        }

        private readonly float m_cellSize;
        private readonly Dictionary<GObjectID, Entry> m_entries = new Dictionary<GObjectID, Entry>();
        private readonly Dictionary<GameplaySpatialCell, HashSet<GObjectID>> m_cells =
            new Dictionary<GameplaySpatialCell, HashSet<GObjectID>>();

        public GameplaySpatialIndex(float cellSize)
        {
            m_cellSize = cellSize;
        }

        public bool Upsert(GObjectID objectID, Position location)
        {
            GameplaySpatialCell cell = ToCell(location);
            if (m_entries.TryGetValue(objectID, out Entry entry))
            {
                if (entry.Location.Equals(location))
                    return false;
                if (!entry.Cell.Equals(cell))
                {
                    RemoveFromCell(objectID, entry.Cell);
                    AddToCell(objectID, cell);
                    entry.Cell = cell;
                }
                entry.Location = location;
                return true;
            }

            m_entries.Add(objectID, new Entry { Location = location, Cell = cell });
            AddToCell(objectID, cell);
            return true;
        }

        public bool Remove(GObjectID objectID)
        {
            if (!m_entries.TryGetValue(objectID, out Entry entry))
                return false;
            m_entries.Remove(objectID);
            RemoveFromCell(objectID, entry.Cell);
            return true;
        }

        public bool TryGetLocation(GObjectID objectID, out Position location)
        {
            if (m_entries.TryGetValue(objectID, out Entry entry))
            {
                location = entry.Location;
                return true;
            }
            location = default;
            return false;
        }

        public IEnumerable<GObjectID> Query(Position origin, float radius)
        {
            GameplaySpatialCell center = ToCell(origin);
            int cellRadius = checked((int)MathF.Ceiling(radius / m_cellSize));
            float width = cellRadius * 2f + 1f;
            var result = new HashSet<GObjectID>();

            if (width * width * width > m_cells.Count)
            {
                foreach (KeyValuePair<GameplaySpatialCell, HashSet<GObjectID>> pair in m_cells)
                {
                    GameplaySpatialCell cell = pair.Key;
                    if (cell.SpaceID.Equals(origin.Space) &&
                        Math.Abs((long)cell.X - center.X) <= cellRadius &&
                        Math.Abs((long)cell.Y - center.Y) <= cellRadius &&
                        Math.Abs((long)cell.Z - center.Z) <= cellRadius)
                        result.UnionWith(pair.Value);
                }
                return result;
            }

            long minX = Math.Max(int.MinValue, (long)center.X - cellRadius);
            long maxX = Math.Min(int.MaxValue, (long)center.X + cellRadius);
            long minY = Math.Max(int.MinValue, (long)center.Y - cellRadius);
            long maxY = Math.Min(int.MaxValue, (long)center.Y + cellRadius);
            long minZ = Math.Max(int.MinValue, (long)center.Z - cellRadius);
            long maxZ = Math.Min(int.MaxValue, (long)center.Z + cellRadius);
            for (long x = minX; x <= maxX; x++)
            for (long y = minY; y <= maxY; y++)
            for (long z = minZ; z <= maxZ; z++)
                if (m_cells.TryGetValue(new GameplaySpatialCell(origin.Space, (int)x, (int)y, (int)z), out HashSet<GObjectID> objects))
                    result.UnionWith(objects);
            return result;
        }

        public void Clear()
        {
            m_entries.Clear();
            m_cells.Clear();
        }

        private GameplaySpatialCell ToCell(Position location) => new GameplaySpatialCell(
            location.Space,
            checked((int)MathF.Floor(location.X / m_cellSize)),
            checked((int)MathF.Floor(location.Y / m_cellSize)),
            checked((int)MathF.Floor(location.Z / m_cellSize)));

        private void AddToCell(GObjectID objectID, GameplaySpatialCell cell)
        {
            if (!m_cells.TryGetValue(cell, out HashSet<GObjectID> objects))
            {
                objects = new HashSet<GObjectID>();
                m_cells.Add(cell, objects);
            }
            objects.Add(objectID);
        }

        private void RemoveFromCell(GObjectID objectID, GameplaySpatialCell cell)
        {
            if (!m_cells.TryGetValue(cell, out HashSet<GObjectID> objects))
                return;
            objects.Remove(objectID);
            if (objects.Count == 0)
                m_cells.Remove(cell);
        }
    }

    public sealed class GameplayInterestSettings
    {
        public float CellSize { get; set; } = 32f;
        public IGameplayPartitionResolver PartitionResolver { get; set; }
        public Action<GameplayMachine, Position, float> ProvisionPartitions { get; set; }
        public float LoadMargin { get; set; }
        public float UnloadDelaySeconds { get; set; } = 5f;
        public int MaxLoadsPerUpdate { get; set; } = 8;
        public bool ManagePartitionLoading { get; set; } = true;
    }

    public partial class GameplayMachine
    {
        [NonSerialized] private GameplaySpatialIndex m_spatialIndex;
        [NonSerialized] private GameplayInterestSettings m_interestSettings;
        [NonSerialized] private bool m_partitionInterestDirty;
        [NonSerialized] private Dictionary<PartitionID, DateTime> m_partitionUnusedSince;
        [NonSerialized] private int m_engineMutationDepth;

        public GameplayInterestSettings InterestSettings => m_interestSettings;

        protected virtual void OnProvisionPartitions(Position center, float range) { }

        protected virtual void OnQueryPartitions(Position center, float range, ISet<PartitionID> results) { }

        protected virtual bool TryResolvePartition(Position position, out PartitionID partition)
        {
            partition = default;
            return false;
        }

        public bool IsActor(IGameplayObjectOperator gameplayObject)
        {
            CheckValid(gameplayObject);
            return GameplayMachineBase.CanCastTo(GetGameplayObjectClass(gameplayObject.ObjectID), Actor.ClassName);
        }

        public bool TryGetPosition(IGameplayObjectOperator gameplayObject, out Position position)
        {
            CheckValid(gameplayObject);
            return m_spatialIndex.TryGetLocation(gameplayObject.ObjectID, out position);
        }

        internal bool IsSpatiallyManaged(GObjectID objectID)
        {
            if (!ContainsGameplayObject(objectID))
                return false;
            ODClassMeta meta = GetGameplayObjectClass(objectID).Meta;
            return meta.RelevancyMode == GameplayObjectRelevancyMode.Spatial &&
                   GameplayMachineBase.CanCastTo(meta.Name, Actor.ClassName);
        }

        internal bool IsSpatiallyVisibleTo(GObjectID objectID, GObjectID playerControllerID)
        {
            if (!IsSpatiallyManaged(objectID))
                return true;
            if (playerControllerID.IsValid && GetNetworkOwnerID(objectID).Equals(playerControllerID))
                return true;
            if (!TryResolveInterestOrigin(playerControllerID, out Position origin, out float radius) ||
                !m_spatialIndex.TryGetLocation(objectID, out Position target) ||
                !origin.Space.Equals(target.Space))
                return false;

            float x = target.X - origin.X;
            float y = target.Y - origin.Y;
            float z = target.Z - origin.Z;
            return x * x + y * y + z * z <= radius * radius;
        }

        internal IEnumerable<GObjectID> GetSpatialVisibilityCandidates(GObjectID playerControllerID)
        {
            var result = new HashSet<GObjectID>();
            if (TryResolveInterestOrigin(playerControllerID, out Position origin, out float radius))
                result.UnionWith(m_spatialIndex.Query(origin, radius));
            if (m_objectsByNetworkOwner.TryGetValue(playerControllerID, out HashSet<GObjectID> owned))
                result.UnionWith(owned.Where(IsSpatiallyManaged));
            return result;
        }

        internal void RecordGameplaySpatialFieldChange(GObjectID objectID, ODFieldName fieldName)
        {
            if (m_role != GameplayMachineRole.Authority || m_applyingReplication || m_cacheMode ||
                !ContainsGameplayObject(objectID))
                return;
            ODClassName className = GetGameplayObjectClass(objectID);
            if (fieldName.Equals(Actor.Fields.Transform) && GameplayMachineBase.CanCastTo(className, Actor.ClassName))
            {
                RefreshSpatialObject(objectID, true);
                RelocateSpatialActor(objectID);
                if (GameplayMachineBase.CanCastTo(className, Pawn.ClassName))
                {
                    Pawn pawn = new Pawn { Machine = this, ObjectID = objectID };
                    if (pawn.PlayerController is PlayerController player)
                        RecordSpatialViewerChanged(player.ObjectID);
                }
                m_partitionInterestDirty = true;
            }
            else if (className.Equals(PlayerController.ClassName) &&
                     (fieldName.Equals(PlayerController.Fields.Pawn) || fieldName.Equals(PlayerController.Fields.ViewRange)))
            {
                RecordSpatialViewerChanged(objectID);
                m_partitionInterestDirty = true;
            }
        }

        internal void RecordGameplaySpatialObjectLife(GObjectID objectID, bool created)
        {
            if (m_role != GameplayMachineRole.Authority || m_applyingReplication || m_cacheMode)
                return;
            if (created)
            {
                RefreshSpatialObject(objectID, true);
                if (GetGameplayObjectClass(objectID).Equals(PlayerController.ClassName))
                    m_partitionInterestDirty = true;
            }
            else
            {
                m_spatialIndex.Remove(objectID);
                m_partitionInterestDirty = true;
            }
        }

        internal void RefreshGameplaySpatialAvailability(GObjectID objectID, bool available)
        {
            if (m_role != GameplayMachineRole.Authority)
                return;
            if (available)
                RefreshSpatialObject(objectID, true);
            else
                m_spatialIndex.Remove(objectID);
        }

        internal void RebuildGameplaySpatialIndex()
        {
            if (m_role != GameplayMachineRole.Authority)
                return;
            m_spatialIndex.Clear();
            foreach (GObjectID objectID in GetAllGameplayObjectIDs())
                RefreshSpatialObject(objectID, false);
        }

        internal void ForgetGameplaySpatialMetadata(GObjectID objectID)
        {
            m_spatialIndex.Remove(objectID);
            m_partitionInterest.Remove(objectID);
            m_partitionInterestDirty = true;
        }

        internal void RecordSpatialOwnershipChanged(
            GObjectID objectID,
            GObjectID oldOwnerID,
            GObjectID newOwnerID)
        {
            if (m_role != GameplayMachineRole.Authority || !IsSpatiallyManaged(objectID))
                return;
            AuthorityGameplayNetworkSession authority = m_networkSession as AuthorityGameplayNetworkSession;
            if (oldOwnerID.IsValid)
                authority?.OnSpatialViewerChanged(oldOwnerID);
            if (newOwnerID.IsValid)
                authority?.OnSpatialViewerChanged(newOwnerID);
        }

        private void InitializeGameplaySpatialRelevancy(
            GameplayInterestSettings settings)
        {
            settings = settings ?? new GameplayInterestSettings();
            float cellSize = settings.CellSize;
            if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize < 0)
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (float.IsNaN(settings.LoadMargin) || float.IsInfinity(settings.LoadMargin) || settings.LoadMargin < 0)
                throw new ArgumentOutOfRangeException(nameof(settings.LoadMargin));
            if (float.IsNaN(settings.UnloadDelaySeconds) || float.IsInfinity(settings.UnloadDelaySeconds) || settings.UnloadDelaySeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(settings.UnloadDelaySeconds));
            if (settings.MaxLoadsPerUpdate < 1)
                throw new ArgumentOutOfRangeException(nameof(settings.MaxLoadsPerUpdate));
            m_interestSettings = settings;
            m_spatialIndex = new GameplaySpatialIndex(cellSize > 0 ? cellSize : 32f);
            m_partitionUnusedSince = new Dictionary<PartitionID, DateTime>();
            m_partitionInterestDirty = true;
        }

        internal void ConfigureGameplayInterestModules()
        {
            foreach (ODClassMeta meta in GameplayMachineBase.GetAllClasses())
                if (meta.RelevancyMode == GameplayObjectRelevancyMode.Spatial &&
                    !GameplayMachineBase.CanCastTo(meta.Name, Actor.ClassName))
                    throw new InvalidOperationException($"Spatial class {meta.Name} must inherit {Actor.ClassName}");
            RebuildGameplaySpatialIndex();
        }

        private void RefreshSpatialObject(GObjectID objectID, bool notify)
        {
            if (!ContainsGameplayObject(objectID))
                return;
            bool changed;
            if (!GameplayMachineBase.CanCastTo(GetGameplayObjectClass(objectID), Actor.ClassName))
            {
                changed = m_spatialIndex.Remove(objectID);
            }
            else
            {
                Transform transform = GetGameplayObjectValue<Transform>(objectID, Actor.Fields.Transform);
                ValidatePosition(transform.Position);
                changed = m_spatialIndex.Upsert(objectID, transform.Position);
            }
            if (!changed || !notify)
                return;
            (m_networkSession as AuthorityGameplayNetworkSession)?.OnSpatialObjectChanged(objectID);
            GObjectID owner = GetNetworkOwnerID(objectID);
            if (owner.IsValid)
                (m_networkSession as AuthorityGameplayNetworkSession)?.OnSpatialViewerChanged(owner);
        }

        private bool TryResolveInterestOrigin(GObjectID playerControllerID, out Position origin, out float radius)
        {
            if (!ContainsGameplayObject(playerControllerID))
            {
                origin = default;
                radius = 0;
                return false;
            }
            PlayerController player = CreatePlayerControllerReference(playerControllerID);
            radius = player.ViewRange;
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0)
            {
                origin = default;
                return false;
            }
            Pawn? pawn = player.Pawn;
            if (pawn.HasValue && m_spatialIndex.TryGetLocation(pawn.Value.ObjectID, out origin))
                return true;
            origin = default;
            return false;
        }

        private static void ValidatePosition(Position position)
        {
            if (float.IsNaN(position.X) || float.IsInfinity(position.X) ||
                float.IsNaN(position.Y) || float.IsInfinity(position.Y) ||
                float.IsNaN(position.Z) || float.IsInfinity(position.Z))
                throw new InvalidOperationException("Actor position must contain finite coordinates");
        }

        private void RecordSpatialViewerChanged(GObjectID playerControllerID)
        {
            (m_networkSession as AuthorityGameplayNetworkSession)?.OnSpatialViewerChanged(playerControllerID);
        }

        internal void ReconcileGameplayInterest()
        {
            if (Role != GameplayMachineRole.Authority || !m_partitionInterestDirty ||
                m_interestSettings.PartitionResolver == null || m_cacheMode)
                return;

            m_partitionInterestDirty = false;
            m_engineMutationDepth++;
            try
            {
                var required = new HashSet<PartitionID>();
                bool partitionVisibilityChanged = false;
                foreach (PlayerController player in GetPlayerControllers().ToArray())
                {
                    var visible = new HashSet<PartitionID>(Partitions
                        .Where(partition => !partition.Coordinate.HasValue)
                        .Select(partition => partition.ID));
                    if (TryResolveInterestOrigin(player.ObjectID, out Position origin, out float range))
                    {
                        m_interestSettings.ProvisionPartitions?.Invoke(this, origin,
                            range + m_interestSettings.LoadMargin);
                        m_interestSettings.PartitionResolver.Query(this, origin, range, visible);
                        m_interestSettings.PartitionResolver.Query(this, origin,
                            range + m_interestSettings.LoadMargin, required);
                    }
                    partitionVisibilityChanged |= SetPartitionInterestCore(player.ObjectID, visible);
                }

                if (m_interestSettings.ManagePartitionLoading)
                    ReconcilePartitionLoading(required);
                if (partitionVisibilityChanged)
                    RecordPartitionChange();
            }
            finally
            {
                m_engineMutationDepth--;
            }
        }

        private void ReconcilePartitionLoading(HashSet<PartitionID> required)
        {
            DateTime now = DateTime.UtcNow;
            int loaded = 0;
            foreach (GameplayPartition partition in Partitions.Where(item => item.Coordinate.HasValue).ToArray())
            {
                if (required.Contains(partition.ID))
                {
                    m_partitionUnusedSince.Remove(partition.ID);
                    if (!partition.IsLoaded && loaded < m_interestSettings.MaxLoadsPerUpdate)
                    {
                        LoadPartition(partition.ID);
                        loaded++;
                    }
                    continue;
                }
                if (!partition.IsLoaded)
                {
                    m_partitionUnusedSince.Remove(partition.ID);
                    continue;
                }
                if (!m_partitionUnusedSince.TryGetValue(partition.ID, out DateTime since))
                {
                    since = now;
                    m_partitionUnusedSince.Add(partition.ID, since);
                }
                if ((now - since).TotalSeconds >= m_interestSettings.UnloadDelaySeconds)
                {
                    UnloadPartition(partition.ID);
                    m_partitionUnusedSince.Remove(partition.ID);
                }
                else
                {
                    m_partitionInterestDirty = true;
                }
            }

            if (required.Any(id => !GetPartition(id).IsLoaded))
                m_partitionInterestDirty = true;
        }

        private void RelocateSpatialActor(GObjectID objectID)
        {
            if (m_interestSettings.PartitionResolver == null ||
                GetGameplayObjectClass(objectID).Meta.PartitionMode != GameplayObjectPartitionMode.Spatial ||
                !m_spatialIndex.TryGetLocation(objectID, out Position position) ||
                !m_interestSettings.PartitionResolver.TryResolve(this, position, out PartitionID destination) ||
                destination.Equals(GetGameplayObjectPartition(objectID)))
                return;
            if (!GetPartition(destination).IsLoaded)
            {
                m_engineMutationDepth++;
                try { LoadPartition(destination); }
                finally { m_engineMutationDepth--; }
            }
            MoveGameplayObject(objectID, destination);
        }
    }
}
