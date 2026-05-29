namespace ChessOverlay.Quality

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// Which class of mutation produced a mutant. Mirrors the rule table of
/// unclebob/mutate4go (arithmetic, comparison, equality, boolean, logical,
/// constant) so reports group the same way. Used only for reporting/grouping.
type MutationCategory =
    | Arithmetic
    | Comparison
    | Equality
    | Boolean
    | Logical
    | Constant

/// A single source-level mutation: at File/Line, the text span at Column equal to
/// Original is replaced with Mutated. Columns are 0-based indices into the line as
/// produced by File.ReadAllLines (newline characters excluded). FunctionId names the
/// enclosing function (empty when the line is not inside one) and drives differential
/// "since last run" selection via the embedded manifest.
type Mutant =
    {
        File: string
        Line: int
        Column: int
        Original: string
        Mutated: string
        Category: MutationCategory
        FunctionId: string
    }

/// Outcome of running the test suite against one mutant.
/// NoCoverage and CompileError are excluded from the mutation score denominator.
/// Timeout (the mutated tests ran past the deadline) is folded into the kill count,
/// matching mutate4go: a mutant that hangs the suite has been detected.
type MutantOutcome =
    | Killed
    | Survived
    | Timeout
    | NoCoverage
    | CompileError

type MutationOptions =
    {
        Root: string
        Inputs: string list
        ChangedOnly: bool
        MaxMutants: int option
        /// Stop the run once this many surviving mutants have been found. Surviving
        /// mutants are the actionable test gaps that should be reported for fixing.
        StopAfterSurvivors: int option
        Threshold: float
        CoveragePath: string option
        /// Only mutate functions whose normalized body changed since the embedded
        /// manifest was last written (mutate4go --since-last-run).
        SinceLastRun: bool
        /// Number of isolated worker copies to mutate in parallel (1 = serial).
        MaxWorkers: int
        /// Multiplier applied to the baseline test duration to bound each mutant's
        /// test run. None disables the per-mutant timeout.
        TimeoutFactor: int option
    }

type MutationReport =
    {
        Total: int
        Killed: int
        Survived: int
        Timeout: int
        NoCoverage: int
        CompileErrors: int
        Score: float
        Survivors: Mutant list
    }

/// A function discovered in a source file, with the text used to detect changes.
type FunctionInfo =
    {
        Id: string
        Name: string
        StartLine: int
        EndLine: int
        Text: string
    }

module MutationTesting =
    /// A rewrite rule: every non-overlapping match of Pattern in the code-only text
    /// of a line becomes a mutant whose replacement is Replacement.
    type private Rule =
        {
            Category: MutationCategory
            Pattern: Regex
            Replacement: string
        }

    let private rule category pattern replacement =
        {
            Category = category
            Pattern = Regex(pattern, RegexOptions.Compiled)
            Replacement = replacement
        }

    // Operators are matched with their surrounding spaces so that the literals never
    // overlap (" > " can never appear inside " >= ") and so that tokens like "->",
    // "|>", "<-", or generic brackets (Map<string, int>) are left untouched. The F#
    // sources here are consistently formatted with spaces around infix operators.
    //
    // The constant rule (0 <-> 1, from mutate4go) uses lookarounds instead of spaces
    // so it only matches standalone integer literals: not the 0 in 0.5, 10, or 0x1F.
    let private rules =
        [
            rule Arithmetic @" \+ " " - "
            rule Arithmetic @" - " " + "
            rule Arithmetic @" \* " " / "
            rule Arithmetic @" / " " * "
            rule Comparison @" >= " " > "
            rule Comparison @" <= " " < "
            rule Comparison @" > " " >= "
            rule Comparison @" < " " <= "
            rule Equality @" <> " " = "
            rule Equality @" = " " <> "
            rule Logical @" && " " || "
            rule Logical @" \|\| " " && "
            rule Boolean @"\btrue\b" "false"
            rule Boolean @"\bfalse\b" "true"
            rule Logical @"\bnot\s+" ""
            rule Constant @"(?<![\w.])0(?![\w.])" "1"
            rule Constant @"(?<![\w.])1(?![\w.])" "0"
        ]

    // Binding `=` (let/member/type/...) vastly outnumbers comparison `=`; mutating it
    // to `<>` almost always fails to compile, wasting a build. We skip the equality
    // rule on lines whose code starts with a binding keyword, where the `=` is the
    // binder rather than a comparison.
    let private bindingKeywords =
        [ "let"; "and"; "member"; "type"; "module"; "use"; "do"; "static"
          "abstract"; "override"; "default"; "val"; "mutable"; "new"; "inherit" ]

    let private startsBinding (codeLine: string) =
        let trimmed = codeLine.TrimStart()

        bindingKeywords
        |> List.exists (fun keyword ->
            trimmed = keyword
            || trimmed.StartsWith(keyword + " ", StringComparison.Ordinal)
            || trimmed.StartsWith(keyword + "!", StringComparison.Ordinal))

    let private isEqualityRule (rule: Rule) =
        rule.Category = Equality && rule.Replacement = " <> "

    /// "original -> mutated", the human-readable description mutate4go prints.
    let description (mutant: Mutant) =
        sprintf "%s -> %s" (mutant.Original.Trim()) (mutant.Mutated.Trim())

    let mutantsForLine (file: string) (lineNumber: int) (line: string) : Mutant list =
        // codeOnly blanks string literals to spaces and drops trailing comments, so
        // operator matches never land inside strings/comments, and column indices for
        // matched (non-space) operators stay aligned with the original line.
        let code = CrapMetric.codeOnly line
        let skipEquality = startsBinding code

        rules
        |> List.filter (fun rule -> not (skipEquality && isEqualityRule rule))
        |> List.collect (fun rule ->
            rule.Pattern.Matches(code)
            |> Seq.cast<Match>
            |> Seq.map (fun m ->
                {
                    File = file
                    Line = lineNumber
                    Column = m.Index
                    Original = m.Value
                    Mutated = rule.Replacement
                    Category = rule.Category
                    FunctionId = ""
                })
            |> Seq.toList)

    /// Apply a mutant to full file contents (with original newlines preserved).
    /// Returns the contents unchanged if the recorded span no longer matches, so a
    /// stale mutant can never corrupt a file.
    let applyMutant (contents: string) (mutant: Mutant) : string =
        let segments = contents.Split('\n')
        let index = mutant.Line - 1

        if index < 0 || index >= segments.Length then
            contents
        else
            let line = segments[index]

            if
                mutant.Column < 0
                || mutant.Column + mutant.Original.Length > line.Length
                || line.Substring(mutant.Column, mutant.Original.Length) <> mutant.Original
            then
                contents
            else
                let before = line.Substring(0, mutant.Column)
                let after = line.Substring(mutant.Column + mutant.Original.Length)
                segments[index] <- before + mutant.Mutated + after
                String.Join("\n", segments)

    // ---------------------------------------------------------------------------
    // Function extraction (mutate4go associates every mutation site with the
    // enclosing function so it can run only changed functions on later runs).
    // We reuse CrapMetric's F# function-span detection and attach the body text.
    // ---------------------------------------------------------------------------

    let extractFunctions (root: string) (file: string) : FunctionInfo list =
        let lines = File.ReadAllLines file

        CrapMetric.findFunctionSpans root file
        |> List.map (fun span ->
            let body =
                lines[span.StartLine - 1 .. span.EndLine - 1]
                |> String.concat "\n"

            {
                Id = $"{span.File}/{span.Name}"
                Name = span.Name
                StartLine = span.StartLine
                EndLine = span.EndLine
                Text = body
            })

    let functionIdAtLine (functions: FunctionInfo list) (line: int) =
        functions
        |> List.tryFind (fun fn -> line >= fn.StartLine && line <= fn.EndLine)
        |> Option.map (fun fn -> fn.Id)
        |> Option.defaultValue ""

    /// Discover every mutant in a file together with the file's functions, tagging
    /// each mutant with its enclosing FunctionId. Mirrors mutate4go's Discover.
    let discover (root: string) (file: string) : Mutant list * FunctionInfo list =
        let functions = extractFunctions root file

        let mutants =
            File.ReadAllLines file
            |> Array.toList
            |> List.mapi (fun index line -> index + 1, line)
            |> List.collect (fun (lineNumber, line) -> mutantsForLine file lineNumber line)
            |> List.map (fun mutant ->
                { mutant with FunctionId = functionIdAtLine functions mutant.Line })

        mutants, functions

    // ---------------------------------------------------------------------------
    // Manifest: an embedded footer recording each function's normalized-text hash
    // and the last successful run date, so differential runs can mutate only the
    // functions that changed. Ported from mutate4go's manifest package.
    // ---------------------------------------------------------------------------

    [<CLIMutable>]
    type ManifestFunction =
        {
            Id: string
            Name: string
            Line: int
            EndLine: int
            Hash: string
        }

    [<CLIMutable>]
    type Manifest =
        {
            Version: int
            TestedAt: string
            Functions: ManifestFunction[]
        }

    module Manifest =
        let beginMarker = "// chessoverlay-mutate-manifest-begin"
        let endMarker = "// chessoverlay-mutate-manifest-end"

        let private jsonOptions =
            JsonSerializerOptions(PropertyNameCaseInsensitive = true)

        let private hash (value: string) =
            use sha = SHA256.Create()
            sha.ComputeHash(Encoding.UTF8.GetBytes value)
            |> Array.map (fun b -> b.ToString("x2"))
            |> String.concat ""

        /// Collapse all runs of whitespace to a single space so reformatting a
        /// function (without semantic change) does not flag it as changed.
        let normalize (value: string) =
            value.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            |> String.concat " "

        /// Remove the embedded manifest footer (and the blank lines before it),
        /// returning the original source. A no-op when no manifest is present.
        let strip (contents: string) =
            match contents.IndexOf(beginMarker, StringComparison.Ordinal) with
            | index when index < 0 -> contents
            | index -> contents.Substring(0, index).TrimEnd('\n') + "\n"

        let extract (contents: string) : Manifest option =
            let startIndex = contents.IndexOf(beginMarker, StringComparison.Ordinal)
            let endIndex = contents.IndexOf(endMarker, StringComparison.Ordinal)

            if startIndex < 0 || endIndex < 0 || endIndex <= startIndex then
                None
            else
                let block = contents.Substring(startIndex + beginMarker.Length, endIndex - (startIndex + beginMarker.Length))

                let json =
                    block.Split('\n')
                    |> Array.map (fun line ->
                        let trimmed = line.Trim()

                        if trimmed.StartsWith("//", StringComparison.Ordinal) then
                            trimmed.Substring(2).Trim()
                        else
                            trimmed)
                    |> Array.filter (fun line -> line <> "")
                    |> String.concat "\n"

                try
                    Some(JsonSerializer.Deserialize<Manifest>(json, jsonOptions))
                with _ ->
                    None

        let build (functions: FunctionInfo list) (contents: string) (now: DateTime) : Manifest =
            {
                Version = 1
                TestedAt = now.ToString("o")
                Functions =
                    functions
                    |> List.map (fun fn ->
                        {
                            Id = fn.Id
                            Name = fn.Name
                            Line = fn.StartLine
                            EndLine = fn.EndLine
                            Hash = hash (normalize fn.Text)
                        })
                    |> List.toArray
            }

        let embed (contents: string) (manifest: Manifest) : string =
            let clean = strip contents
            let json = JsonSerializer.Serialize(manifest, jsonOptions)

            let builder = StringBuilder()
            builder.Append(clean.TrimEnd('\n')) |> ignore
            builder.Append("\n\n") |> ignore
            builder.Append(beginMarker) |> ignore
            builder.Append("\n// ") |> ignore
            builder.Append(json) |> ignore
            builder.Append('\n') |> ignore
            builder.Append(endMarker) |> ignore
            builder.Append('\n') |> ignore
            builder.ToString()

        /// Function ids whose hash differs from the previous manifest (or all of
        /// them when there is no previous manifest).
        let changedFunctionIds (previous: Manifest option) (current: Manifest) : Set<string> =
            match previous with
            | None -> current.Functions |> Array.map (fun fn -> fn.Id) |> Set.ofArray
            | Some prev ->
                let priorHash =
                    prev.Functions
                    |> Array.map (fun fn -> fn.Id, fn.Hash)
                    |> Map.ofArray

                current.Functions
                |> Array.filter (fun fn -> Map.tryFind fn.Id priorHash <> Some fn.Hash)
                |> Array.map (fun fn -> fn.Id)
                |> Set.ofArray

        let private backupPath (path: string) = path + ".mutate.bak"

        let saveBackup (path: string) (contents: string) =
            File.WriteAllText(backupPath path, contents)

        /// Restore a source file from its backup if one exists (used to recover from
        /// an interrupted run). Returns true when a backup was applied.
        let restoreBackup (path: string) =
            let backup = backupPath path

            if File.Exists backup then
                File.WriteAllText(path, File.ReadAllText backup)
                true
            else
                false

        let cleanupBackup (path: string) =
            let backup = backupPath path

            if File.Exists backup then
                File.Delete backup

    let classifyOutcome (buildExitCode: int) (test: (int * bool) option) : MutantOutcome =
        if buildExitCode <> 0 then
            CompileError
        else
            match test with
            | Some(_, true) -> Timeout
            | Some(0, false) -> Survived
            | Some(_, false) -> Killed
            | None -> CompileError

    let summarize (results: (Mutant * MutantOutcome) list) : MutationReport =
        let withOutcome outcome =
            results |> List.filter (fun (_, result) -> result = outcome)

        let killed = withOutcome Killed |> List.length
        let timedOut = withOutcome Timeout |> List.length
        let survivors = withOutcome Survived |> List.map fst
        let survived = survivors.Length
        let detected = killed + timedOut
        let live = detected + survived

        {
            Total = results.Length
            Killed = killed
            Survived = survived
            Timeout = timedOut
            NoCoverage = withOutcome NoCoverage |> List.length
            CompileErrors = withOutcome CompileError |> List.length
            Score = if live = 0 then 1.0 else float detected / float live
            Survivors = survivors
        }

    let private targetFiles (options: MutationOptions) =
        let resolve (input: string) =
            if Path.IsPathRooted input then
                input
            else
                Path.Combine(options.Root, input)

        let isCoreFile (path: string) =
            match Path.GetFileName path with
            | "Domain.fs"
            | "AttackCalculator.fs" -> true
            | _ -> false

        let selected =
            if options.ChangedOnly then
                CrapMetric.changedSourceFiles options.Root
                |> List.filter isCoreFile
            elif not (List.isEmpty options.Inputs) then
                options.Inputs |> List.map resolve
            else
                [ Path.Combine(options.Root, "ChessOverlay", "Domain.fs")
                  Path.Combine(options.Root, "ChessOverlay", "AttackCalculator.fs") ]

        selected |> List.filter File.Exists

    let generateMutants (options: MutationOptions) : Mutant list =
        targetFiles options
        |> List.collect (fun file -> fst (discover options.Root file))

    /// Set of function ids whose body changed since the embedded manifest was
    /// written, across all target files. Used to honor --since-last-run.
    let changedFunctionIds (options: MutationOptions) : Set<string> =
        targetFiles options
        |> List.collect (fun file ->
            let contents = File.ReadAllText file
            let previous = Manifest.extract contents
            let _, functions = discover options.Root file
            let current = Manifest.build functions (Manifest.strip contents) DateTime.UtcNow
            Manifest.changedFunctionIds previous current |> Set.toList)
        |> Set.ofList

    let private relativeKey (root: string) (path: string) =
        Path.GetRelativePath(Path.GetFullPath root, Path.GetFullPath path).Replace('\\', '/')

    let isCovered (coverage: Map<string, Map<int, bool>>) (root: string) (mutant: Mutant) =
        match Map.tryFind (relativeKey root mutant.File) coverage with
        | Some lines -> Map.tryFind mutant.Line lines = Some true
        | None -> false

    /// Update each target file's embedded manifest to the current function hashes
    /// without running any mutations (mutate4go --update-manifest).
    let updateManifests (options: MutationOptions) : string list =
        targetFiles options
        |> List.map (fun file ->
            let contents = File.ReadAllText file
            let _, functions = discover options.Root file
            let clean = Manifest.strip contents
            let manifest = Manifest.build functions clean DateTime.UtcNow
            File.WriteAllText(file, Manifest.embed clean manifest)
            file)

    /// Mutation sites grouped by file, for the structural --scan report (counts only,
    /// no builds or tests). Returns total sites and the changed-by-manifest subset.
    let scan (options: MutationOptions) : {| Total: int; Changed: int; HasManifest: bool |} =
        let mutants = generateMutants options
        let hasManifest = targetFiles options |> List.exists (fun file -> Manifest.extract (File.ReadAllText file) |> Option.isSome)
        let changedIds = changedFunctionIds options
        let changed = mutants |> List.filter (fun m -> Set.contains m.FunctionId changedIds) |> List.length

        {| Total = mutants.Length
           Changed = changed
           HasManifest = hasManifest |}

    // ---------------------------------------------------------------------------
    // Running a test command, with a timeout, and classifying the result.
    // ---------------------------------------------------------------------------

    /// Run `dotnet <arguments>` under `root`, optionally with MSBuild props and a
    /// timeout in milliseconds. Returns (exit code, timed out). On timeout the
    /// process tree is killed and the exit code is reported as -1.
    let private runDotnet (root: string) (arguments: string list) (props: (string * string) list) (timeoutMs: int option) : int * bool =
        let startInfo = ProcessStartInfo("dotnet")
        startInfo.WorkingDirectory <- root
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        arguments |> List.iter startInfo.ArgumentList.Add
        props |> List.iter (fun (key, value) -> startInfo.ArgumentList.Add($"-p:{key}={value}"))

        use dotnet = Process.Start(startInfo)
        // Drain the pipes asynchronously so a killed process never deadlocks on a
        // full stdout/stderr buffer.
        let _ = dotnet.StandardOutput.ReadToEndAsync()
        let _ = dotnet.StandardError.ReadToEndAsync()

        let exited =
            match timeoutMs with
            | Some ms -> dotnet.WaitForExit ms
            | None ->
                dotnet.WaitForExit()
                true

        if not exited then
            try
                dotnet.Kill true
            with _ ->
                ()

            -1, true
        else
            dotnet.ExitCode, false

    let private buildAndTest (root: string) (testProject: string) (props: (string * string) list) (testTimeoutMs: int option) =
        let buildExit, _ = runDotnet root [ "build"; testProject ] props None

        let test =
            if buildExit = 0 then
                Some(runDotnet root [ "test"; testProject; "--no-build" ] props testTimeoutMs)
            else
                None

        classifyOutcome buildExit test

    /// Run the unmutated test suite once and return how long it took, used to derive
    /// per-mutant timeouts (mutate4go's baseline).
    let measureBaselineMs (root: string) : int =
        let testProject = Path.Combine(root, "ChessOverlay.Tests")
        let stopwatch = Stopwatch.StartNew()
        runDotnet root [ "test"; testProject ] [] None |> ignore
        stopwatch.Stop()
        int stopwatch.ElapsedMilliseconds

    /// Builds a runner that, for each mutant, writes the mutation, builds + tests the
    /// suite, classifies the outcome, and always restores the original file. The build
    /// output is isolated under a single per-session directory so builds stay
    /// incremental across mutants. `testTimeoutMs` bounds each mutant's test run.
    ///
    /// Because mutants are applied to the live source files, an interrupted run (Ctrl+C
    /// or process exit) would otherwise skip the per-mutant `finally` and leave broken
    /// source on disk. We track the file currently holding a mutation and register
    /// cancel/exit handlers that restore it, so the working tree is never left corrupted.
    let makeRunnerWithTimeout (root: string) (testTimeoutMs: int option) : Mutant -> MutantOutcome =
        let runName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"
        let basePath segment = String.Join("/", [ ".build-check"; "mutation"; runName; segment ]) + "/"

        let props =
            [ "BaseOutputPath", basePath "bin"
              "BaseIntermediateOutputPath", basePath "obj" ]

        let testProject = Path.Combine(root, "ChessOverlay.Tests")

        // (file, originalContents) for the file currently mutated, or None between mutants.
        let mutable pending: (string * string) option = None
        let gate = obj ()

        let restorePending () =
            lock gate (fun () ->
                match pending with
                | Some(file, original) ->
                    try
                        File.WriteAllText(file, original)
                    with _ ->
                        ()
                    pending <- None
                | None -> ())

        Console.CancelKeyPress.Add(fun _ -> restorePending ())
        AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> restorePending ())

        fun mutant ->
            let original = File.ReadAllText mutant.File

            try
                lock gate (fun () -> pending <- Some(mutant.File, original))
                File.WriteAllText(mutant.File, applyMutant original mutant)
                buildAndTest root testProject props testTimeoutMs
            finally
                restorePending ()

    let makeRunner (root: string) : Mutant -> MutantOutcome = makeRunnerWithTimeout root None

    // ---------------------------------------------------------------------------
    // Parallel mutation via isolated worker copies (mutate4go --max-workers).
    // Each worker gets its own copy of the project tree, so mutations never race on
    // shared source files and the original working tree is never touched.
    // ---------------------------------------------------------------------------

    module Runner =
        /// Directories that must never be copied into a worker: version control and
        /// all build/coverage output (copying them is slow and pointless, and the
        /// worker run dir lives under one of them).
        let private skipDirectories =
            [ ".git"; "bin"; "obj"; "artifacts"; ".build-check"; ".vs"; "TestResults"; "target" ]

        let shouldSkipCopy (relativePath: string) =
            // bin/obj live under each project (not just the root), so any path segment
            // matching an excluded directory is enough to skip it.
            relativePath.Split([| '/'; '\\' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.exists (fun segment -> List.contains segment skipDirectories)

        /// Recursively copy `src` into `dst`, skipping the excluded directories.
        let copyProject (src: string) (dst: string) =
            let src = Path.GetFullPath src
            Directory.CreateDirectory dst |> ignore

            let rec walk (directory: string) =
                for entry in Directory.EnumerateFileSystemEntries directory do
                    let relative = Path.GetRelativePath(src, entry)

                    if not (shouldSkipCopy relative) then
                        let target = Path.Combine(dst, relative)

                        if Directory.Exists entry then
                            Directory.CreateDirectory target |> ignore
                            walk entry
                        else
                            Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
                            File.Copy(entry, target, true)

            walk src

        /// Run `mutants` across `maxWorkers` isolated project copies. For each mutant
        /// the worker's copy of the source file is mutated, `evaluate workerRoot
        /// workerSourcePath mutant` classifies the outcome, and the copy is restored.
        /// `evaluate` is injected so the build+test runner can be stubbed in tests.
        let runMutationsParallelUntil
            (root: string)
            (maxWorkers: int)
            (stopAfterSurvivors: int option)
            (evaluate: string -> string -> Mutant -> MutantOutcome)
            (mutants: Mutant list)
            : (Mutant * MutantOutcome) list =
            if List.isEmpty mutants then
                []
            else
                let root = Path.GetFullPath root
                let workerCount = mutants.Length |> min maxWorkers |> max 1

                let runRoot =
                    Path.Combine(
                        root,
                        ".build-check",
                        "mutation-workers",
                        $"run-{Environment.ProcessId}-{DateTime.UtcNow.Ticks}")

                Directory.CreateDirectory runRoot |> ignore

                try
                    let workerRoots =
                        [ for index in 1..workerCount ->
                              let workerRoot = Path.Combine(runRoot, $"worker-{index}")
                              copyProject root workerRoot
                              workerRoot ]

                    let originals =
                        mutants
                        |> List.map (fun mutant -> mutant.File)
                        |> List.distinct
                        |> List.map (fun file -> file, File.ReadAllText file)
                        |> Map.ofList

                    let queue = ConcurrentQueue(mutants |> List.indexed)
                    let results = ConcurrentBag<int * (Mutant * MutantOutcome)>()
                    let stopGate = obj ()
                    let mutable survivorCount = 0
                    let mutable stopRequested = false

                    let shouldContinue () =
                        lock stopGate (fun () -> not stopRequested)

                    let recordOutcome outcome =
                        lock stopGate (fun () ->
                            if outcome = Survived then
                                survivorCount <- survivorCount + 1

                            match stopAfterSurvivors with
                            | Some limit when limit > 0 && survivorCount >= limit -> stopRequested <- true
                            | _ -> ())

                    let worker (workerRoot: string) =
                        async {
                            let mutable working = true

                            while working && shouldContinue () do
                                match queue.TryDequeue() with
                                | true, (index, mutant) ->
                                    let relative = Path.GetRelativePath(root, mutant.File)
                                    let workerSource = Path.Combine(workerRoot, relative)
                                    let original = originals[mutant.File]
                                    File.WriteAllText(workerSource, applyMutant original mutant)
                                    let outcome = evaluate workerRoot workerSource mutant
                                    File.WriteAllText(workerSource, original)
                                    recordOutcome outcome
                                    results.Add(index, (mutant, outcome))
                                | false, _ -> working <- false
                        }

                    workerRoots
                    |> List.map worker
                    |> Async.Parallel
                    |> Async.RunSynchronously
                    |> ignore

                    results |> Seq.sortBy fst |> Seq.map snd |> Seq.toList
                finally
                    try
                        Directory.Delete(runRoot, true)
                    with _ ->
                        ()

        let runMutationsParallel
            (root: string)
            (maxWorkers: int)
            (evaluate: string -> string -> Mutant -> MutantOutcome)
            (mutants: Mutant list)
            : (Mutant * MutantOutcome) list =
            runMutationsParallelUntil root maxWorkers None evaluate mutants

        /// The production evaluator: build + test the worker's copy in place.
        let buildAndTestEvaluator (testTimeoutMs: int option) : string -> string -> Mutant -> MutantOutcome =
            fun workerRoot _ _ ->
                let testProject = Path.Combine(workerRoot, "ChessOverlay.Tests")
                buildAndTest workerRoot testProject [] testTimeoutMs

    /// Coverage / changed / cap selection shared by serial and parallel execution.
    /// Returns the mutants to run and the no-coverage mutants.
    let private selectMutants (options: MutationOptions) : Mutant list * Mutant list =
        let coverage =
            options.CoveragePath
            |> Option.map (CrapMetric.readCoberturaLineCoverage options.Root)
            |> Option.defaultValue Map.empty

        let candidates =
            let all = generateMutants options

            if options.SinceLastRun then
                let changed = changedFunctionIds options
                all |> List.filter (fun mutant -> Set.contains mutant.FunctionId changed)
            else
                all

        // With no coverage data we cannot tell which mutants are exercised, so run them
        // all rather than silently reporting everything as NoCoverage.
        let covered, uncovered =
            if Map.isEmpty coverage then
                candidates, []
            else
                candidates |> List.partition (isCovered coverage options.Root)

        let toRun =
            match options.MaxMutants with
            | Some limit -> covered |> List.truncate limit
            | None -> covered

        toRun, uncovered

    /// Orchestrates a mutation run, evaluating the selected mutants with `batch`
    /// (serial or parallel). Injecting the batch lets tests supply a stub instead of
    /// invoking real builds.
    let runBatch (options: MutationOptions) (batch: Mutant list -> (Mutant * MutantOutcome) list) : MutationReport =
        let toRun, uncovered = selectMutants options
        let runResults = batch toRun
        let noCoverageResults = uncovered |> List.map (fun mutant -> mutant, NoCoverage)
        summarize (runResults @ noCoverageResults)

    /// Run mutations serially with the injected per-mutant runner.
    let run (options: MutationOptions) (runner: Mutant -> MutantOutcome) : MutationReport =
        let toRun, uncovered = selectMutants options

        let rec loop remaining survivorCount results =
            match options.StopAfterSurvivors with
            | Some limit when limit > 0 && survivorCount >= limit -> List.rev results
            | _ ->
                match remaining with
                | [] -> List.rev results
                | mutant :: tail ->
                    let outcome = runner mutant
                    let nextSurvivorCount =
                        if outcome = Survived then survivorCount + 1 else survivorCount

                    loop tail nextSurvivorCount ((mutant, outcome) :: results)

        let runResults = loop toRun 0 []
        let noCoverageResults = uncovered |> List.map (fun mutant -> mutant, NoCoverage)
        summarize (runResults @ noCoverageResults)
