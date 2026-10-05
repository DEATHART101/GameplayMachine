# GameplayMachine Core

## Interface RPC modes

OD interfaces use one of four RPC modes and are always invoked through `GameplayMachine.Execute`:

- `None` is a normal interface. It executes on the current machine, including a ClientProxy, and may have output fields.
- `Authority` has no output fields. An Authority executes it locally; a ClientProxy sends it to the Authority and returns immediately.
- `Multicast` has no output fields. Only an Authority may invoke it; it executes locally and on every connected ClientProxy.
- `Predictable` has no output fields. A ClientProxy validates and predicts locally, then the Authority validates and executes it before confirming or rejecting that prediction only to the calling client.

Authority and Multicast interfaces do not return business results. Clients observe authoritative results through replicated GameplayObject fields. Predictable interfaces receive only success or failure callbacks for reconciliation.
