using System;
using System.Collections.Generic;
using System.Linq;

namespace GMCore
{
    internal readonly struct LockstepStateHashAudit
    {
        public readonly ulong IncrementalHash;
        public readonly ulong RebuiltHash;

        public bool Matches { get { return IncrementalHash == RebuiltHash; } }

        public LockstepStateHashAudit(ulong incrementalHash, ulong rebuiltHash)
        {
            IncrementalHash = incrementalHash;
            RebuiltHash = rebuiltHash;
        }
    }

    /// <summary>
    /// A deterministic Merkle tree whose leaf zero contains global allocation/root metadata and
    /// whose remaining leaves are addressed directly by GameplayObject ID.
    /// </summary>
    internal sealed class LockstepStateHashTree
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private static readonly ulong EmptyLeafHash = HashTaggedBytes(0xE0, Array.Empty<byte>());

        private ulong[] m_nodes;
        private int m_capacity;

        public ulong RootHash { get { return m_nodes[1]; } }

        private LockstepStateHashTree(int capacity)
        {
            m_capacity = capacity;
            m_nodes = new ulong[capacity * 2];
            for (int index = capacity; index < m_nodes.Length; index++)
                m_nodes[index] = EmptyLeafHash;
            RebuildParents();
        }

        public static LockstepStateHashTree Build(GameplayMachine machine)
        {
            IGameplayObjectSaveManager saveManager = machine.ObjectManager as IGameplayObjectSaveManager;
            if (saveManager == null)
                throw new NotSupportedException(
                    $"{machine.ObjectManager.GetType()} does not support Lockstep state hashing");

            IGameplayObject[] objects = saveManager.GetGameplayObjectsForSave().ToArray();
            int maximumID = saveManager.IDAllocator.ID;
            foreach (IGameplayObject gameplayObject in objects)
            {
                if (gameplayObject.ObjectID.ID <= 0)
                    throw new InvalidOperationException(
                        $"Lockstep state contains non-authoritative GameplayObject ID {gameplayObject.ObjectID.ID}");
                maximumID = Math.Max(maximumID, gameplayObject.ObjectID.ID);
            }

            var result = new LockstepStateHashTree(CapacityFor(maximumID));
            result.SetLeafWithoutPropagation(0, HashLeaf(
                GameplayMachineSaveFormat.SaveLockstepMetadataLeaf(
                    machine, saveManager, machine.Serializers)));
            foreach (IGameplayObject gameplayObject in objects.OrderBy(item => item.ObjectID.ID))
            {
                result.SetLeafWithoutPropagation(gameplayObject.ObjectID.ID, HashLeaf(
                    GameplayMachineSaveFormat.SaveLockstepObjectLeaf(
                        machine, saveManager, gameplayObject, machine.Serializers)));
            }
            result.RebuildParents();
            return result;
        }

        public void UpdateMetadata(GameplayMachine machine)
        {
            IGameplayObjectSaveManager saveManager = GetSaveManager(machine);
            EnsureCapacity(saveManager.IDAllocator.ID);
            SetLeaf(0, HashLeaf(GameplayMachineSaveFormat.SaveLockstepMetadataLeaf(
                machine, saveManager, machine.Serializers)));
        }

        public void UpdateObject(GameplayMachine machine, GObjectID objectID)
        {
            if (objectID.ID <= 0)
                throw new InvalidOperationException(
                    $"Lockstep state contains non-authoritative GameplayObject ID {objectID.ID}");
            IGameplayObjectSaveManager saveManager = GetSaveManager(machine);
            EnsureCapacity(Math.Max(saveManager.IDAllocator.ID, objectID.ID));
            IGameplayObject gameplayObject = machine.ObjectManager.GetGameplayObject(objectID);
            ulong hash = gameplayObject == null
                ? EmptyLeafHash
                : HashLeaf(GameplayMachineSaveFormat.SaveLockstepObjectLeaf(
                    machine, saveManager, gameplayObject, machine.Serializers));
            SetLeaf(objectID.ID, hash);
        }

        private static IGameplayObjectSaveManager GetSaveManager(GameplayMachine machine)
        {
            IGameplayObjectSaveManager saveManager = machine.ObjectManager as IGameplayObjectSaveManager;
            if (saveManager == null)
                throw new NotSupportedException(
                    $"{machine.ObjectManager.GetType()} does not support Lockstep state hashing");
            return saveManager;
        }

        private void EnsureCapacity(int maximumID)
        {
            if (maximumID < m_capacity)
                return;
            int newCapacity = m_capacity;
            while (newCapacity <= maximumID)
                newCapacity = checked(newCapacity * 2);

            ulong[] oldNodes = m_nodes;
            int oldCapacity = m_capacity;
            m_capacity = newCapacity;
            m_nodes = new ulong[newCapacity * 2];
            for (int index = newCapacity; index < m_nodes.Length; index++)
                m_nodes[index] = EmptyLeafHash;
            for (int index = 0; index < oldCapacity; index++)
                m_nodes[newCapacity + index] = oldNodes[oldCapacity + index];
            RebuildParents();
        }

        private void SetLeafWithoutPropagation(int index, ulong hash)
        {
            m_nodes[m_capacity + index] = hash;
        }

        private void SetLeaf(int index, ulong hash)
        {
            int node = m_capacity + index;
            if (m_nodes[node] == hash)
                return;
            m_nodes[node] = hash;
            while ((node >>= 1) != 0)
                m_nodes[node] = HashParent(m_nodes[node * 2], m_nodes[node * 2 + 1]);
        }

        private void RebuildParents()
        {
            for (int node = m_capacity - 1; node > 0; node--)
                m_nodes[node] = HashParent(m_nodes[node * 2], m_nodes[node * 2 + 1]);
        }

        private static int CapacityFor(int maximumID)
        {
            if (maximumID < 0)
                throw new InvalidOperationException("Lockstep GameplayObject allocator cannot be negative");
            int capacity = 1;
            while (capacity <= maximumID)
                capacity = checked(capacity * 2);
            return capacity;
        }

        private static ulong HashLeaf(byte[] bytes) => HashTaggedBytes(0x00, bytes);

        private static ulong HashTaggedBytes(byte tag, byte[] bytes)
        {
            ulong hash = FnvOffset;
            unchecked
            {
                hash = (hash ^ tag) * FnvPrime;
                foreach (byte value in bytes)
                    hash = (hash ^ value) * FnvPrime;
            }
            return hash;
        }

        private static ulong HashParent(ulong left, ulong right)
        {
            ulong hash = FnvOffset;
            unchecked
            {
                hash = (hash ^ 0x01U) * FnvPrime;
                for (int shift = 0; shift < 64; shift += 8)
                    hash = (hash ^ (byte)(left >> shift)) * FnvPrime;
                for (int shift = 0; shift < 64; shift += 8)
                    hash = (hash ^ (byte)(right >> shift)) * FnvPrime;
            }
            return hash;
        }
    }

    internal sealed class LockstepStateHashTracker
    {
        private readonly GameplayMachine m_machine;
        private readonly HashSet<GObjectID> m_dirtyObjects = new HashSet<GObjectID>();
        private LockstepStateHashTree m_tree;
        private bool m_metadataDirty;
        private bool m_rebuildDirty;

        public bool IsInitialized { get { return m_tree != null; } }
        public ulong CurrentHash
        {
            get
            {
                EnsureInitialized();
                CommitDirty();
                return m_tree.RootHash;
            }
        }

        public LockstepStateHashTracker(GameplayMachine machine)
        {
            m_machine = machine;
        }

        public void Initialize()
        {
            m_tree = LockstepStateHashTree.Build(m_machine);
            ClearDirty();
        }

        public void MarkObject(GObjectID objectID)
        {
            if (objectID.ID > 0)
                m_dirtyObjects.Add(objectID);
            else
                m_rebuildDirty = true;
        }

        public void MarkObjectLife(GObjectID objectID)
        {
            MarkObject(objectID);
            m_metadataDirty = true;
        }

        public void MarkMetadata() { m_metadataDirty = true; }
        public void MarkAll() { m_rebuildDirty = true; }

        public ulong CommitDirty()
        {
            EnsureInitialized();
            if (m_rebuildDirty)
            {
                m_tree = LockstepStateHashTree.Build(m_machine);
                ClearDirty();
                return m_tree.RootHash;
            }

            if (m_metadataDirty)
                m_tree.UpdateMetadata(m_machine);
            foreach (GObjectID objectID in m_dirtyObjects.OrderBy(item => item.ID))
                m_tree.UpdateObject(m_machine, objectID);
            ClearDirty();
            return m_tree.RootHash;
        }

        public LockstepStateHashAudit AuditAndRebuild(
            Action<LockstepStateHashAudit> reportMismatchBeforeSwap)
        {
            ulong incrementalHash = CommitDirty();
            LockstepStateHashTree rebuilt = LockstepStateHashTree.Build(m_machine);
            ulong rebuiltHash = rebuilt.RootHash;
            var audit = new LockstepStateHashAudit(incrementalHash, rebuiltHash);
            if (!audit.Matches)
                reportMismatchBeforeSwap?.Invoke(audit);
            m_tree = rebuilt;
            ClearDirty();
            return audit;
        }

        private void EnsureInitialized()
        {
            if (m_tree == null)
                Initialize();
        }

        private void ClearDirty()
        {
            m_dirtyObjects.Clear();
            m_metadataDirty = false;
            m_rebuildDirty = false;
        }
    }
}
