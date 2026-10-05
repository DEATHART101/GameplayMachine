# Gameplay Spatial Relevancy

Spatial relevancy is an optional Authority-side visibility filter. It intersects partition interest and the normal GameplayObject/field replication modes; it never changes object ownership, persistence, or partition membership. Classes using `Always` relevancy keep the existing replication behavior.

## OD Studio

Select a class and set `Relevancy` to `Spatial` in its inspector. Add every local or inherited field whose changes can alter the object's resolved position under `Spatial Dependencies`. C# export creates a preserved user file at:

```text
User/<Namespace>/SpatialRelevancy/<ClassName>.cs
```

Implement the generated `TryGet_<ClassName>` method using the class's own gameplay fields. The generated OD module supplies the provider and dependency metadata through `IODSpatialModule`; GameplayMachine discovers it automatically. Re-exporting does not overwrite the user implementation.

The lower-level registration API remains available for classes supplied by code rather than an OD package.

GameplayMachine does not require GameplayObjects to have position fields. The game registers a provider for each spatial class and converts its own data to a common three-dimensional `float` location:

```csharp
sealed class AvatarSpatialProvider : IGameplaySpatialProvider
{
    public ODClassName TargetClass => Avatar.ClassName;
    public IReadOnlyCollection<ODFieldName> Dependencies => new[] { Avatar.PoseFieldName };

    public bool TryGetLocation(
        GameplayMachine machine,
        CommonGameplayObject gameplayObject,
        out GameplaySpatialLocation location)
    {
        Avatar avatar = machine.GetGameplayObject<Avatar>(gameplayObject.ObjectID);
        location = new GameplaySpatialLocation(
            avatar.Pose.X.Value,
            avatar.Pose.Y.Value,
            avatar.Pose.Z.Value);
        return true;
    }
}
```

Register code-defined providers when creating the machine:

```csharp
var settings = new GameplayMachineSettings
{
    SpatialCellSize = 32f,
    SpatialProviders = new List<IGameplaySpatialProvider>
    {
        new AvatarSpatialProvider(),
    },
};
```

`Dependencies` identifies fields that can change the resolved location. GameplayMachine refreshes the indexed object after those fields change, including collection mutations. A provider applies to its target class and derived classes; an exact provider takes precedence. Returning `false` temporarily removes the object from the spatial index.

## Player Interest

Set a PlayerController's radius around an explicit origin:

```csharp
machine.SetSpatialInterest(player, new GameplaySpatialLocation(x, y, z), radius);
```

Or follow its currently possessed Pawn:

```csharp
machine.SetSpatialInterest(player, radius);
```

The Pawn must itself resolve through a spatial provider. `ClearSpatialInterest(player)` removes the filter. These are Authority mutations and use the normal execution rules. A spatial object owned by the viewing PlayerController remains visible even when it is outside the interest radius.

Use `GameplaySpatialSpaceID` when coordinates from separate worlds must not interact:

```csharp
var world = new GameplaySpatialSpaceID("overworld");
var location = new GameplaySpatialLocation(world, x, y, z);
```

Only locations in the same space can be relevant.

## Runtime Behavior

The Authority maintains a uniform three-dimensional cell index. Position changes update one index entry and re-evaluate that object for connected players. Interest changes query nearby cells and compare them with objects already known by that player. Exact range checks use squared `float` distance. No spatial state is added to saves or network messages: source fields remain the authoritative data, and the index is rebuilt after loading.

Entering range uses the existing object-created/full-state replication path. Leaving range sends object unavailability, so references resolve to null until the same object becomes visible again. The ClientProxy receives only relevant objects and does not maintain an authoritative spatial index.
