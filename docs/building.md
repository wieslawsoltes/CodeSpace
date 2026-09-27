# Build, test and release

Use .NET 10 and the pinned Uno SDK in `global.json`. `CodeSpace.slnx` contains the six portable libraries and two executable regression suites. `CodeSpace.Uno.slnx` contains the app and two reusable Uno libraries. Browser builds require the wasm-tools workload; desktop targets require Uno's normal native platform dependencies.

```sh
dotnet build CodeSpace.slnx -c Release
dotnet run --project tests/CodeSpace.Tests -c Release --no-build
dotnet run --project tests/CodeSpace.ProtocolTests -c Release --no-build
npm test --prefix src/CodeSpace.ExtensionHost
node --test tests/browser/platform-contracts.test.mjs

dotnet run --project src/CodeSpace.App -f net10.0-desktop \
  -p:CodeSpaceTargetFrameworks=net10.0-desktop
```

Desktop executable extensions require Node.js 22+ on PATH. CodeSpace does not silently download it. Browser extensions use a same-origin module worker. Neither host is a security sandbox.

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

Open `http://localhost:4173/CodeSpace/`. Never open the app with `file://`. A root deployment can omit the base-path property. The staging tool copies all Uno output, the extension modules, `.nojekyll` and commit metadata.

## Input and extension transport regressions

`EditorInputBridge` is a reusable Uno `TextBox` subclass that gives the custom document first refusal of editing keys. Handled commands do not enter the input control's own history or navigation. Unhandled character input continues through Uno's platform input path. This prevents native Ctrl+Z handling from consuming the document's Undo command. Browser modifier state is captured from trusted DOM events and cleared on blur/hidden-page transitions, so focus transfers and synthetic accessibility replays do not leave Control stuck.

Seven platform contract tests exercise the actual JavaScript embedded in the production browser adapter. Three additional tests spawn the actual Node extension host as a child process and verify activation/UI-request ordering, built-in module resolution, command disposal, UTF-16 edit requests, explicit trust rejection and recovery after unsupported API errors. These are process/transport tests, not a claim of full native-desktop UI or third-party extension qualification. Run the combined ten tests with the platform command above, or the process tests alone:

```sh
node --test tests/extension-host/process.test.mjs
```

## Real browser tests

```sh
npm install --no-save playwright@1.55.1
npx playwright install --with-deps chromium
BASE_URL=http://127.0.0.1:4173/CodeSpace/ node tests/browser/smoke.mjs
```

Nine workflow checks cover startup, quick-open, typing/Undo, selection/deletion/Redo, atomic multi-cursor editing, split groups, sidebar toggling, the trusted browser-extension probe and recovery after reload. The tests use real pointer and keyboard input; the probe's trust button uses its accessibility invocation. Read-only diagnostic state verifies the resulting documents and layouts rather than assuming a click succeeded. Screenshots, failure HTML and logs are retained under `artifacts/browser-tests`. `?e2e=1` exposes document snapshots for these checks; do not enable it with sensitive data. Headless SwiftShader is not physical-GPU qualification.

## Packages

```sh
for project in Core Editor Docking Languages Extensions Rendering.Skia Controls.Uno Workbench.Uno; do
  dotnet pack "src/CodeSpace.$project/CodeSpace.$project.csproj" -c Release -o artifacts/packages
done
npm pack ./src/CodeSpace.ExtensionHost --pack-destination artifacts/packages
```

Install wasm-tools before packing both targets of the Uno libraries. No public NuGet/npm push is automatic.

## Workflows

`build.yml` builds portable libraries, runs both C# suites and JavaScript tests, and compiles the desktop app on Windows, macOS and Linux. `pages.yml` publishes the WebAssembly app, runs engine/input/process/Chromium tests, creates eight NuGet packages plus the npm archive and source archive, and deploys successful main builds. After deployment it reads the public application, browser worker and build metadata over HTTPS, requiring the published commit to match the workflow commit. Pull requests do not deploy.

`release.yml` runs on `v*` tags. It validates engines, packs libraries, creates browser and framework-dependent desktop archives, computes checksums and opens a **draft prerelease** for review. A workflow definition is not an executed release: tagged release execution, signing, notarization and production distribution qualification are separate gates. No registry credentials or signing secrets are embedded.
