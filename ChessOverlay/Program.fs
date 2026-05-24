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
            BoardGeometry: BoardGeometry option
            Fen: string option
            PieceReader: string option
            PieceTemplates: string option
            CalibrateTemplates: bool
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
            BoardGeometry = tryArgumentValue "--board" args |> Option.bind tryParseBoardGeometry
            Fen = tryArgumentValue "--fen" args
            PieceReader = tryArgumentValue "--piece-reader" args
            PieceTemplates = tryArgumentValue "--piece-templates" args
            CalibrateTemplates = args |> Array.contains "--calibrate-templates"
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
        elif options.CalibrateTemplates then
            mode + " - calibrating templates"
        else
            mode

    let tryGetBoardGeometry options selectBoardGeometry =
        match options.BoardGeometry with
        | Some geometry -> Some geometry
        | None when options.IsDemo -> Some(centeredDemoGeometry ())
        | None -> selectBoardGeometry ()

    [<ExcludeFromCodeCoverage>]
    let private selectBoardGeometry () =
        use selector = new BoardSelectionWindow()
        let result = selector.ShowDialog()

        if result = DialogResult.OK then
            selector.SelectedGeometry
        else
            None

    let private shouldUseTemplates options =
        options.PieceReader = Some "template"
        || options.PieceTemplates.IsSome

    let private templatePath options =
        options.PieceTemplates |> Option.defaultValue "templates"

    let private defaultSimilarityThreshold = 0.75

    let private createTemplateReader templatesPath =
        let templates = PieceTemplates.loadAllFromDirectory templatesPath

        if Array.isEmpty templates then
            UncertainBoardReader() :> IBoardReader, Some(sprintf "No templates found in '%s'" templatesPath)
        else
            TemplateBoardReader(templates, defaultSimilarityThreshold) :> IBoardReader, None

    let private createFenReader value =
        FenBoardReader(value) :> IBoardReader, None

    let private configuredFen options environmentFen =
        [ options.Fen; Option.ofObj environmentFen ]
        |> List.tryFind (fun value ->
            value
            |> Option.exists (String.IsNullOrWhiteSpace >> not))
        |> Option.flatten

    let createReader options environmentFen =
        if shouldUseTemplates options then
            createTemplateReader (templatePath options)
        else
            match configuredFen options environmentFen with
            | Some value -> createFenReader value
            | None when options.IsDemo -> createFenReader startingFen
            | None -> createTemplateReader "templates"

    [<ExcludeFromCodeCoverage>]
    let private templateCount path =
        let templates = PieceTemplates.loadAllFromDirectory path

        try
            templates.Length
        finally
            templates |> Array.iter (fun (_, bitmap) -> bitmap.Dispose())

    [<ExcludeFromCodeCoverage>]
    let private shouldCalibrateTemplates options environmentFen =
        let path = templatePath options

        options.CalibrateTemplates
        || configuredFen options environmentFen |> Option.isNone
           && not options.IsDemo
           && templateCount path = 0

    [<ExcludeFromCodeCoverage>]
    let private calibrateTemplates options environmentFen geometry =
        if not (shouldCalibrateTemplates options environmentFen) then
            None
        else
            let path = templatePath options
            let capturedBitmap, _ = ScreenCapture.captureVirtualScreen ()
            use capture = capturedBitmap
            let savedCount = PieceTemplateCalibration.saveStartingPositionTemplates capture geometry path

            if savedCount = 32 then
                Some(sprintf "Calibrated 32 starting-position templates in '%s'" path)
            else
                Some(sprintf "Calibrated %i of 32 templates in '%s'; start from a normal initial board" savedCount path)

    let statusWithWarning status warning =
        warning
        |> Option.map (fun value -> status + " - " + value)
        |> Option.defaultValue status

    [<ExcludeFromCodeCoverage>]
    let private runOverlay options boardGeometry =
        use overlay = new OverlayWindow()
        let environmentFen = Environment.GetEnvironmentVariable "CHESS_OVERLAY_FEN"

        let calibrationWarning = calibrateTemplates options environmentFen boardGeometry

        let reader, warning =
            createReader
                options
                environmentFen

        let warning =
            match calibrationWarning, warning with
            | Some calibration, Some readerWarning -> Some(calibration + " - " + readerWarning)
            | Some calibration, None -> Some calibration
            | None, Some readerWarning -> Some readerWarning
            | None, None -> None

        use controller = new OverlayController(boardGeometry, reader, overlay, timingEnabled = options.TimingEnabled)
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

        tryGetBoardGeometry options selectBoardGeometry
        |> Option.map (runOverlay options)
        |> Option.defaultValue 1
