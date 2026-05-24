namespace ChessOverlay

open System
open System.Diagnostics.CodeAnalysis
open System.Windows.Forms

module Program =
    let private startingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w - - 0 1"

    type StartupOptions =
        {
            IsDemo: bool
            TimingEnabled: bool
            ScanAutomatically: bool
            BoardGeometry: BoardGeometry option
            Fen: string option
        }

    let tryArgumentValue name (args: string array) =
        args
        |> Array.tryFindIndex ((=) name)
        |> Option.bind (fun index ->
            if index + 1 < args.Length then
                Some args[index + 1]
            else
                None)

    let tryParseBoardGeometry (value: string) =
        let parts = value.Split(',', StringSplitOptions.TrimEntries ||| StringSplitOptions.RemoveEmptyEntries)

        if parts.Length <> 3 then
            None
        else
            match Int32.TryParse parts[0], Int32.TryParse parts[1], Int32.TryParse parts[2] with
            | (true, left), (true, top), (true, size) when size > 0 ->
                Some { Left = left; Top = top; Size = size }
            | _ -> None

    let parseStartupOptions args =
        {
            IsDemo = args |> Array.contains "--demo"
            TimingEnabled = args |> Array.contains "--timing"
            ScanAutomatically = args |> Array.contains "--scan"
            BoardGeometry = tryArgumentValue "--board" args |> Option.bind tryParseBoardGeometry
            Fen = tryArgumentValue "--fen" args
        }

    [<ExcludeFromCodeCoverage>]
    let private centeredDemoGeometry () =
        let bounds = Screen.PrimaryScreen.Bounds
        let size = min 640 (min bounds.Width bounds.Height - 120)

        {
            Left = bounds.Left + (bounds.Width - size) / 2
            Top = bounds.Top + (bounds.Height - size) / 2
            Size = size
        }

    let startupStatus options =
        let mode =
            if options.IsDemo then
                "Mode: demo"
            elif options.BoardGeometry.IsSome then
                "Mode: manual board geometry"
            elif options.ScanAutomatically then
                "Mode: scanning for chessboard"
            else
                "Mode: selected board area"

        if options.TimingEnabled then
            mode + " - timing enabled"
        else
            mode

    let tryCreateDetector options selectBoardGeometry =
        match options.BoardGeometry with
        | Some geometry -> Some(FixedBoardDetector geometry :> IBoardDetector)
        | None when options.IsDemo -> Some(FixedBoardDetector(centeredDemoGeometry ()) :> IBoardDetector)
        | None when options.ScanAutomatically -> Some(ConservativeBoardDetector() :> IBoardDetector)
        | None ->
            selectBoardGeometry ()
            |> Option.map (fun geometry -> FixedBoardDetector geometry :> IBoardDetector)

    [<ExcludeFromCodeCoverage>]
    let private selectBoardGeometry () =
        use selector = new BoardSelectionWindow()
        let result = selector.ShowDialog()

        if result = DialogResult.OK then
            selector.SelectedGeometry
        else
            None

    let createReader options environmentFen =
        match options.Fen, environmentFen with
        | Some value, _ when not (String.IsNullOrWhiteSpace value) ->
            FenBoardReader(value) :> IBoardReader, None
        | _, value when not (String.IsNullOrWhiteSpace value) ->
            FenBoardReader(value) :> IBoardReader, None
        | _ when options.IsDemo ->
            FenBoardReader(startingFen) :> IBoardReader, None
        | _ ->
            UncertainBoardReader() :> IBoardReader, Some "No piece reader configured"

    let statusWithWarning status warning =
        warning
        |> Option.map (fun value -> status + " - " + value)
        |> Option.defaultValue status

    [<ExcludeFromCodeCoverage>]
    let private runOverlay options detector =
        use overlay = new OverlayWindow()

        let reader, warning =
            createReader
                options
                (Environment.GetEnvironmentVariable "CHESS_OVERLAY_FEN")

        use controller = new OverlayController(detector, reader, overlay, timingEnabled = options.TimingEnabled)
        overlay.Load.Add(fun _ ->
            overlay.ShowStatus(statusWithWarning (startupStatus options) warning)
            controller.Start())

        Application.Run overlay
        0

    [<STAThread>]
    [<EntryPoint>]
    [<ExcludeFromCodeCoverage>]
    let main args =
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault false

        let options = parseStartupOptions args

        tryCreateDetector options selectBoardGeometry
        |> Option.map (runOverlay options)
        |> Option.defaultValue 1
