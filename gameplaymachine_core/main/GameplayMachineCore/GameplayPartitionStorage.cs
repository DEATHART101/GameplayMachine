using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GMCore
{
    public interface IGameplayPartitionStore
    {
        byte[] Read(string key);
        void Write(string key, byte[] data);
    }

    public sealed class FileGameplayPartitionStore : IGameplayPartitionStore
    {
        private readonly string m_directory;
        public FileGameplayPartitionStore(string directory) { m_directory = Path.GetFullPath(directory); }
        private string PathFor(string key)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || key == "." || key == "..")
                throw new ArgumentException("Partition store keys must be file names", nameof(key));
            return Path.Combine(m_directory, key);
        }
        public byte[] Read(string key) => File.ReadAllBytes(PathFor(key));
        public void Write(string key, byte[] data)
        {
            Directory.CreateDirectory(m_directory);
            string path = PathFor(key);
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(pending, data);
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }

    public partial class GameplayMachine
    {
        private IGameplayPartitionStore m_partitionStore;
        private readonly Dictionary<PartitionID, string> m_partitionPayloadKeys = new Dictionary<PartitionID, string>();

        private byte[] ReadPartitionPayload(GameplayPartition partition)
        {
            if (partition.Archive != null) return partition.Archive;
            if (m_partitionStore != null && m_partitionPayloadKeys.TryGetValue(partition.ID, out string key))
                return m_partitionStore.Read(key);
            throw new InvalidOperationException($"No payload is available for partition {partition.ID}");
        }

        public void SavePartitionedWorld(IGameplayPartitionStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (Role != GameplayMachineRole.Authority || DuringExecution)
                throw new InvalidOperationException("Save partitioned worlds on Authority between executions");
            // Publish the small manifest last. Interrupted writes leave the previous checkpoint valid.
            string generation = Guid.NewGuid().ToString("N");
            var keys = new Dictionary<PartitionID, string>();
            foreach (var partition in Partitions.Where(p => p.Kind != PartitionKind.Transient))
            {
                if (!partition.IsDirty && ReferenceEquals(store, m_partitionStore) && m_partitionPayloadKeys.TryGetValue(partition.ID, out var existingKey))
                { keys.Add(partition.ID, existingKey); continue; }
                string key = $"{generation}.{partition.ID.Value}.gmp";
                store.Write(key, SavePartition(partition.ID));
                keys.Add(partition.ID, key);
            }
            byte[] manifest = GameplayMachineSaveFormat.Save(this, ObjectManager, Serializers,
                includeObject: _ => false, embedPartitionPayloads: false);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x31575047);
                writer.Write(keys.Count);
                foreach (var pair in keys) { writer.Write(pair.Key.Value); writer.Write(pair.Value); }
                writer.Write(manifest.Length); writer.Write(manifest);
                store.Write("world.gmw", stream.ToArray());
            }
            m_partitionStore = store;
            m_partitionPayloadKeys.Clear();
            foreach (var pair in keys) m_partitionPayloadKeys.Add(pair.Key, pair.Value);
            foreach (var partition in Partitions) partition.IsDirty = false;
            foreach (var partition in Partitions.Where(p => !p.IsLoaded && p.Kind != PartitionKind.Transient)) partition.Archive = null;
        }

        internal void SetPartitionedRootsForLoad(GObjectID root, GObjectID player)
        { m_rootObjectID = root; m_localPlayerControllerID = player; }

        internal void RestorePartitionedWorld(IGameplayPartitionStore store, bool restoreLoadedPartitions)
        {
            m_partitionStore = store ?? throw new ArgumentNullException(nameof(store));
            using (var stream = new MemoryStream(store.Read("world.gmw"), false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != 0x31575047) throw new InvalidDataException("Invalid partitioned world header");
                int count = reader.ReadInt32();
                if (count < 0 || count > stream.Length) throw new InvalidDataException("Invalid partition count");
                for (int i = 0; i < count; i++) m_partitionPayloadKeys.Add(new PartitionID(reader.ReadInt32()), reader.ReadString());
                int length = reader.ReadInt32();
                if (length < 0 || length != stream.Length - stream.Position) throw new InvalidDataException("Invalid manifest length");
                GameplayMachineSaveFormat.Load(this, ObjectManager, reader.ReadBytes(length), Serializers, deferRootValidation: true);
            }
            var load = Partitions.Where(p => p.ID.Equals(PersistentPartitionID) || restoreLoadedPartitions && p.IsLoaded && p.Kind != PartitionKind.Transient).ToArray();
            foreach (var partition in Partitions) if (partition.Kind != PartitionKind.Transient) partition.IsLoaded = false;
            foreach (var partition in load) LoadPartition(partition.ID);
            SetRootForLoad(m_rootObjectID);
            SetLocalPlayerControllerForLoad(m_localPlayerControllerID);
        }
    }
}
