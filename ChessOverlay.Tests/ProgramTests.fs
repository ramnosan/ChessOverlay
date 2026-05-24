namespace ChessOverlay.Tests

open Xunit
open ChessOverlay

module ProgramTests =
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
                |]

        Assert.True(options.IsDemo)
        Assert.True(options.TimingEnabled)
        Assert.Equal(Some { Left = 10; Top = 20; Size = 300 }, options.BoardGeometry)
        Assert.Equal(Some "8/8/8/8/8/8/8/8 w - - 0 1", options.Fen)
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
    let ``Detector creation uses supplied board before manual selection`` () =
        let options = Program.parseStartupOptions [| "--board"; "10,20,300" |]
        let mutable selectionRequested = false

        let detector =
            Program.tryCreateDetector options (fun () ->
                selectionRequested <- true
                None)

        Assert.True(detector.IsSome)
        Assert.False(selectionRequested)

    [<Fact>]
    let ``Detector creation returns none when manual selection is cancelled`` () =
        let options = Program.parseStartupOptions [||]

        let detector = Program.tryCreateDetector options (fun () -> None)

        Assert.True(detector.IsNone)

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
