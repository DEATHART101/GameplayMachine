# Godot C# Integration

Reference `GameplayMachineGodot.csproj` from a Godot .NET project. Keep these runtime classes together; game-specific runners and views belong in the game project.

## Runner

Derive from `GameplayMachineRunner` and provide the generated OD runtime's modules and settings. Root and initial-scene creation are defined by the OD project and happen automatically for a new Authority machine. Loading a save restores its existing world instead.

Inspector exports configure startup role, UDP connection, save location, partitioned saves and diagnostics. `OnMachineStarted`, `OnMachineProcess`, `OnMachineStopping` are game extension points. `MachineStarted` and `MachineStopping` signals serve other nodes. All machine work is pumped on the scene-tree thread.

The legacy Variant field signals are opt-in through `EmitObjectFieldSignals`; C# displayers do not need them or full-world refresh polling.

## Binder

Add `GameplayObjectBinder`, assign its Runner, optional ViewParent and binding rules. A rule maps a gameplay class (not a class resource) to a PackedScene and can include derived classes. Class hints are discovered from loaded generated OD types. Code can also set ClassName and ViewFactory.

The binder creates views for existing/new/available objects and returns bindings before freeing views on deletion, unavailability or machine stop. It binds GameplayObjectDisplayer nodes found under the instantiated view.

## Displayer

```csharp
public partial class HeroView : TypedGameplayObjectDisplayer<Hero>
{
    protected override void OnGameplayObjectBound() { }

    private void OnHealthChanged()
    {
        // Read Target.Health here. Target and Machine live in the base class.
    }

    private void OnItemsChanged() { }
    private void OnItemsItemChanged(CollectionItemChangeType kind, Item key) { }
}
```

Named scalar/collection Changed methods run once on binding and thereafter on field notifications. Set/Map item callbacks run on actual item changes, with the map key as the second argument. Handles are returned automatically. `OnFieldChanged` is a generic fallback; custom bindings can use `Bind`, `BindItems` or `TrackBinding`.

Callback discovery uses reflection and caches methods in each binding. There is no per-frame scan of fields or scene objects. Trimming/AOT exports have not been validated; this implementation targets desktop Godot .NET.

See `../../samples/BlockWorld` for a runnable game and native-engine verification.
