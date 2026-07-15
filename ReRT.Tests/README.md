# ReRT.Tests

Unit tests for the ESAPI-free domain code in `ReRT.csproj`. xUnit + FluentAssertions, targeting `net48`.

## Running locally

```sh
dotnet test
```

Or in Visual Studio: **Test → Run All Tests**.

## With coverage

```sh
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

Coverage XML lands under `TestResults/<guid>/coverage.cobertura.xml`. For a browsable HTML report, install `dotnet-reportgenerator-globaltool` and run:

```sh
reportgenerator -reports:TestResults/*/coverage.cobertura.xml -targetdir:coverage-html
```

## Test layout

| Folder            | Covers                                                          |
|-------------------|-----------------------------------------------------------------|
| `Constraints/`    | `Domain/Constraints/*` — operator parsing + evaluation          |
| `DoseMetrics/`    | `Domain/DoseMetrics/*` (uses `FakeDvhProvider` to skip ESAPI)   |
| `DoseConversion/` | `Domain/DoseConversion/*` — formulas, voxel transformer helpers |
| `Configuration/`  | YAML loaders, institution config, quantity parsing              |
| `Reporting/`      | Template renderer + section row-shape snapshot tests            |
| `Logging/`        | `PatientIdHasher`, `RunMetrics` structured-field projection     |

## What's not tested here

The following are validated by the manual Eclipse smoke test, not by unit tests:

- `Model.cs` — ESAPI-side-effect code (plan-sum creation, dose voxel writes).
- `Domain/DoseMetrics/EsapiDvhProvider.cs` — thin ESAPI `DVHData` adapter.
- `Domain/DoseConversion/VoxelTransformer.Apply` — needs ESAPI `Structure`/`Dose`/`Registration` objects. The pure-math helpers (`ApplyFormulaToVoxel`, `Dilate26Connected`, `Dilate6Connected`, `ClampToGrid`) are unit-tested directly; `ResetClaims` is reached via the `InternalsVisibleTo` seam on the claim grid.
- `Reporting/DocxWriter.cs` — Spire.Doc/`System.Drawing`-coupled.
- `Reporting/DvhPlotter.cs` — `System.Drawing`-coupled and consumes ESAPI `DVHData`.
- `Views/*.xaml` and WPF code-behind.

## Coverage targets

≥ 80% line coverage on each of:

- `Domain/Constraints/**`
- `Domain/DoseMetrics/**` (excluding `EsapiDvhProvider.cs`)
- `Domain/DoseConversion/**` (excluding ESAPI-adjacent paths in `VoxelTransformer.Apply` and the pipeline orchestrator)
- `Reporting/**` (excluding `DocxWriter.cs` and `DvhPlotter.cs`)
- `Configuration/Loaders/**`
- `Logging/**`

Namespaces falling below 80% are documented inline below with a one-sentence justification.

### Documented exceptions

- `Logging/StructuredLogger.WriteRunSummary` — the production write path drives the global `Serilog.Log.Logger` static, so the line-coverage figure for `StructuredLogger` may sit lower than the per-namespace target. The structured payload itself is unit-tested via `RunMetrics.ToStructuredFields` in `Logging/RunMetricsTests.cs`.
- `Domain/DoseConversion/VoxelTransformer.Apply` — entry point requires ESAPI `Structure` / `Dose` / `Registration` objects that can't be constructed off-Eclipse. The pure helpers it composes (`ApplyFormulaToVoxel`, `Dilate*Connected`, `ClampToGrid`, `ResetClaims` via internal seams) are all covered; `Apply` itself is validated by the Eclipse smoke test.

## Adding tests

- Put new tests under the matching folder above.
- Prefer FluentAssertions (`x.Should().Be(...)`) — the existing tests use it consistently.
- For section/report tests, build a fixture via the `BuildFixture()` helper in `Reporting/SectionSnapshotTests.cs` and mutate as needed.
- For ESAPI-free Domain tests, never reach into ESAPI types — extract pure helpers if a piece of logic isn't testable as-is.
