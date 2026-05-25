namespace ChessOverlay

open System
open System.Diagnostics.CodeAnalysis
open System.Windows.Forms

module BoardGeometryStorage =
    open System.IO

    let private storageDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChessOverlay")

    let private storagePath = Path.Combine(storageDir, "board_area.txt")

    let tryLoad () =
        try
            if File.Exists(storagePath) then
                let text = File.ReadAllText(storagePath).Trim()

                match text.Split(',') with
                | [| leftStr; topStr; sizeStr |] ->
                    match Int32.TryParse leftStr, Int32.TryParse topStr, Int32.TryParse sizeStr with
                    | (true, left), (true, top), (true, size) when size > 0 ->
                        Some { Left = left; Top = top; Size = size }
                    | _ -> None
                | _ -> None
            else
                None
        with _ ->
            None

    let save (geometry: BoardGeometry) =
        try
            Directory.CreateDirectory(storageDir) |> ignore
            File.WriteAllText(storagePath, sprintf "%d,%d,%d" geometry.Left geometry.Top geometry.Size)
        with _ ->
            ()

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

    let private shouldUseTemplates options =
        options.PieceReader = Some "template"
        || options.PieceTemplates.IsSome

    let private templatePath options =
        options.PieceTemplates |> Option.defaultValue "templates"

    // Matching compares only the isolated piece (not its square background), and
    // the score is further scaled by how well the piece contrasts match. Correct
    // pieces sit well above empty/wrong squares (which stay near zero), so the
    // acceptance bar is much lower than whole-square correlation; this value also
    // leaves headroom for the few-pixel misalignment of a hand-selected board.
    let private defaultSimilarityThreshold = 0.35

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
    let private runOverlay options (initialGeometry: BoardGeometry option) =
        use overlay = new OverlayWindow()
        let environmentFen = Environment.GetEnvironmentVariable "CHESS_OVERLAY_FEN"

        let calibrationWarning =
            initialGeometry |> Option.bind (calibrateTemplates options environmentFen)

        let reader, warning = createReader options environmentFen

        let mergedWarning =
            match calibrationWarning, warning with
            | Some calibration, Some readerWarning -> Some(calibration + " - " + readerWarning)
            | Some calibration, None -> Some calibration
            | None, Some readerWarning -> Some readerWarning
            | None, None -> None

        use controller = new OverlayController(initialGeometry, reader, overlay, timingEnabled = options.TimingEnabled)

        overlay.Load.Add(fun _ ->
            let statusMsg =
                match initialGeometry with
                | Some _ -> statusWithWarning (startupStatus options) mergedWarning
                | None -> "Press Ctrl+Shift+B to select the board area"

            overlay.ShowStatus(statusMsg)
            controller.Start())

        overlay.SelectBoardRequested.Add(fun () ->
            use selector = new BoardSelectionWindow()

            if selector.ShowDialog() = DialogResult.OK then
                match selector.SelectedGeometry with
                | Some geometry ->
                    BoardGeometryStorage.save geometry
                    controller.UpdateGeometry(geometry)
                | None -> ())

        Application.Run overlay
        0

    [<STAThread>]
    [<EntryPoint>]
    [<ExcludeFromCodeCoverage>]
    let main args =
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault false

        let options = parseStartupOptions args
        let initialGeometry = tryGetBoardGeometry options BoardGeometryStorage.tryLoad
        runOverlay options initialGeometry
