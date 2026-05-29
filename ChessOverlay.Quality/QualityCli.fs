namespace ChessOverlay.Quality

open System
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.IO

[<ExcludeFromCodeCoverage>]
module QualityCli =
    let private usage =
        """ChessOverlay.Quality

Commands:
  all     Run the default quality checks. This is the default command.
  dry     Find candidate duplicate F# code.
  crap    Compute CRAP-style risk scores for app F# code.
  arch    Generate a layered architecture view for ChessOverlay.
  mutate  Run source-level mutation testing over the pure F# core.

ALL usage:
  dotnet run --project ChessOverlay.Quality
  dotnet run --project ChessOverlay.Quality -- all

DRY usage:
  dotnet run --project ChessOverlay.Quality -- dry --threshold 0.78 --min-lines 8
  dotnet run --project ChessOverlay.Quality -- dry --format edn ChessOverlay

DRY options:
  --threshold <n>    Minimum structural similarity score. Default: 0.72.
  --min-lines <n>    Minimum source lines in a candidate region. Default: 4.
  --min-tokens <n>   Minimum normalized tokens in a candidate region. Default: 20.
  --format <f>       text or edn. Default: text.
  --edn              Same as --format edn.
  --text             Same as --format text.

CRAP options:
  --changed          Analyze changed app .fs files only.
  --coverage <file>  Use a Cobertura coverage XML file instead of generating fresh coverage.

ARCH usage:
  dotnet run --project ChessOverlay.Quality -- arch
  dotnet run --project ChessOverlay.Quality -- arch --format html --out artifacts/architecture.html

ARCH options:
  --format <f>       text or html. Default: text.
  --out <file>       Write output to a file instead of stdout.

MUTATE usage:
  dotnet run --project ChessOverlay.Quality -- mutate
  dotnet run --project ChessOverlay.Quality -- mutate --max-mutants 20 --threshold 0.6
  dotnet run --project ChessOverlay.Quality -- mutate --scan
  dotnet run --project ChessOverlay.Quality -- mutate --max-workers 4 --timeout-factor 10

Mutates Domain.fs and AttackCalculator.fs across six operator families (arithmetic,
comparison, equality, boolean, logical, and the constant 0<->1), then runs the test
suite per mutant. This provides equivalent coverage for the pure core. Inspired by
unclebob/mutate4go.

MUTATE options:
  --changed          Mutate changed core .fs files only.
  --since-last-run   Mutate only functions whose body changed since the last manifest.
  --scan             Report mutation-site counts only; no builds or tests.
  --update-manifest  Rewrite the embedded function manifest without mutating.
  --max-mutants <n>  Cap how many covered mutants are run. Default: all.
  --stop-after-survivors <n>  Stop after N surviving mutants are found. Default: 10; 0 disables.
  --max-workers <n>  Run mutants in parallel using N isolated project copies. Default: 1.
  --timeout-factor <n>  Bound each mutant's test run at N x the baseline duration.
  --threshold <n>    Minimum mutation score (killed / live). Default: 0.70.
  --coverage <file>  Use a Cobertura coverage XML file instead of generating fresh coverage.
"""

    let private findRepositoryRoot () =
        let rec loop (directory: DirectoryInfo) =
            if File.Exists(Path.Combine(directory.FullName, "ChessOverlay.slnx")) then
                directory.FullName
            elif isNull directory.Parent then
                Directory.GetCurrentDirectory()
            else
                loop directory.Parent

        loop (DirectoryInfo(Directory.GetCurrentDirectory()))

    let private tryPopValue (name: string) (args: string list) =
        args
        |> List.tryFindIndex ((=) name)
        |> Option.bind (fun index ->
            if index + 1 < args.Length then
                Some args[index + 1]
            else
                None)

    let private parseFloat name fallback args =
        tryPopValue name args
        |> Option.bind (fun value ->
            match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, parsed -> Some parsed
            | _ -> None)
        |> Option.defaultValue fallback

    let private parseInt name fallback args =
        tryPopValue name args
        |> Option.bind (fun value ->
            match Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, parsed -> Some parsed
            | _ -> None)
        |> Option.defaultValue fallback

    let private dryInputs (args: string list) =
        let rec loop remaining inputs =
            match remaining with
            | [] -> List.rev inputs
            | "--threshold" :: _ :: tail
            | "--min-lines" :: _ :: tail
            | "--min-tokens" :: _ :: tail
            | "--format" :: _ :: tail -> loop tail inputs
            | value :: tail when value.StartsWith("--") -> loop tail inputs
            | value :: tail -> loop tail (value :: inputs)

        loop args []

    let private archInputs (args: string list) =
        let rec loop remaining inputs =
            match remaining with
            | [] -> List.rev inputs
            | "--format" :: _ :: tail
            | "--out" :: _ :: tail -> loop tail inputs
            | value :: tail when value.StartsWith("--") -> loop tail inputs
            | value :: tail -> loop tail (value :: inputs)

        loop args []

    let private crapInputs (args: string list) =
        let rec loop remaining inputs =
            match remaining with
            | [] -> List.rev inputs
            | "--coverage" :: _ :: tail
            | "--threshold" :: _ :: tail -> loop tail inputs
            | value :: tail when value.StartsWith("--") -> loop tail inputs
            | value :: tail -> loop tail (value :: inputs)

        loop args []

    let private mutateInputs (args: string list) =
        let rec loop remaining inputs =
            match remaining with
            | [] -> List.rev inputs
            | "--coverage" :: _ :: tail
            | "--threshold" :: _ :: tail
            | "--max-mutants" :: _ :: tail
            | "--stop-after-survivors" :: _ :: tail
            | "--max-workers" :: _ :: tail
            | "--timeout-factor" :: _ :: tail -> loop tail inputs
            | value :: tail when value.StartsWith("--") -> loop tail inputs
            | value :: tail -> loop tail (value :: inputs)

        loop args []

    let private dryFormat args =
        if args |> List.contains "--edn" then
            "edn"
        elif args |> List.contains "--text" then
            "text"
        else
            tryPopValue "--format" args |> Option.defaultValue "text"

    let private archFormat args =
        tryPopValue "--format" args |> Option.defaultValue "text"

    let private printDryText (candidates: DuplicateCandidate list) =
        if List.isEmpty candidates then
            printfn "No duplicate candidates found."
        else
            for index, candidate in candidates |> List.indexed do
                if index > 0 then
                    printfn ""

                printfn "DUPLICATE score=%s" (candidate.Score.ToString("0.00", CultureInfo.InvariantCulture))
                printfn "  %s:%i-%i" candidate.Left.File candidate.Left.StartLine candidate.Left.EndLine
                printfn "  %s:%i-%i" candidate.Right.File candidate.Right.StartLine candidate.Right.EndLine

    let private locationEdn (location: DuplicateLocation) =
        sprintf
            "{:file \"%s\", :start-line %i, :end-line %i}"
            (DryDuplication.escapeEdn location.File)
            location.StartLine
            location.EndLine

    let private printDryEdn (candidates: DuplicateCandidate list) =
        if List.isEmpty candidates then
            printfn "{:candidates []}"
        else
            printfn "{:candidates"
            printf " ["

            for index, candidate in candidates |> List.indexed do
                if index > 0 then
                    printfn ""
                    printf "  "

                printf
                    "{:score %s\n   :left %s\n   :right %s\n   :left-tokens %i\n   :right-tokens %i}"
                    (candidate.Score.ToString(CultureInfo.InvariantCulture))
                    (locationEdn candidate.Left)
                    (locationEdn candidate.Right)
                    candidate.LeftTokens
                    candidate.RightTokens

            printfn "]}"

    let private runDry args =
        let options =
            {
                Root = findRepositoryRoot ()
                Inputs = dryInputs args
                Threshold = parseFloat "--threshold" 0.72 args
                MinimumLines = parseInt "--min-lines" 4 args
                MinimumTokens = parseInt "--min-tokens" 20 args
                Format = dryFormat args
            }

        let candidates = DryDuplication.findDuplicates options

        match options.Format with
        | "edn" ->
            printDryEdn candidates
            0
        | "text" ->
            printDryText candidates
            0
        | unknown ->
            eprintfn "Unknown format: %s" unknown
            2

    let private formatCoverage (coverage: float option) =
        coverage
        |> Option.map (fun value -> value.ToString("P0", CultureInfo.InvariantCulture))
        |> Option.defaultValue "N/A"

    let private formatCrap (crap: float option) =
        crap
        |> Option.map (fun value -> value.ToString("0.0", CultureInfo.InvariantCulture))
        |> Option.defaultValue "N/A"

    let private printCrapReport (threshold: float) (scores: FunctionScore list) =
        printfn "%-6s %-8s %-4s %s" "CRAP" "Coverage" "CC" "Function"
        printfn "%s" (String('-', 78))

        for score in scores do
            let span = score.Span
            printfn
                "%-6s %-8s %-4i %s:%i-%i %s"
                (formatCrap score.Crap)
                (formatCoverage score.Coverage)
                span.CyclomaticComplexity
                span.File
                span.StartLine
                span.EndLine
                span.Name

        let uncovered = scores |> List.filter (fun score -> score.Coverage.IsNone)

        if uncovered.Length > 0 then
            printfn ""
            printfn "Coverage was unavailable for %i function(s)." uncovered.Length

        let exceeded =
            scores
            |> List.filter (fun score ->
                score.Crap
                |> Option.exists (fun value -> value > threshold))

        if exceeded.Length > 0 then
            printfn ""
            printfn "Threshold %.1f exceeded by %i function(s)." threshold exceeded.Length

    let private resolveCoverage root args =
        match tryPopValue "--coverage" args with
        | Some coveragePath ->
            printfn "Using coverage: %s" coveragePath
            Some coveragePath
        | None ->
            printfn "Generating fresh coverage..."
            let result = CrapMetric.generateCoverage root
            printfn "Using coverage: %s" result.CoveragePath
            Some result.CoveragePath

    let private runCrap args =
        try
            let threshold = parseFloat "--threshold" 8.0 args
            let root = findRepositoryRoot ()

            let options =
                {
                    Root = root
                    Inputs = crapInputs args
                    ChangedOnly = args |> List.contains "--changed"
                    CoveragePath = resolveCoverage root args
                    Threshold = threshold
                }

            let scores = CrapMetric.analyze options
            printCrapReport threshold scores

            if scores
               |> List.exists (fun score -> score.Crap |> Option.exists (fun value -> value > threshold)) then
                2
            else
                0
        with ex ->
            eprintfn "%s" ex.Message
            2

    let private categoryName (category: MutationCategory) =
        match category with
        | Arithmetic -> "arithmetic"
        | Comparison -> "comparison"
        | Equality -> "equality"
        | Boolean -> "boolean"
        | Logical -> "logical"
        | Constant -> "constant"

    let private printMutationReport (threshold: float) (stopAfterSurvivors: int option) (report: MutationReport) =
        let detected = report.Killed + report.Timeout
        let live = detected + report.Survived

        printfn
            "Mutation score: %s (%i killed of %i live mutants)"
            (report.Score.ToString("P0", CultureInfo.InvariantCulture))
            detected
            live

        printfn
            "  killed=%i timeout=%i survived=%i no-coverage=%i compile-error=%i total=%i"
            report.Killed
            report.Timeout
            report.Survived
            report.NoCoverage
            report.CompileErrors
            report.Total

        if not (List.isEmpty report.Survivors) then
            let survivorsToReport =
                match stopAfterSurvivors with
                | Some limit when limit > 0 -> report.Survivors |> List.truncate limit
                | _ -> report.Survivors

            printfn ""
            printfn "Survivors (test gaps):"

            for mutant in survivorsToReport do
                printfn
                    "  %s:%i  %s  [%s]  %s"
                    (mutant.File.Replace('\\', '/'))
                    mutant.Line
                    (MutationTesting.description mutant)
                    (categoryName mutant.Category)
                    mutant.FunctionId

            let hidden = report.Survivors.Length - survivorsToReport.Length

            if hidden > 0 then
                printfn "  ... %i additional survivor(s) not shown." hidden

            match stopAfterSurvivors with
            | Some limit when limit > 0 && report.Survived >= limit ->
                printfn ""
                printfn "Stopped after %i surviving mutants were found." limit
            | _ -> ()

            printfn "AI agent instruction: Fix the reported surviving mutants, then rerun mutation testing."

        if report.Score < threshold then
            printfn ""
            printfn "Mutation score below threshold %s." (threshold.ToString("P0", CultureInfo.InvariantCulture))

    let private parseOptionalInt name args =
        tryPopValue name args
        |> Option.bind (fun value ->
            match Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, parsed -> Some parsed
            | _ -> None)

    let private parseStopAfterSurvivors args =
        let value = parseOptionalInt "--stop-after-survivors" args |> Option.defaultValue 10

        if value <= 0 then None else Some value

    let private mutateOptions root args =
        {
            Root = root
            Inputs = mutateInputs args
            ChangedOnly = args |> List.contains "--changed"
            MaxMutants = parseOptionalInt "--max-mutants" args
            StopAfterSurvivors = parseStopAfterSurvivors args
            Threshold = parseFloat "--threshold" 0.70 args
            CoveragePath = None
            SinceLastRun = args |> List.contains "--since-last-run"
            MaxWorkers = parseInt "--max-workers" 1 args
            TimeoutFactor = parseOptionalInt "--timeout-factor" args
        }

    let private runMutate args =
        try
            let root = findRepositoryRoot ()

            // --scan and --update-manifest are fast, structural modes that never build
            // or run tests, matching mutate4go.
            if args |> List.contains "--scan" then
                let options = { mutateOptions root args with CoveragePath = None }
                let result = MutationTesting.scan options
                printfn "Total mutation sites: %i" result.Total
                printfn "Changed mutation sites: %i" result.Changed
                printfn "Manifest exists: %b" result.HasManifest
                0
            elif args |> List.contains "--update-manifest" then
                let options = mutateOptions root args
                let files = MutationTesting.updateManifests options
                for file in files do
                    printfn "Updated manifest: %s" (file.Replace('\\', '/'))
                0
            else
                let baseOptions = mutateOptions root args
                let threshold = baseOptions.Threshold
                let options = { baseOptions with CoveragePath = resolveCoverage root args }

                // A per-mutant timeout needs a baseline; measure the clean suite once.
                let testTimeoutMs =
                    options.TimeoutFactor
                    |> Option.map (fun factor ->
                        printfn "Measuring baseline test duration for timeout..."
                        let baseline = MutationTesting.measureBaselineMs root
                        max 1000 (factor * baseline))

                let report =
                    if options.MaxWorkers > 1 then
                        let evaluate = MutationTesting.Runner.buildAndTestEvaluator testTimeoutMs
                        MutationTesting.runBatch options (MutationTesting.Runner.runMutationsParallelUntil root options.MaxWorkers options.StopAfterSurvivors evaluate)
                    else
                        let runner = MutationTesting.makeRunnerWithTimeout root testTimeoutMs
                        MutationTesting.run options runner

                printMutationReport threshold options.StopAfterSurvivors report

                if report.Score < threshold then 2 else 0
        with ex ->
            eprintfn "%s" ex.Message
            2

    let private runAll () =
        printfn "DRY"
        printfn "%s" (String('-', 78))
        let dryExitCode = runDry []

        printfn ""
        printfn "CRAP"
        printfn "%s" (String('-', 78))
        let crapExitCode = runCrap []

        if dryExitCode <> 0 then
            dryExitCode
        else
            crapExitCode

    let private runArch args =
        let options =
            {
                Root = findRepositoryRoot ()
                Inputs = archInputs args
                Format = archFormat args
                OutputPath = tryPopValue "--out" args
            }

        let model = ArchitectureView.analyze options

        let content =
            match options.Format with
            | "html" -> ArchitectureView.renderHtml model
            | "text" -> ArchitectureView.renderText model
            | unknown ->
                eprintfn "Unknown format: %s" unknown
                ""

        if String.IsNullOrEmpty content then
            2
        else
            match ArchitectureView.writeOutput options content with
            | Some path -> printfn "Architecture view written to %s" path
            | None -> printf "%s" content

            if model.Cycles.Length > 0 then
                2
            else
                0

    [<EntryPoint>]
    let main args =
        let args = Array.toList args

        match args with
        | "--help" :: _
        | "help" :: _ ->
            printf "%s" usage
            0
        | "all" :: [] -> runAll ()
        | "dry" :: tail -> runDry tail
        | "crap" :: tail -> runCrap tail
        | "arch" :: tail -> runArch tail
        | "mutate" :: tail -> runMutate tail
        | [] -> runAll ()
        | _ -> runDry args
