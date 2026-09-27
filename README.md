# CodeSpace

A modular, independently branded Visual Studio Code-style workbench for **Uno Platform 6.7** and **.NET 10**, with a custom editor and GPU-backed Skia rendering. Licensed under MIT.

**Development preview — not a complete or pixel-exact replacement for Visual Studio Code.** Extension API compatibility is incremental and must not be interpreted as support for every existing extension. See `docs/compatibility.md` as components land.

## Architecture

The text engine, editing model, language services, docking model, rendering engine, extension protocol and Uno controls are separate reusable libraries. No Monaco editor, VS Code web workbench, Electron shell or embedded screenshot is used.

## Build

```sh
dotnet run --project tests/CodeSpace.Tests -c Release
dotnet build src/CodeSpace.App -f net10.0-desktop -c Release
dotnet publish src/CodeSpace.App -f net10.0-browserwasm -c Release
```

The application and full documentation are being committed in coherent implementation increments. See the Actions tab for actual validation results.

CodeSpace is not a Microsoft product and is not affiliated with Visual Studio Code or GitHub Codespaces. Visual Studio Code is a Microsoft trademark. Microsoft Marketplace access is not included.
