# KoreForge.Logging Developer Guide

This document explains how the solution is structured, how it is built and tested, and what you need to know when extending or maintaining the package.

## Solution Layout

```
KoreForge.Logging.slnx
├─ src/
│  ├─ KoreForge.Logging.Runtime/           # Public runtime assembly + packaged analyzers/generator
│  ├─ KoreForge.Logging.Generator/         # Incremental source generator (netstandard2.0)
│  └─ KoreForge.Logging.Analyzers/         # Diagnostic analyzers + release tracking (netstandard2.0)
├─ tst/
│  └─ KoreForge.Logging.Tests/             # xUnit tests covering runtime, generator, analyzer
├─ doc/                                # Specification + guides, packed into NuGet
├─ artifacts/                          # NuGet/snupkg output (git ignored)
└─ TestResults/                        # Test + coverage output (git ignored)
```

The `KoreForge.Logging.Runtime` package references the generator and analyzer projects as analyzers, so consumers get all three components from one NuGet.

## Target Frameworks & Dependencies

- Runtime: `net10.0`. Update this if multi-targeting is needed.
- Generator/Analyzer: `netstandard2.0` to keep Roslyn happy (RS1041). Switching to higher TFMs requires disabling that diagnostic or multi-targeting.
- Tests: `net10.0`, referencing Coverlet collector, Microsoft.NET.Test.Sdk, xUnit.

## Analyzer Release Tracking

Keep `src/KoreForge.Logging.Analyzers/AnalyzerReleases/AnalyzerReleases.(Shipped|Unshipped).md` up to date whenever diagnostics change. The analyzer project treats RS2008 as errors.

## Adding Features / Fixes

1. Update or add tests under `tst/KoreForge.Logging.Tests`.
2. Run `scr/build-test.ps1` (or `scr/build-test-codecoverage.ps1` if you want HTML output).
3. Update docs (spec, user, developer guides) when behavior or requirements change.
4. Bump tags per MinVer conventions before releasing.

## Release Checklist

1. `scr/build-clean.ps1`
2. `scr/build-test-codecoverage.ps1`
3. Inspect `TestResults/KoreForge.Logging.Tests/coverage-html/index.html`
4. `scr/build-pack.ps1`
5. Push artifacts from `artifacts/` to NuGet feed (or internal store)
6. Tag commit `KoreForge.Logging/vX.Y.Z` so MinVer picks it up.

## Troubleshooting Notes

- If coverage HTML fails to generate, verify `ReportGenerator` package restored and `dotnet exec` path points to `tools/net10.0/ReportGenerator.dll`.
- If analyzer build errors about release tracking, edit the `.md` files instead of deleting them.

