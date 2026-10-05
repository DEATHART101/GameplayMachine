# XLockstep

Deterministic lockstep primitives shared by GameplayMachine clients and headless match servers.

The library owns fixed-point arithmetic, canonical frame ordering, late-input retargeting, and the binary match protocol. It deliberately does not own matchmaking, HTTP, rendering, or gameplay rules.

Core rules:

- `Fixed64` stores six decimal places in a signed 64-bit raw value and checks arithmetic overflow.
- `LockstepFrameCoordinator.Accept` authenticates the player ID supplied by the server. A closed requested tick
  is moved to `NextOpenTick`, so latency delays an input instead of deleting it.
- Frames sort commands by player ID, per-player sequence, and interface type ID.
- `DeterministicSet` and `DeterministicMap` keep HashSet/Dictionary APIs while exposing canonical enumeration.
- The versioned binary protocol carries hello/welcome, input/ack, frame, confirmed empty-tick ranges, incremental
  or full-audit state hashes, reject, and ping/pong data.
