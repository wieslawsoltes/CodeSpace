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
```

The suite exercises startup, quick-open, typing/Undo, selection/deletion/Redo, multi-cursor edits, split groups, sidebar toggles, trusted worker execution and recovery. Additional workflows exercise folding, extension completion insertion/Undo, formatting/Undo, hover/symbol controls, valid/invalid live settings and dirty/settings recovery.

Pointer/keyboard input and native input events drive the actual workbench. Trust buttons use their accessibility invocation. Read-only state verifies outcomes rather than assuming a click succeeded. Screenshots, logs and failures are retained under artifacts/browser-tests. Diagnostic mode ?e2e=1 exposes document snapshots; do not use it with sensitive content. Headless SwiftShader is not physical-GPU qualification.

## Engine and host tests

The C# suites cover core regression, 10,000 seeded rope differential edits, VSIX/RPC/language behavior, atomic multi-file validation/version/observer behavior, rectangular selections, folding-map differential tests, JSONC settings, allocation-free navigation and reference raster comparisons. FeatureTests writes artifacts/performance.json; workflows retain it. Timings describe CPU/raster work only.

The independent host tests cover API values, activation, module loading, provider selection/cancellation/stale results, diagnostic ownership, configuration precedence/events and changed-document synchronization. Seven browser platform contracts exercise production modifier handling; three subprocess tests launch the actual Node host and test activation/UI ordering, UTF-16/versioned edits, disposal and explicit trust. The platform-contracts runner imports those three tests; do not count them twice.

## Packages

```sh
for project in Core Editor Docking Languages Extensions Rendering.Skia Controls.Uno Workbench.Uno; do
  dotnet pack "src/CodeSpace.$project/CodeSpace.$project.csproj" -c Release -o artifacts/packages
done
npm pack ./src/CodeSpace.ExtensionHost --pack-destination artifacts/packages
python3 tools/verify-packages.py artifacts/packages
```

Install wasm-tools before packing both Uno targets. Verification checks eight library IDs, assembly payloads including both Uno targets, README/license metadata, consistent versions, required npm modules and checksums. No public registry push is automatic.

## Workflows

Build and test runs the portable suites and extension host, retains performance metrics and compiles desktop on Windows/macOS/Linux. Browser and Pages publishes WebAssembly, runs all portable/platform/process/Chromium gates, packages reusable libraries and deploys only successful main builds. It verifies the public page, worker and commit metadata over HTTPS. PRs do not deploy.

Release runs on v* tags, validates suites, packs libraries and produces browser/framework-dependent desktop archives with checksums and a draft prerelease. The npm version is aligned with the tag. The workflow definition is not evidence of an executed release; signing, notarization and production distribution qualification are separate work. No credentials or signing secrets are embedded.
