# OD Manipulator

The repository contains the legacy reflection generator and the new OD Studio toolchain. New projects do not use OD DLLs.

## OD Studio

OD Studio is the source editor and package compiler for the final OD format:

- `ODStudio.Model`: stable-ID schema, static data, validation, and operation history.
- `ODStudio.Package`: Git-friendly `.odproject` directory storage and binary `.odpkg` compilation/loading.
- `ODStudio.Generator`: deterministic `.odpkg` to GameplayMachine C# generation.
- `ODStudio.Editor`: Avalonia desktop editor for Windows, macOS, and Linux.
- `ODStudio.Cli`: project creation, validation, package build, inspection, and C# generation.
- `ODStudio.Tests`: source/package round-trip, integrity, validation, stable-ID, generated-code compilation, and module-registration tests.

An `.odproject` is a directory containing `project.json`, `schemas`, `data`, `scenes`, `code`, `history`, and `editor`. The `code` directory is the authoritative home for interface, driven-field, resolver, and GameplayMachine runtime implementations. A compiled `.odpkg` includes those sources in its `CODE` chunk alongside the model data, with per-chunk CRC32 and a package SHA-256 footer.

Names are editable symbols. Project, definition, field, data-set, and record identities are permanent GUIDs. Renaming never changes runtime identity.

There is no OD DLL importer, legacy save reader, or data migration pipeline.

The runtime does not load `.odpkg`. The build pipeline compiles an `.odproject` to `.odpkg`, then generates ordinary C# from that package. The generated module is registered with `GameplayMachineBase.RegisterODModule` and a normal `GameplayMachine` remains the runtime container. Type and field save IDs are derived from permanent package/type/field GUIDs, so symbol renames do not change persistence identity.

### Run the editor

```powershell
dotnet run --project ODManipulator/ODStudio.Editor
```

Pass an existing project directory to open it directly:

```powershell
dotnet run --project ODManipulator/ODStudio.Editor -- D:\Game\GameData.odproject
```

Use the inspector's `Edit Code`, `Edit Driven Code`, `Edit Runtime`, and `Edit Resolvers` actions to open project-owned sources in Visual Studio Code. Press F5 or use the title-bar Run button to build the current model, start it as Offline, Authority Host, or Client Proxy, and attach the Gameplay Debugger automatically.

### CLI

```powershell
dotnet run --project ODManipulator/ODStudio.Cli -- new D:\Game\GameData.odproject GameData Game.OD
dotnet run --project ODManipulator/ODStudio.Cli -- validate D:\Game\GameData.odproject
dotnet run --project ODManipulator/ODStudio.Cli -- build D:\Game\GameData.odproject D:\Game\Build\GameData.odpkg
dotnet run --project ODManipulator/ODStudio.Cli -- inspect D:\Game\Build\GameData.odpkg
dotnet run --project ODManipulator/ODStudio.Cli -- generate D:\Game\Build\GameData.odpkg D:\Game\Generated\GameData
```

The editor's `Generate C#` command runs the same package-based generator. It emits the generated type, behavior, module, resource, scene, and runtime files plus a complete copy of project-owned gameplay sources. Every gameplay source in the destination is overwritten on the next export; game-engine projects should keep only presentation code outside this output.
