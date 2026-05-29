namespace ChessOverlay

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.Threading.Tasks
open System.Windows.Forms

module internal ChromeDomRenderDelta =
    type RenderKey =
        {
            Geometry: BoardGeometry
            Board: BoardState
            Strategy: string
        }

    let tryKey geometry (reading: BoardReading) =
        if reading.Strategy.StartsWith("Chrome ", StringComparison.Ordinal) then
            Some
                {
                    Geometry = geometry
                    Board = reading.Board
                    Strategy = reading.Strategy
                }
        else
            None

    let shouldRecalculate lastKey geometry reading =
        match tryKey geometry reading with
        | Some key -> Some key <> lastKey
        | None -> true

module internal ChromeOverlayVisibility =
    /// When a DOM (Chrome tab) reader is active but returns no reading, the tab is
    /// minimized, backgrounded, closed, or navigated away from the board — it is no
    /// longer visible on screen. The overlay must then be cleared completely: leaving
    /// a stale board outline or piece labels would float over unrelated content.
    ///
    let shouldClearOverlay (domReaderActive: bool) (domReading: BoardReading option) : bool =
        domReaderActive && domReading.IsNone

[<ExcludeFromCodeCoverage>]
type OverlayController(
    initialGeometry: BoardGeometry option,
    reader: IBoardReader,
    overlay: OverlayWindow,
    ?timingEnabled: bool,
    ?scanIntervalMilliseconds: int) =

    let scanInterval = defaultArg scanIntervalMilliseconds 500
    let domScanInterval = 30
    let domReader =
        match reader with
        | :? IDomBoardReader as value when value.IsDomAvailable -> Some value
        | _ -> None

    let timingEnabled = defaultArg timingEnabled false
    let confidenceThreshold = BoardReadingConfidence.minimumUsable
    let timer = new Timer(Interval = if domReader.IsSome then domScanInterval else scanInterval)
    let scanGate = obj ()
    let mutable scanInProgress = false
    let mutable isRunning = false
    let mutable scanGeneration = 0
    let mutable boardGeometry = initialGeometry
    let mutable lastChromeDomRenderKey: ChromeDomRenderDelta.RenderKey option = None
    let mutable lastScreenScan = DateTime.MinValue

    let toVirtualScreenGeometry (origin: Point) (geometry: BoardGeometry) =
        {
            Left = geometry.Left + origin.X
            Top = geometry.Top + origin.Y
            Size = geometry.Size
        }

    let currentVirtualScreenGeometry geometry =
        let bounds = SystemInformation.VirtualScreen
        toVirtualScreenGeometry bounds.Location geometry

    let measure name action =
        let stopwatch = Stopwatch.StartNew()
        let result = action ()
        stopwatch.Stop()

        if timingEnabled then
            Debug.WriteLine(sprintf "Timing: %s %ims" name stopwatch.ElapsedMilliseconds)

        result

    let isActiveGeneration generation =
        lock scanGate (fun () -> isRunning && generation = scanGeneration)

    let updateOverlay generation action =
        if isActiveGeneration generation && overlay.IsHandleCreated && not overlay.IsDisposed then
            overlay.BeginInvoke(
                MethodInvoker(fun () ->
                    if isActiveGeneration generation then
                        action ()))
            |> ignore

    let uncertainStatus reading =
        match reader, reading with
        | :? UncertainBoardReader, _ -> "Board selected - no piece templates loaded"
        | _, None -> "Board selected - piece reader unavailable"
        | _, Some boardReading when boardReading.Board.IsEmpty -> sprintf "Reader: %s - 0 pieces matched" boardReading.Strategy
        | _, Some boardReading -> sprintf "Reader: %s - only %i pieces matched" boardReading.Strategy boardReading.Board.Count

    let candidateNotation piece =
        let color =
            match piece.Color with
            | Black -> "b"
            | White -> "w"

        let kind =
            match piece.Kind with
            | King -> "K"
            | Queen -> "Q"
            | Rook -> "R"
            | Bishop -> "B"
            | Knight -> "N"
            | Pawn -> "P"

        color + kind

    let logCandidates (reading: BoardReading) =
        if timingEnabled then
            reading.Candidates
            |> Map.toSeq
            |> Seq.choose (fun (square, candidates) ->
                candidates
                |> List.tryHead
                |> Option.map (fun candidate -> square, candidate))
            |> Seq.sortByDescending (fun (_, candidate) -> candidate.Score)
            |> Seq.truncate 16
            |> Seq.map (fun (square, candidate) ->
                sprintf "%s=%s %.2f" (Squares.name square) (candidateNotation candidate.Piece) candidate.Score)
            |> String.concat "; "
            |> fun summary -> Debug.WriteLine("Piece candidates: " + summary)

    let shouldRunScreenScan () =
        if domReader.IsNone then
            true
        else
            let now = DateTime.UtcNow
            let elapsed = now - lastScreenScan

            if elapsed.TotalMilliseconds >= float scanInterval then
                lastScreenScan <- now
                true
            else
                false

    let renderReading generation screenGeometry reading =
        reading |> Option.iter logCandidates

        match reading with
        | Some boardReading when boardReading.Confidence >= confidenceThreshold ->
            if ChromeDomRenderDelta.shouldRecalculate lastChromeDomRenderKey screenGeometry boardReading then
                lastChromeDomRenderKey <- ChromeDomRenderDelta.tryKey screenGeometry boardReading

                let attackArrows = AttackCalculator.enemyAttackArrows boardReading.Board
                let friendlyForkMoveArrows = AttackCalculator.friendlyForkMoveArrows boardReading.Board
                let enemyForkMoveArrows = AttackCalculator.enemyForkMoveArrows boardReading.Board
                let hangingSquares = AttackCalculator.hangingSquares boardReading.Board
                let enemyHangingSquares = AttackCalculator.enemyHangingSquares boardReading.Board

                let forkSquares =
                    AttackCalculator.enemyForks boardReading.Board
                    |> List.map fst
                    |> Set.ofList

                measure
                    "overlay-update"
                    (fun () ->
                        updateOverlay
                            generation
                            (fun () ->
                                overlay.ShowFrame
                                    {
                                        Geometry = screenGeometry
                                        AttackArrows = attackArrows
                                        FriendlyForkMoveArrows = friendlyForkMoveArrows
                                        EnemyForkMoveArrows = enemyForkMoveArrows
                                        HangingSquares = hangingSquares
                                        EnemyHangingSquares = enemyHangingSquares
                                        ForkSquares = forkSquares
                                        DetectedPieces = Some boardReading.Board
                                        Strategy = Some boardReading.Strategy
                                    }))
        | _ ->
            lastChromeDomRenderKey <- None
            let status = uncertainStatus reading
            measure
                "overlay-update"
                (fun () -> updateOverlay generation (fun () -> overlay.ShowUncertainBoard(screenGeometry, status)))

    let scanOnce generation =
        try
            match boardGeometry with
            | None -> ()
            | Some geometry ->
                let screenGeometry = currentVirtualScreenGeometry geometry
                let domReading =
                    domReader
                    |> Option.bind (fun value -> measure "dom-reading" value.ReadDom)

                match domReading with
                | Some reading -> renderReading generation screenGeometry (Some reading)
                | None when ChromeOverlayVisibility.shouldClearOverlay domReader.IsSome domReading ->
                    lastChromeDomRenderKey <- None
                    measure "overlay-clear" (fun () -> updateOverlay generation overlay.ClearFrame)
                | None when shouldRunScreenScan () ->
                    let capturedBitmap, origin = measure "capture" ScreenCapture.captureVirtualScreen
                    use capture = capturedBitmap

                    let screenGeometry = toVirtualScreenGeometry origin geometry
                    let reading = measure "piece-reading" (fun () -> reader.Read(capture, geometry))
                    renderReading generation screenGeometry reading
                | None -> ()
        finally
            lock scanGate (fun () -> scanInProgress <- false)

    let startScan () =
        let scanToStart =
            lock scanGate (fun () ->
                if scanInProgress || not isRunning then
                    None
                else
                    scanInProgress <- true
                    Some scanGeneration)

        scanToStart
        |> Option.iter (fun generation -> Task.Run(fun () -> scanOnce generation) |> ignore)

    do
        timer.Tick.Add(fun _ -> startScan ())

    member _.Start() =
        lock scanGate (fun () ->
            isRunning <- true
            scanGeneration <- scanGeneration + 1)

        lastChromeDomRenderKey <- None
        lastScreenScan <- DateTime.MinValue

        if boardGeometry.IsSome then
            timer.Start()
            startScan ()

    member _.UpdateGeometry(geometry: BoardGeometry) =
        boardGeometry <- Some geometry

        if isRunning && not timer.Enabled then
            timer.Start()

        if isRunning then
            startScan ()

    member _.Stop() =
        timer.Stop()

        lock scanGate (fun () ->
            isRunning <- false
            scanGeneration <- scanGeneration + 1)

        lastChromeDomRenderKey <- None
        lastScreenScan <- DateTime.MinValue

    interface IDisposable with
        member _.Dispose() =
            timer.Dispose()
