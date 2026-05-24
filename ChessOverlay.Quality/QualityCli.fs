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
  dry     Find candidate duplicate F# code. This is the default command.
  crap    Compute CRAP-style risk scores for app F# code.
  arch    Generate a layered architecture view for ChessOverlay.

DRY usage:
  dotnet run --project ChessOverlay.Quality
  dotnet run --project ChessOverlay.Quality -- dry --threshold 0.86 --min-lines 8
  dotnet run --project ChessOverlay.Quality -- dry --format edn ChessOverlay

DRY options:
  --threshold <n>    Minimum structural similarity score. Default: 0.82.
  --min-lines <n>    Minimum source lines in a candidate region. Default: 4.
  --min-tokens <n>   Minimum normalized tokens in a candidate region. Default: 20.
  --format <f>       text or edn. Default: text.
  --edn              Same as --format edn.
  --text             Same as --format text.

CRAP options:
  --changed          Analyze changed app .fs files only.
  --coverage <file>  Use a Cobertura coverage XML file.

ARCH usage:
  dotnet run --project ChessOverlay.Quality -- arch
  dotnet run --project ChessOverlay.Quality -- arch --format html --out artifacts/architecture.html

ARCH options:
  --format <f>       text or html. Default: text.
  --out <file>       Write output to a file instead of stdout.
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
                Threshold = parseFloat "--threshold" 0.82 args
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

    let private runCrap args =
        let threshold = parseFloat "--threshold" 8.0 args

        let options =
            {
                Root = findRepositoryRoot ()
                Inputs = crapInputs args
                ChangedOnly = args |> List.contains "--changed"
                CoveragePath = tryPopValue "--coverage" args
                Threshold = threshold
            }

        let scores = CrapMetric.analyze options
        printCrapReport threshold scores

        if scores
           |> List.exists (fun score -> score.Crap |> Option.exists (fun value -> value > threshold)) then
            2
        else
            0

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
        | "dry" :: tail -> runDry tail
        | "crap" :: tail -> runCrap tail
        | "arch" :: tail -> runArch tail
        | _ -> runDry args
