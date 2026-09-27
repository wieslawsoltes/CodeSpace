# Contributing

Keep portable engines free of UI dependencies. Features need a reusable API, deterministic tests and real workbench integration. Preserve UTF-16 offsets and line endings; validate complete edit batches before mutation. Do not replace the custom editor with Monaco or embed the original VS Code workbench.

Run both C# suites and independent extension tests. UI changes need browser interaction coverage and screenshots. Performance claims need measurements, distinguishing CPU work from GPU execution. Update the compatibility matrix with every new feature and failure boundary.

Extension changes require success/failure/lifecycle tests and review of execution/data authority. Do not silently pretend an unsupported API succeeds. Never run extension code during inspection or import. Do not add Marketplace scraping, proprietary assets or unlicensed fixtures. Use focused commits and explicit validation notes; release artifacts remain drafts until reviewed.
