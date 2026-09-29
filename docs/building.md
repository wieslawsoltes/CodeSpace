# Build, test and release

Use .NET 10 and the pinned Uno SDK in global.json. CodeSpace.slnx contains six portable libraries and three executable regression suites. CodeSpace.Uno.slnx contains the app and two reusable Uno libraries. Browser builds require wasm-tools; desktop targets require Uno's native platform dependencies.

```sh
dotnet build CodeSpace.slnx -c Release
dotnet run --project tests/CodeSpace.Tests -c Release --no-build
dotnet run --project tests/CodeSpace.ProtocolTests -c Release --no-build
dotnet run --project tests/CodeSpace.FeatureTests -c Release --no-build
npm test --prefix src/CodeSpace.ExtensionHost
node --test tests/browser/platform-contracts.test.mjs

dotnet run --project src/CodeSpace.App -f net10.0-desktop \
  -p:CodeSpaceTargetFrameworks=net10.0-desktop
```

The raster suite uses the Linux Skia native package on Linux CI. Desktop extensions require optional Node.js 22+ on PATH; CodeSpace never downloads it automatically. Browser extensions use same-origin workers. Neither host is an untrusted-code sandbox.

## Browser / GitHub Pages

```sh
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/CodeSpace.App -f net10.0-browserwasm -c Release \
  -p:CodeSpaceTargetFrameworks=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/CodeSpace/ -o artifacts/browser
python3 tools/prepare-pages.py artifacts/browser artifacts/site
mkdir -p artifacts/serve
ln -s "$PWD/artifacts/site" artifacts/serve/CodeSpace
python3 -m http.server 4173 --directory artifacts/serve
```

Open http://localhost:4173/CodeSpace/ rather than file://. Omit the base-path property for root hosting. Staging copies the complete Uno output, all extension-host modules, .nojekyll and commit metadata.

## Browser interaction tests

```sh
npm install --no-save playwright@1.55.1
npx playwright install --with-deps chromium
BASE_URL=http://127.0.0.1:4173/CodeSpace/ node tests/browser/smoke.mjs
BASE_URL=http://127.0.0.1:4173/CodeSpace/ node tests/browser/view-recovery.mjs
```

The suite exercises startup, quick-open, typing/Undo, selection/deletion/Redo, multi-cursor edits, split groups, sidebar toggles, trusted worker execution and recovery. Additional workflows exercise folding, extension completion insertion/Undo, formatting/Undo, hover/symbol controls, valid/invalid live settings and dirty/settings recovery.

Pointer/keyboard input and native input events drive the actual workbench. Trust buttons use their accessibility invocation. Read-only state verifies outcomes rather than assuming a click succeeded. Screenshots, logs and failures are retained under artifacts/browser-tests. Diagnostic mode ?e2e=1 exposes document snapshots; do not use it with sensitive content. Headless SwiftShader is not physical-GPU qualification.

The main browser suite has fourteen workflow checks, including a hover-dialog-to-symbol-picker regression that asserts the command query never enters the document. A separate fresh-context recovery suite has four checks: caret-only persistence, fold-only persistence, scroll-only persistence without text edits, and restoring those states together after reload. It waits for the saved data rather than using a fixed autosave delay. Full IME, screen-reader and hardware-GPU qualification remain separate.

## Engine and host tests

The C# suites cover core regression, 10,000 seeded rope differential edits, VSIX/RPC/language behavior, atomic multi-file validation/version/observer behavior, rectangular selections, folding-map differential tests, JSONC settings, allocation-free navigation and reference raster comparisons. The continuation adds eight tests for incremental lexical convergence, queued edits, 300 randomized lexical edits, retained-layout raster equivalence, viewport notifications and captured-save baselines. FeatureTests writes artifacts/performance.json; workflows retain it. Timings describe CPU/raster work only.

The independent host tests cover API values, activation, module loading, provider selection/cancellation/stale results, diagnostic ownership, configuration precedence/events and changed-document synchronization. Seven browser platform contracts exercise production modifier handling; three subprocess tests launch the actual Node host and test activation/UI ordering, UTF-16/versioned edits, disposal and explicit trust. The platform-contracts runner imports those three tests; do not count them twice.

## Packages

```sh
for project in Core Editor Docking Languages Extensions Rendering.Skia Controls.Uno Workbench.Uno; do
  dotnet pack "src/CodeSpace.$project/CodeSpace.$project.csproj" -c Release -o artifacts/packages
done
npm pack ./src/CodeSpace.ExtensionHost --pack-destination artifacts/packages
python3 tools/verify-packages.py artifacts/packages
```

Install wasm-tools before packing both Uno targets. Verification checks eight library IDs, assembly payloads including both Uno targets, README/license metadata, consistent versions, required npm modules and checksums. Only version tags publish the NuGet packages (see below); the npm archive is never pushed to a registry.

## Workflows

Build and test runs the portable suites and extension host, retains performance metrics and compiles desktop on Windows/macOS/Linux. Browser and Pages publishes WebAssembly, runs all portable/platform/process/Chromium gates, packages reusable libraries and deploys only successful main builds. It verifies the public page, worker and commit metadata over HTTPS. PRs do not deploy.

Release runs on v* tags or a manually supplied version. It validates suites, packs libraries with symbols, and produces self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64; `CodeSpace-<version>-<rid>.zip`/`.tar.gz`), browser/source archives and `SHA256SUMS.txt`. The npm version is aligned with the tag. Tags attach all assets to a GitHub Release (prerelease when the version has a suffix such as `-preview.1`) and then publish the NuGet packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (GitHub OIDC via `NuGet/login`, no stored API key) from the protected `nuget` environment; only the `NUGET_USER` variable is required. Manual runs are dry runs: they build and upload every asset as workflow artifacts but publish nothing. Signing, notarization and production distribution qualification are separate work. No credentials or signing secrets are embedded.
