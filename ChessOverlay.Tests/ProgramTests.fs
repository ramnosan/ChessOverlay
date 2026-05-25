namespace ChessOverlay.Tests

open System.IO
open Xunit
open ChessOverlay

module ProgramTests =
    [<Fact>]
    let ``tryParseGeometry accepts valid left,top,size string`` () =
        Assert.Equal(Some { Left = 10; Top = 20; Size = 300 }, BoardGeometryStorage.tryParseGeometry "10,20,300")

    [<Fact>]
    let ``tryParseGeometry rejects wrong field count`` () =
        Assert.Equal(None, BoardGeometryStorage.tryParseGeometry "10,20")
        Assert.Equal(None, BoardGeometryStorage.tryParseGeometry "10,20,300,400")

    [<Fact>]
    let ``tryParseGeometry rejects non-numeric fields`` () =
        Assert.Equal(None, BoardGeometryStorage.tryParseGeometry "10,top,300")
        Assert.Equal(None, BoardGeometryStorage.tryParseGeometry "left,20,300")

    [<Fact>]
    let ``tryParseGeometry rejects zero or negative size`` () =
        Assert.Equal(None, BoardGeometryStorage.tryParseGeometry "10,20,0")
        Assert.Equal(None, BoardGeometryStorage.tryParseGeometry "10,20,-1")

    [<Fact>]
    let ``tryLoadFrom returns None when file does not exist`` () =
        Assert.Equal(None, BoardGeometryStorage.tryLoadFrom (Path.GetTempPath() + "nonexistent_chess_file.txt"))

    [<Fact>]
    let ``tryLoadFrom parses valid geometry file`` () =
        let path = Path.GetTempFileName()
        try
            File.WriteAllText(path, "5,10,200")
            Assert.Equal(Some { Left = 5; Top = 10; Size = 200 }, BoardGeometryStorage.tryLoadFrom path)
        finally
            File.Delete(path)

    [<Fact>]
    let ``tryLoadFrom returns None for malformed file content`` () =
        let path = Path.GetTempFileName()
        try
            File.WriteAllText(path, "garbage")
            Assert.Equal(None, BoardGeometryStorage.tryLoadFrom path)
        finally
            File.Delete(path)
    [<Fact>]
    let ``Startup options parse board geometry and flags`` () =
        let options =
            Program.parseStartupOptions
                [|
                    "--demo"
                    "--timing"
                    "--board"
                    "10,20,300"
                    "--fen"
                    "8/8/8/8/8/8/8/8 w - - 0 1"
                    "--calibrate-templates"
                |]

        Assert.True(options.IsDemo)
        Assert.True(options.TimingEnabled)
        Assert.Equal(Some { Left = 10; Top = 20; Size = 300 }, options.BoardGeometry)
        Assert.Equal(Some "8/8/8/8/8/8/8/8 w - - 0 1", options.Fen)
        Assert.True(options.CalibrateTemplates)
        Assert.True(Program.tryParseBoardGeometry("10,20") |> Option.isNone)
        Assert.True(Program.tryParseBoardGeometry("10,20,0") |> Option.isNone)
        Assert.True(Program.tryParseBoardGeometry("10,top,300") |> Option.isNone)

    [<Fact>]
    let ``Startup status describes selected mode and timing`` () =
        let options =
            Program.parseStartupOptions [| "--board"; "1,2,80"; "--timing" |]

        Assert.Equal("Mode: manual board geometry - timing enabled", Program.startupStatus options)
        Assert.Equal("Mode: manual board geometry - warning", Program.statusWithWarning "Mode: manual board geometry" (Some "warning"))

    [<Fact>]
    let ``Board geometry creation uses supplied board before manual selection`` () =
        let options = Program.parseStartupOptions [| "--board"; "10,20,300" |]
        let mutable selectionRequested = false

        let geometry =
            Program.tryGetBoardGeometry options (fun () ->
                selectionRequested <- true
                None)

        Assert.Equal(Some { Left = 10; Top = 20; Size = 300 }, geometry)
        Assert.False(selectionRequested)

    [<Fact>]
    let ``Board geometry creation returns none when manual selection is cancelled`` () =
        let options = Program.parseStartupOptions [||]

        let geometry = Program.tryGetBoardGeometry options (fun () -> None)

        Assert.True(geometry.IsNone)

    [<Fact>]
    let ``Reader creation prefers command line FEN over environment FEN`` () =
        let options =
            Program.parseStartupOptions [| "--fen"; "8/8/8/8/8/8/8/8 w - - 0 1" |]

        let reader, warning =
            Program.createReader options "invalid"

        Assert.IsType<FenBoardReader>(reader) |> ignore
        Assert.True(warning.IsNone)

    [<Fact>]
    let ``Reader creation uses environment FEN when command line FEN is absent`` () =
        let options = Program.parseStartupOptions [||]

        let reader, warning =
            Program.createReader options "8/8/8/8/8/8/8/8 w - - 0 1"

        Assert.IsType<FenBoardReader>(reader) |> ignore
        Assert.True(warning.IsNone)

    [<Fact>]
    let ``Reader creation uses demo FEN and reports missing piece reader`` () =
        let demoReader, demoWarning =
            Program.createReader (Program.parseStartupOptions [| "--demo" |]) null

        let defaultReader, defaultWarning =
            Program.createReader (Program.parseStartupOptions [||]) null

        Assert.IsType<FenBoardReader>(demoReader) |> ignore
        Assert.True(demoWarning.IsNone)
        Assert.IsType<UncertainBoardReader>(defaultReader) |> ignore
        Assert.Equal(Some "No templates found in 'templates'", defaultWarning)

    [<Fact>]
    let ``save writes geometry that tryLoad reads back`` () =
        let geometry = { Left = 42; Top = 99; Size = 512 }
        BoardGeometryStorage.save geometry
        Assert.Equal(Some geometry, BoardGeometryStorage.tryLoad())
