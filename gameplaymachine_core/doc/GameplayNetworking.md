# GameplayMachine Networking

GameplayMachine has two runtime roles:

- `Authority`: the normal standalone machine. Networking can be enabled or disabled at runtime.
- `ClientProxy`: a replicated view of an Authority. It cannot host or write fields directly, but it can execute `None` interfaces locally and modify local state inside Predictable callbacks.

`LiteNetLibGameplayNetworkTransport` is the built-in reliable UDP transport. GameplayMachine divides large logical messages into bounded fragments, while LiteNetLib supplies packet-level reliability, ordering, retransmission, connection liveness, and MTU-aware UDP delivery. `InMemoryGameplayNetworkTransport` is intended for tests and same-process use; the legacy TCP transport remains available.

## Start an Authority

```csharp
GameplayMachine authority = GameplayMachineBase.SpawnGameplayMachine(
    machineKey,
    settings,
    MyGameModule.Instance);

authority.EnableNetworking(
    LiteNetLibGameplayNetworkTransport.CreateServer(
        new IPEndPoint(IPAddress.Any, 7777)),
    new GameplayNetworkOptions
    {
        InitializePlayerController = (machine, connection, playerController, connectionData) =>
            InitializePlayer(machine, playerController, connectionData),
    });
```

Calling `DisableNetworking()` stops accepting clients without deleting or replacing Authority state. Networking may be enabled again later with a new transport.

## Connect a ClientProxy

```csharp
GameplayMachine client = GameplayMachineBase.CreateClientProxy(
    machineKey,
    settings,
    MyGameModule.Instance);

client.ConnectNetworking(
    LiteNetLibGameplayNetworkTransport.CreateClient("127.0.0.1", 7777),
    new GameplayNetworkOptions
    {
        ConnectionData = playerLoginToken,
    });
```

Drive both roles from the host application's update loop:

```csharp
authority.UpdateNetwork();
client.UpdateNetwork();
```

`UpdateNetwork()` polls the transport and applies received state on the calling thread; the transport does not mutate a GameplayMachine from a background thread. The ClientProxy becomes usable when `NetworkState == GameplayNetworkState.Ready`. The handshake rejects generated OD schemas that do not match.

A successful handshake loads a connection-specific snapshot. Subsequent Authority changes are coalesced by object and field during an execution, then replicated in revision order. Configure replication in OD Studio; changing it changes the generated network schema ID.

## PlayerController and ownership

Every GameplayMachine has an engine-managed local `PlayerController`. An Authority creates its own controller during startup and invokes `OnPlayerJoin` once for it. Each accepted client connection gets another controller before the initial snapshot is sent. `PlayerController` cannot be created by game code or used as an OD base class.

Use these getters from either a machine or its proxy:

```csharp
PlayerController localPlayer = machine.GetLocalPlayerController();
IEnumerable<PlayerController> players = machine.GetPlayerControllers();
```

`GameplayNetworkOptions.InitializePlayerController` initializes a newly created remote controller from authenticated `ConnectionData`. The legacy `ResolvePlayerController` hook may return an existing engine-managed controller restored from a save. The client receives its binding during the handshake. On the Authority, `GetPlayerController(connection)` resolves a specific connection.

Ownership is a direct reference to a PlayerController, not a recursive ownership hierarchy. Transferring ownership updates visibility in the next replication frame.

`Pawn` is a built-in OD class that game classes may inherit. `Pawn.PlayerController` and `PlayerController.Pawn` are bidirectional `OwnerOnly` fields. Use the built-in Authority interfaces to maintain both fields and ownership atomically:

```csharp
machine.Execute(new PossessParam
{
    PlayerController = machine.GetLocalPlayerController(),
    Pawn = pawn,
});

machine.Execute(new UnPossessParam
{
    PlayerController = machine.GetLocalPlayerController(),
});
```

To handle player session lifecycle in a custom machine, override the protected callbacks and configure the machine factory:

```csharp
sealed class MyGameplayMachine : GameplayMachine
{
    protected override void OnPlayerJoin(PlayerController playerController) { }
    protected override void OnPlayerDisconnected(PlayerController playerController) { }
}

var settings = new GameplayMachineSettings
{
    GameplayMachineFactory = () => new MyGameplayMachine(),
};
```

For the Authority's local player, `OnPlayerJoin` runs during machine startup. For remote players, it runs after the initial state is sent. `OnPlayerDisconnected` runs on the Authority before the connection's PlayerController is unbound and deleted, so its gameplay data remains readable during the callback.

GameplayObject replication modes are:

- `AllClients`: the object is included for every ready client.
- `OwnerOnly`: the object exists only on the client whose PlayerController owns it.

Field replication modes are:

- `None`: the field is never synchronized.
- `AllClients`: the field is synchronized whenever its containing object is visible.
- `OwnerOnly`: the field is synchronized only to the object's owner.

The framework intentionally does not provide `SkipOwner` or simulation/interpolation modes. GameplayMachine synchronizes authoritative gameplay data; prediction smoothing and interpolation belong to the presentation layer.

## RPC

Every OD interface is invoked through the same API:

```csharp
CheckableInterfaceResult result = client.Execute(param);
```

Its OD `RPC Mode` defines routing:

- `None` executes on the current machine and may have output fields.
- `Authority` executes locally on an Authority. A ClientProxy sends it to the Authority and returns immediately. It may declare output fields, but those values are only available to an Authority caller; reading the generated `Result` after a ClientProxy call throws.
- `Multicast` can only be invoked by an Authority and executes on that Authority and every ready ClientProxy. It may declare output fields, but only the Authority's local result is retained; receiver-side results are ignored. Invoking it directly on a ClientProxy throws.
- `Predictable` executes `CanExecute` and `OnPredictClient` immediately on a ClientProxy. The Authority then executes `CanExecute` and, if allowed, `DoExecute`. Only the invoking client receives the result and runs `OnSuccessClient` or `OnFailClient`.

Predictable callback parameters are a generated-codec snapshot captured before `OnPredictClient`, so they retain their invocation-time values. Authoritative field replication is sent before the success response on the reliable ordered channel. Pending predictions fail when the client disconnects or disables networking.

The Authority can restrict Authority and Predictable calls with `GameplayNetworkOptions.AuthorizeServerRpc`. RPC interfaces cannot be routines, and Predictable interfaces cannot have output fields. Client-visible business results should be represented by replicated GameplayObject fields rather than RPC return values.

During an Authority or Predictable RPC, `machine.CurrentRpcContext` identifies the calling connection and its PlayerController. Nested `Execute` calls retain that context for the duration of the RPC. Locally initiated Authority executions have no RPC context.

OD interfaces marked `PlayerInput` receive the trusted executing `PlayerController` as an additional generated `CanExecute` and `DoExecute` argument. For remote Authority and Predictable calls, this is resolved from the authenticated connection; it is never serialized as an interface input. Local execution uses the machine's local PlayerController. PlayerInput interfaces cannot use Multicast mode.

Routine interfaces are not exposed as network RPCs. Long-running network behavior should be started by an Authority interface and advanced by Authority state.
