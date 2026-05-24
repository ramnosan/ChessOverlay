namespace ChessOverlay

open System
open System.Windows.Forms

module Program =
    let private startingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w - - 0 1"

    let private tryArgumentValue name (args: string array) =
        args
        |> Array.tryFindIndex ((=) name)
        |> Option.bind (fun index ->
            if index + 1 < args.Length then
                Some args[index + 1]
            else
                None)

    let private tryParseBoardGeometry (value: string) =
        let parts = value.Split(',', StringSplitOptions.TrimEntries ||| StringSplitOptions.RemoveEmptyEntries)

        if parts.Length <> 3 then
            None
        else
            match Int32.TryParse parts[0], Int32.TryParse parts[1], Int32.TryParse parts[2] with
            | (true, left), (true, top), (true, size) when size > 0 ->
                Some { Left = left; Top = top; Size = size }
            | _ -> None

    let private centeredDemoGeometry () =
        let bounds = Screen.PrimaryScreen.Bounds
        let size = min 640 (min bounds.Width bounds.Height - 120)

        {
            Left = bounds.Left + (bounds.Width - size) / 2
            Top = bounds.Top + (bounds.Height - size) / 2
            Size = size
        }

    [<STAThread>]
    [<EntryPoint>]
    let main args =
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault false

        use overlay = new OverlayWindow()

        let isDemo = args |> Array.contains "--demo"

        let detector =
            match tryArgumentValue "--board" args |> Option.bind tryParseBoardGeometry with
            | Some geometry -> FixedBoardDetector geometry :> IBoardDetector
            | None when isDemo -> FixedBoardDetector(centeredDemoGeometry ()) :> IBoardDetector
            | None -> ConservativeBoardDetector() :> IBoardDetector

        let reader =
            match tryArgumentValue "--fen" args, Environment.GetEnvironmentVariable "CHESS_OVERLAY_FEN" with
            | Some value, _ when not (String.IsNullOrWhiteSpace value) ->
                FenBoardReader(value) :> IBoardReader
            | _, value when not (String.IsNullOrWhiteSpace value) ->
                FenBoardReader(value) :> IBoardReader
            | _ when isDemo ->
                FenBoardReader(startingFen) :> IBoardReader
            | _ -> UncertainBoardReader() :> IBoardReader

        printfn "Chess Overlay is running."
        printfn "Close this console window to stop it."

        if isDemo then
            printfn "Mode: demo"
        elif args |> Array.contains "--board" then
            printfn "Mode: manual board geometry"
        else
            printfn "Mode: scanning for chessboard"

        use controller = new OverlayController(detector, reader, overlay)
        overlay.Load.Add(fun _ -> controller.Start())

        Application.Run overlay
        0
