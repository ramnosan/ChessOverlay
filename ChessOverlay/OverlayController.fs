namespace ChessOverlay

open System
open System.Drawing
open System.Windows.Forms

type OverlayController(
    detector: IBoardDetector,
    reader: IBoardReader,
    overlay: OverlayWindow,
    ?scanIntervalMilliseconds: int) =

    let scanInterval = defaultArg scanIntervalMilliseconds 750
    let confidenceThreshold = 0.90
    let timer = new Timer(Interval = scanInterval)
    let mutable scanInProgress = false

    let toVirtualScreenGeometry (origin: Point) (geometry: BoardGeometry) =
        {
            Left = geometry.Left + origin.X
            Top = geometry.Top + origin.Y
            Size = geometry.Size
        }

    let scanOnce () =
        if not scanInProgress then
            scanInProgress <- true

            try
                let capturedBitmap, origin = ScreenCapture.captureVirtualScreen ()
                use capture = capturedBitmap

                match detector.Detect capture with
                | BoardNotFound -> overlay.ShowSearching()
                | BoardDetected geometry ->
                    let screenGeometry = toVirtualScreenGeometry origin geometry

                    match reader.Read(capture, geometry) with
                    | Some reading when reading.Confidence >= confidenceThreshold ->
                        let attackedSquares = AttackCalculator.enemyAttackedSquares reading.Board

                        overlay.ShowFrame
                            {
                                Geometry = screenGeometry
                                HighlightedSquares = attackedSquares
                            }
                    | _ -> overlay.ShowUncertainBoard screenGeometry
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
