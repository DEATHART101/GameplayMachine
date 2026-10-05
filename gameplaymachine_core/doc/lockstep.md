# GameplayMachine Lockstep

Lockstep is selected for the whole generated OD project with
`GameplayMachineSettings.SynchronizationMode = GameplaySynchronizationMode.Lockstep`. A machine cannot mix
StateSync and Lockstep interfaces.

## Execution boundary

- Application code may submit only generated `PlayerInput` interfaces through `GameplayMachine.Execute`.
- Submission does not execute gameplay immediately. The relay authenticates the connection, assigns the input
  to an open tick, orders the frame by player and sequence, then broadcasts it over the gameplay UDP transport.
- An input that arrives after its requested tick is reassigned to the next open tick and acknowledged through
  `GameplayLockstepInputAcceptedParam`; it is not silently dropped.
- During frame execution, a generated PlayerInput implementation may call ordinary interfaces only through its
  generated `GameplayMachineProxy`.
- `GameplayMachineProxy` rejects PlayerInput interfaces, preventing recursive or bypassed frame submission.
- State mutation outside interface execution is automatically disabled in Lockstep mode.

## Deterministic model

OD Studio generates `Single`, `Double`, and `Decimal` as `XLockstep.Fixed64`. It emits deterministic Set and Map
types and their GameplayMachine storage collections retain the existing field/item change events. Lists preserve
their explicit order; Sets and Maps enumerate canonically. Lockstep validation limits Set values and Map keys to
types with a canonical order.

Actor, Pawn, Transform, spatial relevancy, partition controls, routines, StateSync prediction, and multicast
events are unavailable in a Lockstep project. The first implementation deliberately has no deterministic
Transform replacement.

## Hosting

Every game process is a complete Authority simulation created with `SpawnGameplayMachine`. Create one standalone
`GameplayLockstepRelayServer`, call `Start`, and connect every game peer with
`ConnectLockstepNetworking`. The relay has no `GameplayMachine`, OD module, gameplay object, generated interface
implementation, or save data. It sees PlayerInput payloads only as opaque bytes and is responsible solely for
connection identity, player-slot assignment, deterministic ordering, frame timing, heartbeat traffic, and hash
forwarding.

`ExpectedPlayerCount` gates the match start. After all peers connect, the relay compares their schema ID, resource
catalog ID, and generated Lockstep-interface manifest. Each peer creates the same deterministic initial state and
player-controller slots locally, then reports its tick-zero full-state hash. The relay sends `Ready` only when all
initial hashes match. There is no authority snapshot and no server-side gameplay execution.

```csharp
var options = new GameplayLockstepOptions { ExpectedPlayerCount = 2 };
var relay = new GameplayLockstepRelayServer(serverTransport, options);
relay.Start();

GameplayMachine first = GameplayMachineBase.SpawnGameplayMachine(null, settings, modules);
GameplayMachine second = GameplayMachineBase.SpawnGameplayMachine(null, settings, modules);
first.ConnectLockstepNetworking(firstTransport, options);
second.ConnectLockstepNetworking(secondTransport, options);

// Pump all three in the application's update loop.
relay.Update();
first.UpdateNetwork();
second.UpdateNetwork();
```

State verification is two-level:

- Every applied tick commits only dirty GameplayObject leaves and their Merkle paths. Object creation/deletion,
  stored fields and GameplayMachine List/Set/Map mutations, ownership, root, and partition changes all mark the
  corresponding state dirty. `GameplayLockstepFrameAppliedParam.StateHash` and
  `ComputeLockstepStateHash()` expose this current Merkle root.
- `StateHashIntervalTicks` controls how often fixed-rate peers send an incremental root. An
  input-driven match sends one for every command frame.
- `FullStateHashIntervalTicks` defaults to 300. At that tick boundary each peer independently traverses the
  actual object manager, rebuilds the complete tree, compares it with its pre-existing incremental root, and
  then atomically adopts the rebuilt tree. A missed dirty mark raises
  `GameplayLockstepHashAuditFailedParam`; a peer mismatch raises `GameplayLockstepDesyncParam` with the remote
  player slot and `HashKind = FullAudit`.
- `ComputeLockstepSnapshotHash()` retains the slower monolithic network-snapshot hash for diagnostics. It is not
  called automatically per tick.

The hash is a deterministic divergence detector, not a cryptographic integrity proof. A leaf contains the
object ID, class, network owner, replication mode, partition, and all stored fields in stable-ID order. Leaf zero
contains the object-ID allocator and root ID. Resources are covered separately by the resource-catalog handshake.

`TickMode` controls the clock:

- `FixedRate` is the default for real-time simulation. The relay confirms every logical tick, but consecutive
  empty frames are sent as a reliable `AdvanceTicks` range. A range is flushed before a non-empty frame, at the
  state-hash/audit interval, or after `EmptyFrameFlushMilliseconds`. Peers apply every confirmed empty tick in order;
  they never infer missing ticks locally. `InputDelayTicks` controls the normal scheduling lead.
- `InputDriven` is for chess, card games, and other simulations where nothing changes without a player command.
  The relay seals a frame only when at least one authenticated PlayerInput is pending. `InputDelayTicks` must
  be zero, every non-empty frame carries a state-hash check, and future requested ticks are retargeted to the next
  open tick so an input cannot become permanently pending.

`MaximumEmptyTicksPerMessage` bounds range expansion on peers. `HeartbeatMilliseconds` sends independent
unreliable-sequenced ping/pong traffic, so an idle input-driven match is not completely silent. Gameplay clocks
or disconnect deadlines should use an agreed deterministic clock rather than relying on input-driven ticks.

Matchmaking and control-plane HTTP are outside this layer. Match traffic, handshake, input acknowledgements,
frames, hashes, and ping/pong use the configured gameplay transport; `LiteNetLibGameplayNetworkTransport` is the
default UDP implementation.
