namespace ChessOverlay

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.Windows.Forms

[<ExcludeFromCodeCoverage>]
type OverlayController(
    detector: IBoardDetector,
    reader: IBoardReader,
    overlay: OverlayWindow,
    ?timingEnabled: bool,
    ?scanIntervalMilliseconds: int) =

    let scanInterval = defaultArg scanIntervalMilliseconds 750
    let timingEnabled = defaultArg timingEnabled false
    let confidenceThreshold = 0.45
    let timer = new Timer(Interval = scanInterval)
    let mutable scanInProgress = false

    let toVirtualScreenGeometry (origin: Point) (geometry: BoardGeometry) =
        {
            Left = geometry.Left + origin.X
            Top = geometry.Top + origin.Y
            Size = geometry.Size
        }

    let measure name action =
        let stopwatch = Stopwatch.StartNew()
        let result = action ()
        stopwatch.Stop()

        if timingEnabled then
            Debug.WriteLine(sprintf "Timing: %s %ims" name stopwatch.ElapsedMilliseconds)

        result

    let readBoardGeometry capture =
        match detector with
        | :? FixedBoardDetector as fixedDetector -> BoardDetected fixedDetector.Geometry
        | _ -> measure "board-detection" (fun () -> detector.Detect capture)

    let scanOnce () =
        if not scanInProgress then
            scanInProgress <- true

            try
                let capturedBitmap, origin = measure "capture" ScreenCapture.captureVirtualScreen
                use capture = capturedBitmap

                match readBoardGeometry capture with
                | BoardNotFound -> measure "overlay-update" (fun () -> overlay.ShowSearching())
                | BoardDetected geometry ->
                    let screenGeometry = toVirtualScreenGeometry origin geometry

                    match measure "piece-reading" (fun () -> reader.Read(capture, geometry)) with
                    | Some reading when reading.Confidence >= confidenceThreshold ->
                        let attackedSquares = AttackCalculator.enemyAttackedSquares reading.Board

                        measure
                            "overlay-update"
                            (fun () ->
                                overlay.ShowFrame
                                    {
                                        Geometry = screenGeometry
                                        HighlightedSquares = attackedSquares
                                        DetectedPieces = Some reading.Board
                                    })
                    | _ -> measure "overlay-update" (fun () -> overlay.ShowUncertainBoard screenGeometry)
            finally
                scanInProgress <- false

    do
        timer.Tick.Add(fun _ -> scanOnce ())

    member _.Start() =
        scanOnce ()
        timer.Start()

    member _.Stop() = timer.Stop()

    interface IDisposable with
        member _.Dispose() =
            timer.Dispose()
