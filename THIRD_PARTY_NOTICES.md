# Third party dependencies

The MIT license at the repository root covers the first-party source in this repository.
It does not relicense third-party packages obtained from NuGet.

The active runtime uses LiteNetLib and Newtonsoft.Json. The desktop editor uses Avalonia
and its rendering, font, and platform dependencies. Tests use NUnit, xUnit, and Microsoft
test tooling. The optional Godot adapter uses the Godot .NET SDK.

Release archives include third-party license material under `licenses/` and the .NET
runtime's license/notice files alongside the self-contained editor. Consult the package
metadata and upstream notices for the exact terms for each component.

No third-party dependency source has been incorporated into the first-party utility
directories. Existing copyright notices in imported source must be retained.
