# Performance measurements

## Reproducible CPU/raster comparison

Run `dotnet run --project tests/CodeSpace.FeatureTests -c Release` on Linux with the pinned Skia native package. The suite emits `artifacts/performance.json`; both build workflows retain it. The rendering test uses one process, one 1100 × 700 raster surface per path, the same font/text and the same cached layouts. It compares the original scalar per-cluster draw path with the new positioned-text batches. This isolates submission overhead; it is not a hardware-GPU or complete workbench benchmark.

Initial measured result: [Build run 36347991055](https://github.com/wieslawsoltes/CodeSpace/actions/runs/36347991055), source commit `29d0d1d4b684c7de7f6ecba4cba07704faaec8ce`, September 27, 2026. The performance-engine artifact contains the original machine-readable evidence.

| Measurement | Scalar/materialized reference | Indexed/batched path |
|---|---:|---:|
| Text draw calls per frame | 2,450 | 630 |
| CPU raster time, 100 repeated frames | 1,309.235 ms | 134.3579 ms |
| Differing output pixels | Reference image | 0 of 770,000 |
| Allocations for line lookup on a 2-million-character line | 40,000,240 bytes for **10** materialized lookups | 0 bytes for **10,000** indexed lookups |
| Cached layout rebuilds during repeated painting | — | 0 |

The measured raster time ratio is approximately **9.74×** for this fixture; text draw calls fell by **74.3%**. These are runner-specific measurements, not guaranteed application speedups. Different operation counts in the allocation test intentionally demonstrate that indexed navigation does not allocate the full line; they must not be used to infer a timing speedup. The optimized path still allocated about 2 KiB per frame in this fixture. Every new workflow run publishes its own results, which may vary.

## Changes behind the measurements

Line bounds and position lookup use cached rope metadata without allocating strings. Slice/copy operations write into the destination span directly. Editor sessions share saved buffer snapshots instead of flattening documents on creation. The renderer caches ASCII advances, positioned text blobs, and minimap samples; binary searches replace sequential hit-test scans. A 512-entry layout cache disposes native blobs on eviction. Folding uses memory proportional to collapsed intervals instead of a mapping array for every line.

Extension synchronization now sends changed-document snapshots rather than reserializing every document after each request. A live timer runs only after explicit host activation. Unchanged JS document indexes and editor adapters are reused. This transport improvement is covered by behavior tests, not a reported throughput benchmark.

## Limits

No physical-GPU timing, power, full-frame input latency, whole-application comparison against VS Code, or fastest-renderer claim is established. Unicode text still uses the cluster path; complete shaping, bidi and font fallback remain work. Very long lines, cold distant lexical jumps, large search result sets and full-workspace recovery serialization remain optimization targets. The scalar switch is retained as a regression reference, not a second UI implementation.
