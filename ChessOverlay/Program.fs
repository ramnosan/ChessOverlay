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
            PreferGpu: bool
            BoardGeometry: BoardGeometry option
            Fen: string option
            PieceReader: string option
            PieceModel: string option
            PieceLabels: string option
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
            PreferGpu = args |> Array.contains "--gpu"
            BoardGeometry = tryArgumentValue "--board" args |> Option.bind tryParseBoardGeometry
            Fen = tryArgumentValue "--fen" args
            PieceReader = tryArgumentValue "--piece-reader" args
            PieceModel = tryArgumentValue "--piece-model" args
            PieceLabels = tryArgumentValue "--piece-labels" args
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

    let private shouldUseYolo options =
        options.PieceReader = Some "yolo"
        || (options.PieceModel.IsSome && options.PieceLabels.IsSome)

    let createReader options environmentFen registerDisposable =
        match options.Fen, environmentFen with
        | _ when shouldUseYolo options ->
            match options.PieceModel, options.PieceLabels with
            | Some modelPath, Some labelsPath when IO.File.Exists modelPath && IO.File.Exists labelsPath ->
                let labels = YoloLabels.load labelsPath
                let classCount = YoloLabels.classCount labels
                let yoloDetector = new OnnxYoloObjectDetector(modelPath, options.PreferGpu, ?classCount = classCount)
                registerDisposable (yoloDetector :> IDisposable)
                YoloBoardReader(yoloDetector :> IYoloObjectDetector, labels) :> IBoardReader, None
            | _ ->
                UncertainBoardReader() :> IBoardReader, Some "YOLO model or labels missing"
        | Some value, _ when not (String.IsNullOrWhiteSpace value) ->
            FenBoardReader(value) :> IBoardReader, None
        | _, value when not (String.IsNullOrWhiteSpace value) ->
            FenBoardReader(value) :> IBoardReader, None
        | _ when options.IsDemo ->
            FenBoardReader(startingFen) :> IBoardReader, None
        | _ ->
            UncertainBoardReader() :> IBoardReader, Some "YOLO model or labels missing"

    let statusWithWarning status warning =
        warning
        |> Option.map (fun value -> status + " - " + value)
        |> Option.defaultValue status

    [<ExcludeFromCodeCoverage>]
    let private runOverlay options detector =
        use overlay = new OverlayWindow()
        let mutable disposableReaderDependency: IDisposable option = None

        let reader, warning =
            createReader
                options
                (Environment.GetEnvironmentVariable "CHESS_OVERLAY_FEN")
                (fun disposable -> disposableReaderDependency <- Some disposable)

        use controller = new OverlayController(detector, reader, overlay, timingEnabled = options.TimingEnabled)
        overlay.Load.Add(fun _ ->
            overlay.ShowStatus(statusWithWarning (startupStatus options) warning)
            controller.Start())

        Application.Run overlay
        disposableReaderDependency |> Option.iter _.Dispose()
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
