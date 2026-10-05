# Runtime Partitions and Reference Lifecycle

Status: original design notes. The implemented API and current limits are documented in [Gameplay Partitions](GameplayPartitions.md).

## Goals

- Support adventure rooms whose objects can move into persistent player-owned state.
- Support large streamed worlds split into independently loaded chunks.
- Preserve GameplayObject identity when an object moves between rooms, chunks, or persistent storage.
- Make unresolved and deleted GameplayObject references safe to read.
- Keep field callbacks, saves, replication, and streaming semantically consistent.

## Partition Model

Every runtime GameplayObject belongs to one machine-level partition. Partition membership is engine metadata and is not an OD field.

Initial partition kinds:

- `Persistent`: player, inventory, and objects carried across scenes.
- `SceneInstance`: one instantiated OD scene or adventure room.
- `Chunk`: one streamed world chunk.
- `Transient`: short-lived objects that do not need durable storage.

Moving an object between partitions preserves its `GObjectID`. Taking an item out of a room therefore moves the existing object to the persistent partition instead of cloning it.

An OD scene is a template. Creating a scene creates a runtime scene partition, instantiates its GameplayObjects with new runtime IDs, remaps references between template objects, and records the selected root ID.

Suggested machine APIs:

```csharp
PartitionID CreatePartition(PartitionKind kind, string key);
SceneInstanceID CreateScene(ODScene scene);
void MoveGameplayObject(GObjectID objectID, PartitionID destination);
void LoadPartition(PartitionID partitionID);
void UnloadPartition(PartitionID partitionID);
void DeletePartition(PartitionID partitionID);
```

Partitions are saved independently. A small manifest stores machine metadata, the root ID, partition identities, and which partitions are currently active. Chunk loading must not require deserializing the complete world.

## Reference Representation

GameplayObject fields and collections store stable `GObjectID` references. Generated getters resolve the stored identity against the owning machine:

- Existing and loaded target: return the typed GameplayObject operator.
- Existing but unloaded target: return `null`/`default`.
- Permanently deleted target: return `null`/`default`.
- Reference received before its target during snapshot or delta application: return `null`/`default` until the target arrives.

List, Set, Map, and GameplayObject references nested inside structs follow the same resolution rule.

## Effective Value and Changed Events

The public meaning of `AfterXChanged` is that the value observable through the generated getter changed. A reference can therefore change effectively even when no setter was called:

- `A -> null` when A is permanently deleted.
- `A -> null` when A becomes temporarily unavailable because its partition unloads.
- `null -> A` when a partition loads or a replicated target arrives.

Internally these cases must remain distinct.

### Permanent Delete

Deleting target A must proactively find every live reference to A before completing the deletion:

- A direct field such as `B.Target` is set to null and `B.AfterTargetChanged` fires once.
- A List item referencing A is removed and the List changed event fires once for the mutation batch.
- A Set item referencing A is removed and its item-changed event reports `Remove`.
- A Map key referencing A removes the entry and reports `Remove`.
- A Map value referencing A removes the entry by default and reports `Remove`. A future field policy may allow nulling a nullable value instead.
- A reference nested in a struct clears the nested slot and notifies the owning top-level OD field.

These are authoritative data mutations. They dirty the owning partitions, participate in save data, and produce normal field or collection replication deltas.

Deletion order:

1. Mark A as `Deleting` so new references to it are rejected.
2. Null or remove inbound references in one mutation transaction.
3. Dispatch/coalesce the affected field and collection events.
4. Remove A from the object manager and its partition.
5. Broadcast the GameplayObject deleted lifecycle event.

Object IDs are never reused. Callbacks may inspect the old reference identity supplied by event context, but resolving A after step 4 returns null.

### Temporary Unavailability

Partition unload, client interest loss, and out-of-order network arrival are not data deletion. The stored `GObjectID` remains intact and the owning partition is not dirtied.

The engine still emits the public `AfterXChanged` callback when the getter changes between an object and null, but it uses a local resolution-change path that:

- does not call `SetGameplayObjectValue`;
- does not create a field replication delta;
- does not mark save data dirty;
- can carry an internal reason such as `Loaded`, `Unloaded`, `Replicated`, or `Deleted` for diagnostics.

This preserves the useful rule "getter value changed, therefore the callback fires" without turning streaming into authoritative gameplay mutations.

## Reverse Reference Index

Proactive notification requires an engine-maintained reverse-reference index:

```text
target GObjectID -> reference slots
reference slot  -> owner object, top-level field, container/path information
```

The index is updated when:

- a direct or nested reference field is set;
- List/Set/Map items are added, removed, replaced, or cleared;
- a GameplayObject is created from a scene;
- a partition or complete save is loaded;
- a network snapshot or delta is applied.

It is rebuilt and validated after deserialization. Deletion and load/unload notification use this index instead of scanning every GameplayObject. For Lists, the index should identify the owning collection and target identity rather than relying only on an unstable numeric index.

## Networking

Authority owns permanent deletion and partition membership changes.

- A permanent delete sends reference field/collection deltas before `DeleteObject(A)`.
- Clients apply those deltas and receive the same field callbacks before the object-deleted lifecycle callback.
- Losing replication interest sends `Despawn`/`Unavailable`, not `DeleteObject`.
- Regaining interest sends `Spawn`/`Available`; retained references resolve again and receive local effective-value callbacks.
- Moving an object between relevant partitions can use `MovePartition`; it must not allocate a new object ID.

Initial synchronization may create referring objects before their targets. Reverse-index registration happens immediately, while effective-value callbacks are emitted as targets become available. Snapshot application should batch these callbacks to avoid exposing half-applied state.

## Validation and Tests

Required automated coverage:

- Direct, nullable, inherited, and nested-struct references become null and notify after deletion.
- List, Set, Map-key, and Map-value deletion cleanup emits the expected collection event exactly once.
- Diamond/cyclic object reference graphs do not recurse indefinitely during deletion.
- Unload/reload changes getter results and emits callbacks without dirtying saves or producing network field deltas.
- Out-of-order replicated references transition `null -> object` when the target arrives.
- Authority and clients observe reference cleanup before the target deletion event.
- Moving an object between scene/chunk/persistent partitions preserves identity and references.
- Save/load rebuilds the reverse-reference index and rejects references to permanently deleted IDs.

## Delivery Order

1. Add partition metadata, APIs, manifest persistence, and object movement.
2. Add reverse-reference indexing and permanent-delete cleanup.
3. Add effective-value callbacks for load/unload and out-of-order replication.
4. Add per-partition save/load and scene instantiation remapping.
5. Add network spawn/despawn/move messages and client interest management.
6. Add chunk streaming policy above the engine primitives.
