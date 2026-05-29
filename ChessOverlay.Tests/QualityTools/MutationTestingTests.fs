namespace ChessOverlay.Tests

open System
open System.IO
open Xunit
open ChessOverlay.Quality
open ChessOverlay.Quality.MutationTesting

module MutationTestingTests =
    let private tempRoot () =
        let root = Path.Combine(Path.GetTempPath(), "ChessOverlayMutationTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        root

    let private mutantsFor line =
        MutationTesting.mutantsForLine "Sample.fs" 1 line

    let private hasMutation original mutated line =
        mutantsFor line
        |> List.exists (fun mutant -> mutant.Original.Trim() = original && mutant.Mutated.Trim() = mutated)

    // ---- Mutation discovery (ports mutations_test.go) ------------------------

    [<Fact>]
    let ``comparison operators are mutated`` () =
        Assert.True(hasMutation ">" ">=" "        if value > 0 then")
        Assert.True(hasMutation "=" "<>" "        if a = b then")
        Assert.True(hasMutation "<>" "=" "        if a <> b then")

    [<Fact>]
    let ``arithmetic operators are mutated`` () =
        Assert.True(hasMutation "+" "-" "        let total = a + b")
        Assert.True(hasMutation "-" "+" "        let total = a - b")
        Assert.True(hasMutation "*" "/" "        let area = a * b")

    [<Fact>]
    let ``boolean operators and literals are mutated`` () =
        Assert.True(hasMutation "&&" "||" "        if a && b then")
        Assert.True(hasMutation "||" "&&" "        if a || b then")
        Assert.True(hasMutation "true" "false" "        let ready = true")

    [<Fact>]
    let ``constant zero and one are swapped`` () =
        // mutate4go's constant rule: 0 <-> 1.
        Assert.True(hasMutation "0" "1" "        let start = 0")
        Assert.True(hasMutation "1" "0" "        let step = 1")

    [<Fact>]
    let ``constants inside larger numbers are not mutated`` () =
        // 0 in 0.5, 1 in 10, and digits inside identifiers must be left alone.
        Assert.Empty(mutantsFor "        let ratio = 0.5")
        Assert.Empty(mutantsFor "        let count = 10")
        Assert.Empty(mutantsFor "        let mask = 0x1F")

    [<Fact>]
    let ``operators inside string literals are not mutated`` () =
        let mutants = mutantsFor "        let label = \"a + b > c\""
        Assert.Empty(mutants)

    [<Fact>]
    let ``operators inside comments are not mutated`` () =
        let mutants = mutantsFor "        let x = y // a + b > c && d"
        // Only the binder `=` is on this line, and binder `=` is skipped, so the
        // comment contributes nothing.
        Assert.Empty(mutants)

    [<Fact>]
    let ``binding equals is not treated as a comparison`` () =
        Assert.False(hasMutation "=" "<>" "    let value = 1")
        Assert.False(hasMutation "=" "<>" "        member this.Size = size")

    [<Fact>]
    let ``arrows and pipes are left untouched`` () =
        Assert.Empty(mutantsFor "        items |> List.map (fun x -> x)")

    [<Fact>]
    let ``description renders original to mutated`` () =
        let mutant = mutantsFor "        if a > b then" |> List.head
        Assert.Equal("> -> >=", MutationTesting.description mutant)

    [<Fact>]
    let ``discover assigns mutation sites to their enclosing function`` () =
        // Port of TestDiscoverMutationSites: one function, and the expected set of
        // mutation descriptions including the constant and boolean rules.
        let root = tempRoot ()
        let file = Path.Combine(root, "Sample.fs")

        File.WriteAllText(
            file,
            String.Join("\n", [ "module Sample"; ""; "let score x ="; "    x + 1 > 0 && true" ])
        )

        let mutants, functions = MutationTesting.discover root file

        Assert.Single(functions) |> ignore
        Assert.Equal("score", functions.[0].Name)
        Assert.All(mutants, fun mutant -> Assert.Equal(functions.[0].Id, mutant.FunctionId))

        let descriptions = mutants |> List.map MutationTesting.description |> Set.ofList

        for expected in [ "+ -> -"; "1 -> 0"; "> -> >="; "0 -> 1"; "&& -> ||"; "true -> false" ] do
            Assert.Contains(expected, descriptions)

        Directory.Delete(root, true)

    [<Fact>]
    let ``applyMutant rewrites the exact span and round-trips`` () =
        let contents = "let f a b =\r\n    if a > b then 1 else 0\r\n"
        let mutant = mutantsFor "    if a > b then 1 else 0" |> List.head

        // Re-target the mutant onto line 2 of the contents above.
        let onLineTwo = { mutant with Line = 2 }
        let mutated = MutationTesting.applyMutant contents onLineTwo

        Assert.Contains("if a >= b then", mutated)
        Assert.DoesNotContain("if a > b then", mutated)

    [<Fact>]
    let ``applyMutant is a no-op when the span no longer matches`` () =
        let contents = "let x = 1\n"

        let stale =
            { File = "Sample.fs"; Line = 1; Column = 4; Original = " > "; Mutated = " >= "; Category = Comparison; FunctionId = "" }

        Assert.Equal(contents, MutationTesting.applyMutant contents stale)

    // ---- Outcome classification / scoring ------------------------------------

    [<Theory>]
    [<InlineData(1, 0, false, false)>] // build failed -> compile error
    [<InlineData(0, 0, false, true)>] // built, tests passed -> survived
    let ``classifyOutcome distinguishes compile errors from survivors`` (buildExit: int) (testExit: int) (timedOut: bool) (survived: bool) =
        let outcome = MutationTesting.classifyOutcome buildExit (Some(testExit, timedOut))
        let expected = if survived then Survived else CompileError
        Assert.Equal(expected, outcome)

    [<Fact>]
    let ``classifyOutcome reports killed when tests fail`` () =
        Assert.Equal(Killed, MutationTesting.classifyOutcome 0 (Some(1, false)))

    [<Fact>]
    let ``classifyOutcome reports timeout as its own outcome`` () =
        Assert.Equal(Timeout, MutationTesting.classifyOutcome 0 (Some(-1, true)))

    [<Fact>]
    let ``summarize folds timeouts into the kill count`` () =
        let mutant category =
            { File = "Sample.fs"; Line = 1; Column = 0; Original = "+"; Mutated = "-"; Category = category; FunctionId = "" }

        let results =
            [
                mutant Arithmetic, Killed
                mutant Arithmetic, Killed
                mutant Comparison, Timeout
                mutant Arithmetic, Survived
                mutant Boolean, NoCoverage
                mutant Boolean, CompileError
            ]

        let report = MutationTesting.summarize results

        Assert.Equal(2, report.Killed)
        Assert.Equal(1, report.Timeout)
        Assert.Equal(1, report.Survived)
        Assert.Equal(1, report.NoCoverage)
        Assert.Equal(1, report.CompileErrors)
        Assert.Equal(6, report.Total)
        // Score = (killed + timeout) / (killed + timeout + survived) = 3 / 4.
        Assert.Equal(3.0 / 4.0, report.Score, 6)
        Assert.Single(report.Survivors) |> ignore

    [<Fact>]
    let ``run uses the injected runner and caps mutants`` () =
        // A temp file with three known comparison mutants and no coverage data, so the
        // runner is exercised directly (Map.isEmpty coverage => run all).
        let root = tempRoot ()
        let app = Path.Combine(root, "ChessOverlay")
        Directory.CreateDirectory(app) |> ignore
        let file = Path.Combine(app, "Domain.fs")

        File.WriteAllText(
            file,
            String.Join("\n", [ "module Sample"; "let f a b = if a > b then a < b else a > b" ])
        )

        let options =
            {
                Root = root
                Inputs = [ file ]
                ChangedOnly = false
                MaxMutants = Some 1
                StopAfterSurvivors = None
                Threshold = 0.7
                CoveragePath = None
                SinceLastRun = false
                MaxWorkers = 1
                TimeoutFactor = None
            }

        let mutable calls = 0

        let runner _ =
            calls <- calls + 1
            Killed

        let report = MutationTesting.run options runner

        // Three comparison mutants exist, but MaxMutants caps the run at one.
        Assert.Equal(1, calls)
        Assert.Equal(1, report.Killed)
        Assert.Equal(1, report.Total)

        Directory.Delete(root, true)

    [<Fact>]
    let ``run stops after survivor limit is reached`` () =
        let root = tempRoot ()
        let app = Path.Combine(root, "ChessOverlay")
        Directory.CreateDirectory(app) |> ignore
        let file = Path.Combine(app, "Domain.fs")

        File.WriteAllText(
            file,
            String.Join("\n", [ "module Sample"; "let f a b = if a > b then a < b else a > b" ])
        )

        let options =
            {
                Root = root
                Inputs = [ file ]
                ChangedOnly = false
                MaxMutants = None
                StopAfterSurvivors = Some 2
                Threshold = 0.7
                CoveragePath = None
                SinceLastRun = false
                MaxWorkers = 1
                TimeoutFactor = None
            }

        let mutable calls = 0

        let runner _ =
            calls <- calls + 1
            Survived

        let report = MutationTesting.run options runner

        Assert.Equal(2, calls)
        Assert.Equal(2, report.Survived)
        Assert.Equal(2, report.Total)

        Directory.Delete(root, true)

    // ---- Manifest (ports manifest_test.go) -----------------------------------

    [<Fact>]
    let ``manifest embed, extract, and strip round-trip`` () =
        let content = "module Sample\n\nlet f () = 1\n"

        let functions =
            [ { Id = "Sample.fs/f"; Name = "f"; StartLine = 3; EndLine = 3; Text = "let f () = 1" } ]

        let built = MutationTesting.Manifest.build functions content (DateTime(2026, 1, 1))
        let embedded = MutationTesting.Manifest.embed content built

        match MutationTesting.Manifest.extract embedded with
        | Some manifest -> Assert.Equal("Sample.fs/f", manifest.Functions.[0].Id)
        | None -> Assert.Fail("expected a manifest")

        Assert.Equal(content, MutationTesting.Manifest.strip embedded)

    [<Fact>]
    let ``changedFunctionIds reports only functions whose hash changed`` () =
        let previous =
            Some
                { Version = 1
                  TestedAt = ""
                  Functions =
                    [| { Id = "f"; Name = "f"; Line = 1; EndLine = 1; Hash = "old" }
                       { Id = "g"; Name = "g"; Line = 2; EndLine = 2; Hash = "same" } |] }

        let current =
            { Version = 1
              TestedAt = ""
              Functions =
                [| { Id = "f"; Name = "f"; Line = 1; EndLine = 1; Hash = "new" }
                   { Id = "g"; Name = "g"; Line = 2; EndLine = 2; Hash = "same" } |] }

        let changed = MutationTesting.Manifest.changedFunctionIds previous current

        Assert.Contains("f", changed)
        Assert.DoesNotContain("g", changed)

    [<Fact>]
    let ``changedFunctionIds reports everything when there is no previous manifest`` () =
        let current =
            { Version = 1
              TestedAt = ""
              Functions = [| { Id = "f"; Name = "f"; Line = 1; EndLine = 1; Hash = "h" } |] }

        let changed = MutationTesting.Manifest.changedFunctionIds None current
        Assert.Contains("f", changed)

    [<Fact>]
    let ``manifest normalize collapses whitespace so reformatting is not a change`` () =
        Assert.Equal(
            MutationTesting.Manifest.normalize "let f () =\n    1",
            MutationTesting.Manifest.normalize "let f ()   =   1"
        )

    [<Fact>]
    let ``manifest backup saves, restores, and cleans up`` () =
        let root = tempRoot ()
        let file = Path.Combine(root, "Sample.fs")
        File.WriteAllText(file, "original\n")

        MutationTesting.Manifest.saveBackup file "original\n"
        File.WriteAllText(file, "mutated\n")

        Assert.True(MutationTesting.Manifest.restoreBackup file)
        Assert.Equal("original\n", File.ReadAllText file)

        MutationTesting.Manifest.cleanupBackup file
        Assert.False(MutationTesting.Manifest.restoreBackup file)

        Directory.Delete(root, true)

    // ---- Coverage integration (ports coverage_test.go) -----------------------

    [<Fact>]
    let ``cobertura coverage drives isCovered by line`` () =
        let root = tempRoot ()
        let coveragePath = Path.Combine(root, "coverage.cobertura.xml")

        File.WriteAllText(
            coveragePath,
            """<?xml version="1.0"?>
<coverage>
  <packages>
    <package>
      <classes>
        <class filename="ChessOverlay/Domain.fs">
          <lines>
            <line number="4" hits="1" />
            <line number="8" hits="0" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>"""
        )

        let coverage = CrapMetric.readCoberturaLineCoverage root coveragePath
        let file = Path.Combine(root, "ChessOverlay", "Domain.fs")

        let mutantOn line =
            { File = file; Line = line; Column = 0; Original = "+"; Mutated = "-"; Category = Arithmetic; FunctionId = "" }

        Assert.True(MutationTesting.isCovered coverage root (mutantOn 4))
        Assert.False(MutationTesting.isCovered coverage root (mutantOn 8))
        Assert.False(MutationTesting.isCovered coverage root (mutantOn 99))

        Directory.Delete(root, true)

    // ---- Runner: copyProject + parallel workers (ports runner_test.go) -------

    [<Fact>]
    let ``shouldSkipCopy excludes build and vcs directories`` () =
        Assert.True(MutationTesting.Runner.shouldSkipCopy "bin")
        Assert.True(MutationTesting.Runner.shouldSkipCopy (Path.Combine("obj", "Debug")))
        Assert.True(MutationTesting.Runner.shouldSkipCopy (Path.Combine(".git", "config")))
        Assert.False(MutationTesting.Runner.shouldSkipCopy "ChessOverlay")
        Assert.False(MutationTesting.Runner.shouldSkipCopy (Path.Combine("ChessOverlay", "Domain.fs")))

    [<Fact>]
    let ``copyProject skips worker-excluded directories`` () =
        let root = tempRoot ()

        let write relative (content: string) =
            let path = Path.Combine(root, relative)
            Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
            File.WriteAllText(path, content)

        write "ChessOverlay.slnx" "solution"
        write (Path.Combine("ChessOverlay", "Domain.fs")) "module Domain"
        write (Path.Combine("ChessOverlay", "bin", "app.dll")) "binary"
        write (Path.Combine("ChessOverlay", "obj", "app.obj")) "obj"
        write (Path.Combine(".git", "HEAD")) "ref"
        write (Path.Combine("artifacts", "coverage.xml")) "coverage"
        write (Path.Combine(".build-check", "old")) "old"

        let dst = Path.Combine(tempRoot (), "copy")
        MutationTesting.Runner.copyProject root dst

        Assert.True(File.Exists(Path.Combine(dst, "ChessOverlay.slnx")))
        Assert.True(File.Exists(Path.Combine(dst, "ChessOverlay", "Domain.fs")))
        Assert.False(Directory.Exists(Path.Combine(dst, "ChessOverlay", "bin")))
        Assert.False(Directory.Exists(Path.Combine(dst, "ChessOverlay", "obj")))
        Assert.False(Directory.Exists(Path.Combine(dst, ".git")))
        Assert.False(Directory.Exists(Path.Combine(dst, "artifacts")))
        Assert.False(Directory.Exists(Path.Combine(dst, ".build-check")))

        Directory.Delete(root, true)

    [<Fact>]
    let ``parallel mutation uses isolated worker copies and restores the source`` () =
        // Port of TestRunMutationsParallelUsesIsolatedWorkerCopies: the evaluator only
        // ever sees the mutated content in its own worker copy, and the real source
        // file is left untouched with the worker run directory cleaned up afterwards.
        let root = tempRoot ()
        let source = "module Sample\n\nlet flag = true\nlet other = true\n"
        let file = Path.Combine(root, "Sample.fs")
        File.WriteAllText(file, source)

        let mutantOnLine lineNumber lineText =
            let mutant = MutationTesting.mutantsForLine "Sample.fs" 1 lineText |> List.head
            { mutant with File = file; Line = lineNumber }

        let mutants =
            [ mutantOnLine 3 "let flag = true"
              mutantOnLine 4 "let other = true" ]

        // Killed iff the worker copy actually contains the mutation; reading the real
        // source would still show "true" and report Survived.
        let evaluate _ (workerSource: string) _ =
            if (File.ReadAllText workerSource).Contains("false") then Killed else Survived

        let results = MutationTesting.Runner.runMutationsParallel root 2 evaluate mutants

        Assert.Equal(2, results.Length)
        Assert.All(results, fun (_, outcome) -> Assert.Equal(Killed, outcome))

        // The real working tree is untouched.
        Assert.Equal(source, File.ReadAllText file)

        // The per-run worker directory was cleaned up.
        let workerRoot = Path.Combine(root, ".build-check", "mutation-workers")

        if Directory.Exists workerRoot then
            Assert.Empty(Directory.GetDirectories workerRoot)

        Directory.Delete(root, true)

    [<Fact>]
    let ``parallel mutation stops after survivor limit`` () =
        let root = tempRoot ()
        let source = "module Sample\n\nlet flag = true\nlet other = true\n"
        let file = Path.Combine(root, "Sample.fs")
        File.WriteAllText(file, source)

        let mutantOnLine lineNumber lineText =
            let mutant = MutationTesting.mutantsForLine "Sample.fs" 1 lineText |> List.head
            { mutant with File = file; Line = lineNumber }

        let mutants =
            [ mutantOnLine 3 "let flag = true"
              mutantOnLine 4 "let other = true" ]

        let evaluate _ _ _ = Survived

        let results = MutationTesting.Runner.runMutationsParallelUntil root 1 (Some 1) evaluate mutants

        Assert.Single(results) |> ignore
        Assert.Equal(Survived, results |> List.head |> snd)
        Assert.Equal(source, File.ReadAllText file)

        Directory.Delete(root, true)
