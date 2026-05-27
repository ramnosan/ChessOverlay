namespace ChessOverlay

open System
open System.Diagnostics.CodeAnalysis
open System.Windows.Forms

module BoardGeometryStorage =
    open System.IO

    let private storageDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChessOverlay")

    let private storagePath = Path.Combine(storageDir, "board_area.txt")

    let internal tryParseThreeInts (a: string) (b: string) (c: string) =
        match Int32.TryParse a, Int32.TryParse b, Int32.TryParse c with
        | (true, x), (true, y), (true, z) when z > 0 -> Some(x, y, z)
        | _ -> None

    let tryParseGeometry (text: string) =
        match text.Split(',') with
        | [| leftStr; topStr; sizeStr |] ->
            tryParseThreeInts leftStr topStr sizeStr
            |> Option.map (fun (left, top, size) -> { Left = left; Top = top; Size = size })
        | _ -> None

    let tryLoadFrom path =
        try
            if File.Exists(path) then
                File.ReadAllText(path).Trim() |> tryParseGeometry
            else
                None
        with _ ->
            None

    let tryLoad () = tryLoadFrom storagePath

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

    let private splitOptions =
        StringSplitOptions.TrimEntries ||| StringSplitOptions.RemoveEmptyEntries

    let tryParseBoardGeometry (value: string) =
        let parts = value.Split(',', splitOptions)

        if parts.Length <> 3 then
            None
        else
            BoardGeometryStorage.tryParseThreeInts parts[0] parts[1] parts[2]
            |> Option.map (fun (left, top, size) -> { Left = left; Top = top; Size = size })

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

    // Move-hint / premove dots ship with the app (an empty highlighted square
    // carries no piece to calibrate from), so they load from beside the binary
    // rather than the user-calibrated piece-templates directory.
    let private fieldTemplatesPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "FieldTemplates")

    let private createTemplateReader templatesPath =
        let templates = PieceTemplates.loadAllFromDirectory templatesPath

        if Array.isEmpty templates then
            UncertainBoardReader() :> IBoardReader, Some(sprintf "No templates found in '%s'" templatesPath)
        else
            let fieldTemplates = PieceTemplates.loadFieldTemplates fieldTemplatesPath
            TemplateBoardReader(templates, fieldTemplates, defaultSimilarityThreshold) :> IBoardReader, None

    let private createFenReader value =
        FenBoardReader(value) :> IBoardReader, None

    let private configuredFen options environmentFen =
        [ options.Fen; Option.ofObj environmentFen ]
        |> List.tryFind (fun value ->
            value
            |> Option.exists (String.IsNullOrWhiteSpace >> not))
        |> Option.flatten

    let private createChromeFallbackReader templatesPath =
        let template, warning = createTemplateReader templatesPath
        let chrome = ChromeBoardDetector.ChromeBoardReader() :> IBoardReader
        FallbackBoardReader(chrome, template) :> IBoardReader, warning

    let private createReaderFromFen options environmentFen =
        match configuredFen options environmentFen with
        | Some value -> createFenReader value
        | None when options.IsDemo -> createFenReader startingFen
        | None -> createChromeFallbackReader "templates"

    let createReader options environmentFen =
        if shouldUseTemplates options then
            createTemplateReader (templatePath options)
        else
            createReaderFromFen options environmentFen

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
        let reader =
            LastBoardStateReader(reader, LastBoardStateStorage.tryLoad, LastBoardStateStorage.save) :> IBoardReader

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

        let mutable paused = false

        overlay.ToggleOverlayRequested.Add(fun () ->
            if paused then
                paused <- false
                overlay.ShowOverlayUi()
                controller.Start()
            else
                paused <- true
                controller.Stop()
                overlay.HideOverlayUi())

        overlay.SelectBoardRequested.Add(fun () ->
            let applyGeometry geometry =
                BoardGeometryStorage.save geometry
                controller.UpdateGeometry geometry

            use chromeDialog = new ChromeBoardSelectionDialog()
            chromeDialog.ShowDialog() |> ignore

            if chromeDialog.WantsManualSelection then
                use selector = new BoardSelectionWindow()

                if selector.ShowDialog() = DialogResult.OK then
                    selector.SelectedGeometry |> Option.iter applyGeometry
            else
                chromeDialog.SelectedGeometry |> Option.iter applyGeometry)

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
