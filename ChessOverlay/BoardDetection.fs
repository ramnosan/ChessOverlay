namespace ChessOverlay

open System
open System.Drawing

type IBoardDetector =
    abstract Detect: Bitmap -> DetectionResult

type IBoardReader =
    abstract Read: Bitmap * BoardGeometry -> BoardReading option

type ConservativeBoardDetector() =
    let minimumBoardSize = 160
    let coarseMaxDimension = 420
    let coarseCandidateStep = 20
    let coarseSizeStep = 20
    let refineStep = 4
    let refinedCandidateCount = 4

    let colorDistance (left: Color) (right: Color) =
        let dr = int left.R - int right.R
        let dg = int left.G - int right.G
        let db = int left.B - int right.B
        sqrt (float (dr * dr + dg * dg + db * db))

    let clamp minimum maximum value =
        min maximum (max minimum value)

    let sampleAverage (bitmap: Bitmap) x y width height =
        let mutable r = 0L
        let mutable g = 0L
        let mutable b = 0L
        let mutable count = 0L
        let boundedX = clamp 0 (bitmap.Width - 1) x
        let boundedY = clamp 0 (bitmap.Height - 1) y
        let boundedWidth = max 1 (min width (bitmap.Width - boundedX))
        let boundedHeight = max 1 (min height (bitmap.Height - boundedY))
        let samplePoints =
            [ 1, 1
              3, 1
              1, 3
              3, 3 ]

        for xWeight, yWeight in samplePoints do
            let sampleX = boundedX + boundedWidth * xWeight / 4
            let sampleY = boundedY + boundedHeight * yWeight / 4
            let pixel = bitmap.GetPixel(sampleX, sampleY)
            r <- r + int64 pixel.R
            g <- g + int64 pixel.G
            b <- b + int64 pixel.B
            count <- count + 1L

        if count = 0L then
            Color.Black
        else
            Color.FromArgb(int (r / count), int (g / count), int (b / count))

    let scoreCandidate includeEdges minimumSquareSize (bitmap: Bitmap) left top size =
        let squareSize = size / 8

        if squareSize < minimumSquareSize then
            0.0
        else
            let colors =
                [ for rank in 0 .. 7 do
                      for file in 0 .. 7 do
                          let inset = max 2 (squareSize / 5)
                          let color =
                              sampleAverage
                                  bitmap
                                  (left + file * squareSize + inset)
                                  (top + rank * squareSize + inset)
                                  (squareSize - inset * 2)
                                  (squareSize - inset * 2)

                          file, rank, color ]

            let lightSquares =
                colors
                |> List.filter (fun (file, rank, _) -> (file + rank) % 2 = 0)
                |> List.map (fun (_, _, color) -> color)

            let darkSquares =
                colors
                |> List.filter (fun (file, rank, _) -> (file + rank) % 2 = 1)
                |> List.map (fun (_, _, color) -> color)

            let averageColor (values: Color list) =
                let count = List.length values
                let r = values |> List.sumBy (fun color -> int color.R)
                let g = values |> List.sumBy (fun color -> int color.G)
                let b = values |> List.sumBy (fun color -> int color.B)
                Color.FromArgb(r / count, g / count, b / count)

            let lightAverage = averageColor lightSquares
            let darkAverage = averageColor darkSquares
            let contrast = colorDistance lightAverage darkAverage

            let patternPenalty =
                colors
                |> List.averageBy (fun (file, rank, color) ->
                    let expected =
                        if (file + rank) % 2 = 0 then
                            lightAverage
                        else
                            darkAverage

                    colorDistance color expected)

            let rowConsistency =
                [ for rank in 0 .. 7 do
                      let rankColors = colors |> List.filter (fun (_, squareRank, _) -> squareRank = rank)

                      let rankContrast =
                          rankColors
                          |> List.pairwise
                          |> List.averageBy (fun ((_, _, leftColor), (_, _, rightColor)) ->
                              colorDistance leftColor rightColor)

                      rankContrast ]
                |> List.average

            let edgeContrast =
                if not includeEdges then
                    0.0
                else
                    let edgeInset = max 2 (squareSize / 12)
                    let edgeThickness = max 2 (squareSize / 10)
                    let outsideOffset = max 3 (squareSize / 6)
                    let samples =
                        [ for index in 0 .. 2 .. 6 do
                              let center = index * squareSize + squareSize / 2

                              if top - outsideOffset >= 0 then
                                  sampleAverage bitmap (left + center - edgeThickness / 2) (top + edgeInset) edgeThickness edgeThickness,
                                  sampleAverage bitmap (left + center - edgeThickness / 2) (top - outsideOffset) edgeThickness edgeThickness

                              if top + size + outsideOffset < bitmap.Height then
                                  sampleAverage bitmap (left + center - edgeThickness / 2) (top + size - edgeInset - edgeThickness) edgeThickness edgeThickness,
                                  sampleAverage bitmap (left + center - edgeThickness / 2) (top + size + outsideOffset - edgeThickness) edgeThickness edgeThickness

                              if left - outsideOffset >= 0 then
                                  sampleAverage bitmap (left + edgeInset) (top + center - edgeThickness / 2) edgeThickness edgeThickness,
                                  sampleAverage bitmap (left - outsideOffset) (top + center - edgeThickness / 2) edgeThickness edgeThickness

                              if left + size + outsideOffset < bitmap.Width then
                                  sampleAverage bitmap (left + size - edgeInset - edgeThickness) (top + center - edgeThickness / 2) edgeThickness edgeThickness,
                                  sampleAverage bitmap (left + size + outsideOffset - edgeThickness) (top + center - edgeThickness / 2) edgeThickness edgeThickness ]

                    if List.isEmpty samples then
                        0.0
                    else
                        samples
                        |> List.averageBy (fun (insideColor, outsideColor) -> colorDistance insideColor outsideColor)

            max 0.0 (contrast + rowConsistency * 0.25 + edgeContrast * 0.35 - patternPenalty)

    let rememberBest candidates candidate =
        let sorted =
            candidate :: candidates
            |> List.sortByDescending (fun (_, score) -> score)

        if sorted.Length > refinedCandidateCount then
            sorted |> List.take refinedCandidateCount
        else
            sorted

    let scanRange includeEdges minimumBoardSize minimumSquareSize (bitmap: Bitmap) candidates leftStart leftEnd topStart topEnd sizeStart sizeEnd step =
        let maxSize = min bitmap.Width bitmap.Height
        let mutable best = candidates

        for size in max minimumBoardSize sizeStart .. step .. min maxSize sizeEnd do
            for top in max 0 topStart .. step .. min (bitmap.Height - size) topEnd do
                for left in max 0 leftStart .. step .. min (bitmap.Width - size) leftEnd do
                    let geometry = { Left = left; Top = top; Size = size }
                    let score = scoreCandidate includeEdges minimumSquareSize bitmap left top size
                    best <- rememberBest best (geometry, score)

        best

    let createCoarseBitmap (bitmap: Bitmap) =
        let maxDimension = max bitmap.Width bitmap.Height

        if maxDimension <= coarseMaxDimension then
            bitmap, 1.0, false
        else
            let scale = float coarseMaxDimension / float maxDimension
            let width = max 1 (int (round (float bitmap.Width * scale)))
            let height = max 1 (int (round (float bitmap.Height * scale)))
            let scaled = new Bitmap(width, height)

            use graphics = Graphics.FromImage scaled
            graphics.InterpolationMode <- Drawing2D.InterpolationMode.NearestNeighbor
            graphics.PixelOffsetMode <- Drawing2D.PixelOffsetMode.Half
            graphics.DrawImage(bitmap, 0, 0, width, height)

            scaled, scale, true

    let toOriginalGeometry scale (geometry: BoardGeometry) =
        {
            Left = int (round (float geometry.Left / scale))
            Top = int (round (float geometry.Top / scale))
            Size = int (round (float geometry.Size / scale))
        }

    interface IBoardDetector with
        member _.Detect(bitmap: Bitmap) =
            let maxSize = min bitmap.Width bitmap.Height

            if maxSize < minimumBoardSize then
                BoardNotFound
            else
                let coarseBitmap, coarseScale, disposeCoarseBitmap = createCoarseBitmap bitmap
                use disposableCoarseBitmap =
                    if disposeCoarseBitmap then
                        { new IDisposable with
                            member _.Dispose() = coarseBitmap.Dispose() }
                    else
                        { new IDisposable with
                            member _.Dispose() = () }

                let coarseMinimumBoardSize = max 80 (int (round (float minimumBoardSize * coarseScale)))
                let coarseMinimumSquareSize = max 8 (coarseMinimumBoardSize / 8 / 2)

                let coarseCandidates =
                    scanRange
                        false
                        coarseMinimumBoardSize
                        coarseMinimumSquareSize
                        coarseBitmap
                        []
                        0
                        (coarseBitmap.Width - coarseMinimumBoardSize)
                        0
                        (coarseBitmap.Height - coarseMinimumBoardSize)
                        coarseMinimumBoardSize
                        (min coarseBitmap.Width coarseBitmap.Height)
                        coarseCandidateStep
                    |> List.map (fun (geometry, score) -> toOriginalGeometry coarseScale geometry, score)

                let refinedCandidates =
                    coarseCandidates
                    |> List.fold
                        (fun candidates (geometry, _) ->
                            let margin = max 24 (int (round (float coarseCandidateStep / coarseScale)))
                            let sizeMargin = max 32 (int (round (float coarseSizeStep / coarseScale)))

                            scanRange
                                true
                                minimumBoardSize
                                24
                                bitmap
                                candidates
                                (geometry.Left - margin)
                                (geometry.Left + margin)
                                (geometry.Top - margin)
                                (geometry.Top + margin)
                                (geometry.Size - sizeMargin)
                                (geometry.Size + sizeMargin)
                                refineStep)
                        coarseCandidates

                let bestGeometry, bestScore =
                    refinedCandidates
                    |> List.tryHead
                    |> Option.defaultValue ({ Left = 0; Top = 0; Size = 0 }, 0.0)

                match Some bestGeometry with
                | Some geometry when bestScore >= 35.0 -> BoardDetected geometry
                | _ -> BoardNotFound

type FenBoardReader(fen: string) =
    interface IBoardReader with
        member _.Read(_, _) =
            match Fen.parseBoard fen with
            | Ok board -> Some { Board = board; Confidence = 1.0 }
            | Error _ -> None

type UncertainBoardReader() =
    interface IBoardReader with
        member _.Read(_, _) = None

type FixedBoardDetector(geometry: BoardGeometry) =
    interface IBoardDetector with
        member _.Detect(_) = BoardDetected geometry

module ScreenCapture =
    let captureVirtualScreen () =
        let bounds = System.Windows.Forms.SystemInformation.VirtualScreen
        let bitmap = new Bitmap(bounds.Width, bounds.Height)

        use graphics = Graphics.FromImage bitmap
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size)
        bitmap, bounds.Location
