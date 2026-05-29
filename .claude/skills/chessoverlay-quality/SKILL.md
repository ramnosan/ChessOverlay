---
name: chessoverlay-quality
description: Run the ChessOverlay.Quality analysis suite (DRY duplication, CRAP risk, architecture view) and report results. Use when the user asks to run quality tools, code quality / health checks, duplication / complexity / coverage analysis, or the architecture report for this F# repo.
---

# ChessOverlay.Quality

`ChessOverlay.Quality` is a custom F# CLI (`ChessOverlay.Quality/`) that runs three analyses over the repo. This skill runs all three and reports the results.

## Steps

Run from the repository root (the directory containing `ChessOverlay.slnx`).

### 1. Build the quality tool once

```bash
dotnet build ChessOverlay.Quality/ChessOverlay.Quality.fsproj -c Release -v quiet
```

Then pass `--no-build` to the `dotnet run` calls below so each command does not rebuild.

### 2. DRY — duplicate code detection

```bash
dotnet run --project ChessOverlay.Quality -c Release --no-build -- dry
```

Reports structurally similar F# regions. "No duplicate candidates found." is the clean result.
Options: `--threshold <n>` (default 0.82), `--min-lines <n>` (default 4), `--min-tokens <n>` (default 20), `--format text|edn`.

### 3. CRAP — risk scores (needs coverage to be meaningful)

CRAP combines cyclomatic complexity with test coverage. Without a coverage file it prints `N/A` for every function, so generate coverage first via the test project (it references `coverlet.collector`). The `--filter` excludes the slow FsCheck `*PropertyTests` modules (which run thousands of generated cases) — the fast example-based tests still run, so coverage stays meaningful:

```bash
dotnet test ChessOverlay.Tests/ChessOverlay.Tests.fsproj -c Release \
  --collect:"XPlat Code Coverage" --results-directory artifacts/coverage \
  --filter "FullyQualifiedName!~PropertyTests"
```

That writes `artifacts/coverage/<guid>/coverage.cobertura.xml`. Feed the newest one to CRAP:

```bash
COV=$(ls -t artifacts/coverage/*/coverage.cobertura.xml | head -1)
dotnet run --project ChessOverlay.Quality -c Release --no-build -- crap --coverage "$COV"
```

Options: `--changed` (changed app `.fs` files only), `--threshold <n>` (default 8.0).
Note: CRAP exits with code **2** when any function exceeds the threshold — this is the intended "findings present" signal, not a tool failure. Report the offending functions; do not treat exit 2 as an error.

### 4. ARCH — layered architecture view

```bash
dotnet run --project ChessOverlay.Quality -c Release --no-build -- arch
```

Prints module count, dependency count, cycle count, and the layered module grouping.
Options: `--format text|html`, `--out <file>`. ARCH exits with code **2** if dependency cycles are detected.

### 5. Report

Summarize for the user:
- **DRY**: number of duplicate candidates (or "clean").
- **CRAP**: how many functions exceed the threshold, and the top offenders (highest CRAP first).
- **ARCH**: module / dependency / cycle counts; flag any cycles.

## Notes

- Requires the .NET 10 SDK (`net10.0`). The test project targets `net10.0-windows` (WinForms), so coverage generation runs on Windows.
- On Windows PowerShell, replace the `COV=$(...)` line with:
  `$COV = (Get-ChildItem artifacts/coverage/*/coverage.cobertura.xml | Sort-Object LastWriteTime -Desc | Select-Object -First 1).FullName`
- `--help` on the CLI prints full usage: `dotnet run --project ChessOverlay.Quality -- --help`.
