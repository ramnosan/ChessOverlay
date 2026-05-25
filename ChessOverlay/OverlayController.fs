namespace ChessOverlay

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.Threading.Tasks
open System.Windows.Forms

[<ExcludeFromCodeCoverage>]
type OverlayController(
    initialGeometry: BoardGeometry option,
    reader: IBoardReader,
    overlay: OverlayWindow,
    ?timingEnabled: bool,
    ?scanIntervalMilliseconds: int) =

    let scanInterval = defaultArg scanIntervalMilliseconds 500
    let timingEnabled = defaultArg timingEnabled false
    let confidenceThreshold = 0.45
    let timer = new Timer(Interval = scanInterval)
    let scanGate = obj ()
    let mutable scanInProgress = false
    let mutable boardGeometry = initialGeometry

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

    let updateOverlay action =
        if overlay.IsHandleCreated && not overlay.IsDisposed then
            overlay.BeginInvoke(MethodInvoker action) |> ignore

    let uncertainStatus reading =
        match reader, reading with
        | :? UncertainBoardReader, _ -> "Board selected - no piece templates loaded"
        | _, None -> "Board selected - piece reader unavailable"
        | _, Some boardReading when boardReading.Board.IsEmpty -> "Board selected - 0 pieces matched"
        | _, Some boardReading -> sprintf "Board selected - only %i pieces matched" boardReading.Board.Count

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

    let scanOnce () =
        match boardGeometry with
        | None -> lock scanGate (fun () -> scanInProgress <- false)
        | Some geometry ->
            try
                let capturedBitmap, origin = measure "capture" ScreenCapture.captureVirtualScreen
                use capture = capturedBitmap

                let screenGeometry = toVirtualScreenGeometry origin geometry

                let reading = measure "piece-reading" (fun () -> reader.Read(capture, geometry))
                reading |> Option.iter logCandidates

                match reading with
                | Some boardReading when boardReading.Confidence >= confidenceThreshold ->
                    let attackArrows = AttackCalculator.enemyAttackArrows boardReading.Board
                    let friendlyForkMoveArrows = AttackCalculator.friendlyForkMoveArrows boardReading.Board
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
                                (fun () ->
                                    overlay.ShowFrame
                                        {
                                            Geometry = screenGeometry
                                            AttackArrows = attackArrows
                                            FriendlyForkMoveArrows = friendlyForkMoveArrows
                                            HangingSquares = hangingSquares
                                            EnemyHangingSquares = enemyHangingSquares
                                            ForkSquares = forkSquares
                                            DetectedPieces = Some boardReading.Board
                                        }))
                | _ ->
                    let status = uncertainStatus reading
                    measure "overlay-update" (fun () -> updateOverlay (fun () -> overlay.ShowUncertainBoard(screenGeometry, status)))
            finally
                lock scanGate (fun () ->
                    scanInProgress <- false)

    let startScan () =
        let shouldStart =
            lock scanGate (fun () ->
                if scanInProgress then
                    false
                else
                    scanInProgress <- true
                    true)

        if shouldStart then
            Task.Run(fun () -> scanOnce ()) |> ignore

    do
        timer.Tick.Add(fun _ -> startScan ())

    member _.Start() =
        if boardGeometry.IsSome then
            timer.Start()
            startScan ()

    member _.UpdateGeometry(geometry: BoardGeometry) =
        boardGeometry <- Some geometry

        if not timer.Enabled then
            timer.Start()

        startScan ()

    member _.Stop() = timer.Stop()

    interface IDisposable with
        member _.Dispose() =
            timer.Dispose()
