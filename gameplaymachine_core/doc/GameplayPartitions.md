# Gameplay Partitions

Implemented runtime model. Save format is GMS2 version 8; replication frames are version 7. Both peers must use this version. Older snapshots are not migrated.

## Scene and Partition

- An OD scene is an authoring template; `CreateScene` instantiates it and returns a `GameplaySceneInstance` with its own root, template-object mapping and main partition.
- A partition is the actual object loading/storage unit. A scene can contain its main partition and additional chunk partitions. Scenes do not introduce a second object manager.
- Each object belongs to exactly one partition. Moving it preserves its object ID and all references.
- `GameplayMachine.GetRoot()` remains the single persistent machine root. Creating a scene does not replace it. Use `scene.Root` for a scene root. Root and PlayerControllers cannot leave Persistent.
- `Persistent` cannot unload. `Scene` and `Chunk` support unload/reload and durable saves. `Transient` supports in-session unloading but its objects are omitted from durable saves.

Mutations run on Authority through the normal execution boundary (or during authorized machine initialization). ClientProxy receives partition state; it cannot load or rearrange authoritative objects itself. No background mutation thread is introduced.

```csharp
// Inside DoExecute, gm is GameplayMachineProxy.
var room = gm.CreateScene(RoomScene.Instance);
var chunk = gm.CreatePartition(PartitionKind.Chunk, "0:0", room.ID);
using (gm.UsePartition(chunk))
{
    var item = gm.CreateGameplayObject<Item>();
    // Set its initial fields here.
}
gm.UnloadPartition(chunk);
gm.LoadPartition(chunk); // Same IDs and saved values.
gm.DestroyScene(room.ID);
```

`PopulatePartition(template, partitionID)` fills an existing partition and returns the new template-object mapping. It does not replace the containing scene's root.

## Moving Between Rooms

Before destroying the old room, move the player and carried objects to Persistent or to the new room. `DestroyScene` permanently deletes objects still belonging to its partitions. It does not delete objects moved elsewhere.

`SetPartitionOwner(childID, ownerID)` explicitly makes a child follow its owner's partition. Assign this relationship when an item becomes carried; clear it with a default owner ID when dropping it, then move it into the spatial partition. Ordinary OD references do not imply lifetime ownership. Network owner and partition owner are separate concepts. Cycles and invalid group moves are rejected before moving the group.

The engine does not infer spatial coordinates or inventory semantics. The game supplies that policy, ideally in shared pickup/drop and movement interfaces.

## References and Events

- Unloading removes live objects but retains IDs and stored reference slots. Getters resolve unavailable targets to null/default; loading makes the same references resolve again.
- Effective reference changes notify the owning field, including collection Changed and dependent driven fields. Availability alone does not emit Set/Map item mutation events or dirty the owner.
- Permanent deletion uses the reverse-reference index and normal cleanup mutations. Archived inbound owners are loaded only as needed, cleaned, and returned to their unloaded state.
- Subscribe to `GameplayObjectAvailabilityChangedParam` for unload/load and visibility changes. Create/delete lifecycle events still represent creation and permanent removal; replicated introductions retain the existing created event contract.
- `ContainsGameplayObject` means available now. `ExistsGameplayObject` also includes known archived IDs. A proxy cannot discover IDs it has never received.

## Storage

`SaveGameplayMachine` still produces a complete snapshot, including archived partitions. For streaming storage use:

```csharp
var store = new FileGameplayPartitionStore(directory);
machine.SavePartitionedWorld(store); // Between executions.
var loaded = GameplayMachineBase.LoadPartitionedGameplayMachine(
    new MachineKey { ID = 1 }, settings, store, false, modules);
// false: load Persistent only; call LoadPartition for the desired rooms/chunks.
```

`IGameplayPartitionStore` allows another storage backend. A checkpoint writes new generation payloads for dirty partitions, reuses unchanged payloads, and publishes `world.gmw` last. Restoring reads the manifest and requested payloads only. Stored reverse-reference metadata supports deletion without scanning every partition payload.

The file store uses atomic file replacement for each write. Old generation files remain for recovery; retention/garbage collection is not implemented. I/O and decoding are synchronous. Saving a checkpoint releases in-memory archives backed by the store; unloading without a checkpoint retains its archive in RAM.

## Networking

Authority `SetPartitionInterest(controller, partitions)` restricts a player's subscribed partitions; null means all. Persistent is always included. This filter intersects existing object and field None/OwnerOnly/AllClients rules and never overrides them.

Leaving interest/unloading sends unavailability, not a permanent delete. Reentry sends the current visible object state with the same IDs. Moving an object between two visible partitions only updates membership. A partition's `IsLoaded` describes Authority state, not whether that proxy subscribes to it.

Ordinary fields retain their existing deltas; Set/Map retain per-key deltas. Structural partition changes currently resend the small scene/partition directory, filtered to exclude hidden object IDs, rather than using directory-entry deltas. The existing reliable UDP transport performs packet fragmentation. Interest defaults to all until configured, so use the player initialization hook to restrict the initial snapshot in a production game.

## Verification

`main/Test/GameplayPartitionTests.cs` covers independent roots, movement, ownership, unload/reload resolution events, archived deletion, targeted storage reads, dirty checkpoints, transient data and network interest transitions. Existing networking tests cover owner handoff and RPCs. `samples/BlockWorld` additionally exercises the Godot scene tree, physics, real input, views, disk save/reload and two-process UDP play.
