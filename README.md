# GameplayMachine

GameplayMachine is a C# gameplay framework and desktop editor. Define your data model,
resources, scenes, and gameplay interfaces in the editor, and export gameplay code for
C#, Unity, or Godot. Keep rendering and input presentation in the engine.

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
