# Security model

CodeSpace 0.1.0 is a development preview, not a security-qualified IDE distribution. Do not execute untrusted extensions or use it to store production secrets.

VSIX inspection validates paths, duplicates, entry counts and expansion bounds without extracting or executing. It does not verify signatures, publishers, licenses or malicious behavior. Activation requires separate explicit trust.

**A worker is not a security sandbox.** Extension code can access workspace data exposed by the host and the network. A desktop Node process has the current user's operating-system authority. No registry dependencies or install scripts are automatically installed. Filesystem/process/remote/webview/authentication additions require new threat-model review.

Recovery is unencrypted local storage, not a backup. Export important content. Browser recovery uses localStorage and may fail on quota or privacy restrictions; the UI reports failures. `?e2e=1` intentionally exposes read-only document snapshots to same-origin test JavaScript and must not be used for sensitive documents. It has no command execution endpoint.

Report vulnerabilities privately through GitHub Security Advisories when enabled, or contact the maintainer privately before posting exploit details. Include affected commit, platform, reproduction and whether extension trust was granted. No support-time guarantee is implied.
