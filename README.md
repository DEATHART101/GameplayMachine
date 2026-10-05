# GameplayMachine

**A different way to build games: build the gameplay first, then give it a presentation.**

[YouTube: Eduard's GameplayMachine channel](https://www.youtube.com/@eduard-123-k1j)
| [Download the editor](https://github.com/DEATHART101/GameplayMachine/releases)

GameplayMachine is a C# gameplay framework and desktop editor that separates **gameplay**
from **display**. Your rules, objects, resources, scenes, and gameplay operations form an
independently runnable GameplayMachine. Unity, Godot, or a C# application provides the
visuals, sound, UI, and input presentation around it.

You finish and test the gameplay in GameplayMachine Editor first, without needing a game
scene full of visual objects. Then export it and connect **displayers** to gameplay objects.
A displayer reacts to data changes instead of owning the rules or polling every frame.

## From gameplay to screen

1. **Define the game.** Create types, fields, resources, scenes, and interfaces in the
   editor. Write the rules in the generated interface implementation files.
2. **Run the gameplay.** Test it with the editor runner and inspect its state and interface
   executions in the gameplay debugger, before adding a presentation layer.
3. **Connect the presentation.** Export to C#, Unity, or Godot. A runner hosts the machine;
   a binder associates gameplay objects with views; displayers update those views when
   their bound data changes. Player input goes back through gameplay interfaces.

For example, define a `Hero` class with an `int Health` field and a gameplay interface
named `Damage` with inputs `Target` (`Hero`, marked `NotNull`) and `Amount` (`int`).
In a project named `MyGame`, its generated implementation file can contain:

```csharp
using System;
using ODCore;

namespace MyGame;

public partial struct DamageParam
{
    private static EventError CanExecute(MyGameGameplayMachineProxy machine, DamageParam input)
    {
        if (input.Amount <= 0)
            return "Damage must be positive.";
        if (input.Target.Health <= 0)
            return "Hero is already defeated.";
        return true;
    }

    private static void DoExecute(
        MyGameGameplayMachineProxy machine, DamageParam input, ref DamageResult outResult)
    {
        input.Target.Health = Math.Max(0, input.Target.Health - input.Amount);
    }
}
```

That code knows nothing about sprites, scenes in a rendering engine, or health bars.
After exporting to Unity, attach this view script to a prefab and configure the binder
to associate it with `Hero` objects:

```csharp
using MyGame;
using UnityEngine;
using UnityEngine.UI;

public sealed class HeroDisplayer : HeroDisplayerBase
{
    [SerializeField] private Slider healthBar;

    protected override void OnHealthChanged(int value)
    {
        healthBar.value = value;
    }
}
```

`HeroDisplayerBase` is generated from your model. It subscribes to `Health` changes,
triggers the callback with the current value when binding, and manages subscription
cleanup when the view is unbound. Set the slider's range in Unity; write only the visual
response here, not another copy of the damage rules.

```text
Gameplay interface -> Hero.Health changes -> OnHealthChanged -> Health bar updates
```

With state synchronization configured, an authoritative change replicated to a client
reaches the same field-change callback. The view does not need a separate networking
implementation. The same gameplay can also drive a Godot view or a text-based C# view;
the presentation changes, not the rules.

This is the workflow GameplayMachine is built around: **a complete, testable game model
first; an engine-specific presentation second.** Networking and saves operate on the
gameplay data, while displayers concentrate on how that data looks and sounds.

## Framework features

The framework includes authoritative state synchronization, deterministic lockstep with a
relay server, generated save/load serialization, observable collections, and a remote
gameplay debugger.

This is an early preview. APIs and exported formats can change.

## Build and run

Install the .NET 10 SDK and .NET 8 runtime. PowerShell 7 is used by the scripts.
The Windows editor is the currently tested desktop distribution.

```powershell
git clone https://github.com/DEATHART101/GameplayMachine.git
cd GameplayMachine
pwsh ./scripts/build.ps1
dotnet run --project od-manipulator/ODManipulator/ODStudio.Editor
```

All required first-party dependency source is in this repository. Restore uses public
NuGet.org packages only; no sibling private repositories are needed.

Run the test suites:

```powershell
pwsh ./scripts/test.ps1
```

Build the optional Godot adapter with:

```powershell
dotnet build gameplaymachine_core/main/GameplayMachineGodot/GameplayMachineGodot.csproj
```

It targets Godot .NET 4.7.2. Use the .NET edition of Godot, not the standard edition.

## Downloads

Preview binaries are published under [Releases](https://github.com/DEATHART101/GameplayMachine/releases).
Extract the entire archive and launch `editor/GameplayMachine.Editor.exe`.
Do not move the executable out of the extracted tree: Godot export locates the bundled
runtime source there.

The Windows editor is self-contained, but code generation, workspace compilation,
runner builds, and exports still require the .NET SDK. Install the .NET 8 runtime
to run generated command-line games. Unity/Godot editors are separate installations.

## Repository layout

- `gameplaymachine_core/main/GameplayMachineCore`: gameplay runtime, networking, saves, debugger.
- `gameplaymachine_core/main/GameplayMachineGodot`: shared Godot runner, binder, and displayer.
- `od-core/main/ODCore`: shared OD types and metadata.
- `xlockstep`: deterministic frame scheduling, protocol, and tests.
- `od-manipulator/ODManipulator/ODStudio.*`: editor, generators, exporters, CLI, and tests.
- `dependencies`: source for the first-party utility libraries actually used by the runtime.
- `scripts`: repeatable build, test, and release packaging commands.

Original component directory names are retained for generator/runtime path compatibility.
Old DLL-container generators and the separate SaaS/DB stack are not included in the
supported build. Examples are being prepared separately and are not part of this repository.

## Release packaging

```powershell
pwsh ./scripts/package-editor.ps1 -Version 0.1.0-preview.1
pwsh ./scripts/test-published-editor.ps1 -EditorDirectory artifacts/GameplayMachine-Editor-0.1.0-preview.1-win-x64/editor
```

Package IDs and assembly versions inside the runtime retain their existing component
versions. The archive version is the suite release version. No NuGet packages are
automatically published.

## License

MIT, copyright Eduard Xue. See [LICENSE](LICENSE).
Third-party dependencies retain their own licenses; see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
