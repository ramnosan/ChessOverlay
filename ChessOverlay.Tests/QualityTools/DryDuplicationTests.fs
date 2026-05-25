namespace ChessOverlay.Tests

open System
open System.IO
open Xunit
open ChessOverlay.Quality

module DryDuplicationTests =
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

    let private defaultOptions root =
        {
            Root = root
            Inputs = [ "ChessOverlay" ]
            Threshold = 0.72
            MinimumLines = 4
            MinimumTokens = 12
            Format = "text"
        }

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
