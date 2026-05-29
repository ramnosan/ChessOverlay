namespace ChessOverlay.Tests

open System
open System.IO
open System.Text
open Xunit
open ChessOverlay.Quality

module DryDuplicationTests =
    let private consoleLock = obj ()

    let private tempRoot () =
        let root = Path.Combine(Path.GetTempPath(), "ChessOverlayDryTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        root

    let private writeFile root relativePath (lines: string list) =
        let path = Path.Combine(root, relativePath)
        Directory.CreateDirectory(Path.GetDirectoryName(path)) |> ignore
        let contents = String.Join(Environment.NewLine, lines)
        File.WriteAllText(path, contents)
        path

    let private repositoryRoot () =
        let rec loop (directory: DirectoryInfo) =
            if File.Exists(Path.Combine(directory.FullName, "ChessOverlay.slnx")) then
                directory.FullName
            elif isNull directory.Parent then
                Directory.GetCurrentDirectory()
            else
                loop directory.Parent

        loop (DirectoryInfo(Directory.GetCurrentDirectory()))

    let private defaultOptions root =
        {
            Root = root
            Inputs = [ "ChessOverlay" ]
            Threshold = 0.72
            MinimumLines = 4
            MinimumTokens = 12
            Format = "text"
        }

    let private dry4CljOptions root =
        {
            Root = root
            Inputs = [ root ]
            Threshold = 0.80
            MinimumLines = 3
            MinimumTokens = 8
            Format = "text"
        }

    let private captureStdout (run: unit -> int) =
        lock consoleLock (fun () ->
            let originalOut = Console.Out
            use writer = new StringWriter(StringBuilder())

            try
                Console.SetOut(writer)
                let exitCode = run ()
                writer.Flush()
                exitCode, writer.ToString().Replace("\r\n", "\n")
            finally
                Console.SetOut(originalOut))

    [<Fact>]
    let ``DRY detector finds structurally duplicated FSharp declarations`` () =
        let root = tempRoot ()

        writeFile
            root
            "ChessOverlay/Left.fs"
            [
                "namespace Sample"
                ""
                "module Left ="
                "    let evaluate value ="
                "        let adjusted = value + 1"
                "        if adjusted > 10 then"
                "            adjusted * 2"
                "        else"
                "            adjusted - 2"
            ]
        |> ignore

        writeFile
            root
            "ChessOverlay/Right.fs"
            [
                "namespace Sample"
                ""
                "module Right ="
                "    let score input ="
                "        let result = input + 4"
                "        if result > 20 then"
                "            result * 8"
                "        else"
                "            result - 9"
            ]
        |> ignore

        let duplicates = DryDuplication.findDuplicates (defaultOptions root)

        let duplicate =
            Assert.Single duplicates

        Assert.Equal("ChessOverlay/Left.fs", duplicate.Left.File)
        Assert.Equal("ChessOverlay/Right.fs", duplicate.Right.File)
        Assert.True(duplicate.Score >= 0.72)

    [<Fact>]
    let ``DRY detector respects minimum token threshold`` () =
        let root = tempRoot ()

        writeFile
            root
            "ChessOverlay/One.fs"
            [
                "namespace Sample"
                ""
                "module One ="
                "    let duplicate value ="
                "        let adjusted = value + 1"
                "        if adjusted > 10 then adjusted else adjusted - 1"
            ]
        |> ignore

        writeFile
            root
            "ChessOverlay/Two.fs"
            [
                "namespace Sample"
                ""
                "module Two ="
                "    let duplicate value ="
                "        let adjusted = value + 1"
                "        if adjusted > 10 then adjusted else adjusted - 1"
            ]
        |> ignore

        let duplicates =
            DryDuplication.findDuplicates
                {
                    defaultOptions root with
                        MinimumTokens = 1000
                }

        Assert.Empty duplicates

    [<Fact>]
    let ``dry4clj reports structural duplicate candidates with file and line ranges`` () =
        let root = tempRoot ()

        writeFile
            root
            "left.fs"
            [
                "module Sample.Left"
                ""
                "let alpha xs ="
                "    let ys = xs |> List.filter odd"
                "    ys |> List.map id"
            ]
        |> ignore

        writeFile
            root
            "right.fs"
            [
                "module Sample.Right"
                ""
                "let beta items ="
                "    let kept = items |> List.filter even"
                "    kept |> List.map id"
            ]
        |> ignore

        let candidates = DryDuplication.findDuplicates (dry4CljOptions root)
        let candidate = Assert.Single candidates

        Assert.Equal("left.fs", candidate.Left.File)
        Assert.Equal(3, candidate.Left.StartLine)
        Assert.Equal(5, candidate.Left.EndLine)
        Assert.Equal("right.fs", candidate.Right.File)
        Assert.Equal(3, candidate.Right.StartLine)
        Assert.Equal(5, candidate.Right.EndLine)

    [<Fact>]
    let ``dry4clj matches fuzzy structures containing maps sets and property calls`` () =
        let root = tempRoot ()

        writeFile
            root
            "left.fs"
            [
                "module Sample.Left"
                ""
                "let gamma row ="
                "    let allowed = set [ \"a\"; \"b\" ]"
                "    if Set.contains row.Kind allowed then"
                "        Map.ofList [ \"left\", row.A; \"right\", row.B ]"
                "    else"
                "        Map.empty"
            ]
        |> ignore

        writeFile
            root
            "right.fs"
            [
                "module Sample.Right"
                ""
                "let delta item ="
                "    let accepted = set [ \"c\"; \"d\" ]"
                "    if Set.contains item.Kind accepted then"
                "        Map.ofList [ \"left\", item.C; \"right\", item.D ]"
                "    else"
                "        Map.empty"
            ]
        |> ignore

        let candidates = DryDuplication.findDuplicates (dry4CljOptions root)

        Assert.Single candidates |> ignore

    [<Fact>]
    let ``dry4clj reads conditional source regions`` () =
        let root = tempRoot ()

        writeFile
            root
            "left.fs"
            [
                "module Sample.Left"
                ""
                "let alpha x ="
                "#if DEBUG"
                "    if x > 0 then"
                "        x + 1"
                "    else"
                "        x"
                "#endif"
            ]
        |> ignore

        writeFile
            root
            "right.fs"
            [
                "module Sample.Right"
                ""
                "let beta y ="
                "#if DEBUG"
                "    if y > 0 then"
                "        y + 1"
                "    else"
                "        y"
                "#endif"
            ]
        |> ignore

        let candidates =
            DryDuplication.findDuplicates
                {
                    dry4CljOptions root with
                        Threshold = 0.50
                        MinimumLines = 1
                        MinimumTokens = 1
                }

        Assert.Single candidates |> ignore

    [<Fact>]
    let ``dry4clj filters forms shorter than the minimum line count`` () =
        let root = tempRoot ()

        writeFile root "one.fs" [ "module One"; "let a x = x + 1" ] |> ignore
        writeFile root "two.fs" [ "module Two"; "let b y = y + 2" ] |> ignore

        let candidates =
            DryDuplication.findDuplicates
                {
                    dry4CljOptions root with
                        MinimumLines = 3
                        MinimumTokens = 1
                }

        Assert.Empty candidates

    [<Fact>]
    let ``dry4clj parses command line options and paths`` () =
        let root = tempRoot ()

        writeFile
            root
            "left.fs"
            [
                "module Sample.Left"
                ""
                "let alpha xs ="
                "    let ys = xs |> List.filter odd"
                "    ys |> List.map id"
            ]
        |> ignore

        writeFile
            root
            "right.fs"
            [
                "module Sample.Right"
                ""
                "let beta items ="
                "    let kept = items |> List.filter even"
                "    kept |> List.map id"
            ]
        |> ignore

        let exitCode, output =
            captureStdout (fun () ->
                QualityCli.main [|
                    "dry"
                    "--threshold"
                    "0.80"
                    "--min-lines"
                    "3"
                    "--min-tokens"
                    "8"
                    "--format"
                    "edn"
                    root
                |])

        Assert.Equal(0, exitCode)
        Assert.Contains("{:candidates", output)
        Assert.Contains(":left-tokens", output)
        Assert.DoesNotContain("No duplicate candidates found.", output)

    [<Fact>]
    let ``dry4clj defaults to repository source roots when no paths are provided`` () =
        let root = repositoryRoot ()
        let files = DryDuplication.discoverSourceFiles root []

        Assert.Contains(files, fun path -> path.Replace('\\', '/').EndsWith("ChessOverlay/Domain.fs"))
        Assert.Contains(files, fun path -> path.Replace('\\', '/').EndsWith("ChessOverlay.Tests/QualityTools/DryDuplicationTests.fs"))

    [<Fact>]
    let ``dry4clj formats text output with line ranges`` () =
        let root = tempRoot ()

        writeFile
            root
            "left.fs"
            [
                "module Sample.Left"
                ""
                "let alpha xs ="
                "    let ys = xs |> List.filter odd"
                "    ys |> List.map id"
            ]
        |> ignore

        writeFile
            root
            "right.fs"
            [
                "module Sample.Right"
                ""
                "let beta items ="
                "    let kept = items |> List.filter even"
                "    kept |> List.map id"
            ]
        |> ignore

        let exitCode, output =
            captureStdout (fun () -> QualityCli.main [| "dry"; "--threshold"; "0.80"; "--min-lines"; "3"; "--min-tokens"; "8"; root |])

        Assert.Equal(0, exitCode)
        Assert.Contains("DUPLICATE score=", output)
        Assert.Contains("left.fs:3-5", output.Replace('\\', '/'))
        Assert.Contains("right.fs:3-5", output.Replace('\\', '/'))

    [<Fact>]
    let ``dry4clj prints a clear message when no text candidates exist`` () =
        let root = tempRoot ()
        writeFile root "one.fs" [ "module One"; "let a x = x + 1" ] |> ignore

        let exitCode, output =
            captureStdout (fun () -> QualityCli.main [| "dry"; root |])

        Assert.Equal(0, exitCode)
        Assert.Equal("No duplicate candidates found.\n", output)

    [<Fact>]
    let ``dry4clj prints help from main without scanning files`` () =
        let exitCode, output =
            captureStdout (fun () -> QualityCli.main [| "--help" |])

        Assert.Equal(0, exitCode)
        Assert.Contains("ChessOverlay.Quality", output)
        Assert.Contains("DRY usage:", output)

    [<Fact>]
    let ``dry4clj prints edn from main`` () =
        let root = tempRoot ()
        writeFile root "one.fs" [ "module One"; "let a x = x + 1" ] |> ignore

        let exitCode, output =
            captureStdout (fun () -> QualityCli.main [| "dry"; "--edn"; root |])

        Assert.Equal(0, exitCode)
        Assert.Equal("{:candidates []}\n", output)
