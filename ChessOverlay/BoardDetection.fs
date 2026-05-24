namespace ChessOverlay

open System
open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.Drawing.Imaging
open System.Runtime.InteropServices

type IBoardDetector =
    abstract Detect: Bitmap -> DetectionResult

type IBoardReader =
    abstract Read: Bitmap * BoardGeometry -> BoardReading option

type private LockedBitmap(bitmap: Bitmap) =
    let bounds = Rectangle(0, 0, bitmap.Width, bitmap.Height)
    let data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, bitmap.PixelFormat)
    let bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8

    member _.Width = bitmap.Width
    member _.Height = bitmap.Height

    member _.GetPixel(x: int, y: int) =
        let boundedX = min (bitmap.Width - 1) (max 0 x)
        let boundedY = min (bitmap.Height - 1) (max 0 y)
        let offset =
            IntPtr(data.Scan0.ToInt64() + int64 (boundedY * data.Stride + boundedX * bytesPerPixel))

        if bytesPerPixel >= 3 then
            let b = Marshal.ReadByte(offset, 0)
            let g = Marshal.ReadByte(offset, 1)
            let r = Marshal.ReadByte(offset, 2)
            Color.FromArgb(int r, int g, int b)
        else
            bitmap.GetPixel(boundedX, boundedY)

    interface IDisposable with
        member _.Dispose() = bitmap.UnlockBits(data)

type ConservativeBoardDetector() =
    let minimumBoardSize = 160
    let coarseMaxDimension = 420
    let coarseCandidateStep = 20
    let coarseSizeStep = 20
    let refineStep = 8
    let cachedRefineStep = 4
    let refinedCandidateCount = 4
    let detectionThreshold = 28.0
    let validationThreshold = 24.0
    let maximumCachedMisses = 3
    let mutable lastGeometry: BoardGeometry option = None
    let mutable cachedMisses = 0

    let colorDistance (left: Color) (right: Color) =
        let dr = int left.R - int right.R
        let dg = int left.G - int right.G
        let db = int left.B - int right.B
        sqrt (float (dr * dr + dg * dg + db * db))

    let clamp minimum maximum value =
        min maximum (max minimum value)

    let sampleAverage (pixels: LockedBitmap) x y width height =
        let mutable r = 0L
        let mutable g = 0L
        let mutable b = 0L
        let mutable count = 0L
        let boundedX = clamp 0 (pixels.Width - 1) x
        let boundedY = clamp 0 (pixels.Height - 1) y
        let boundedWidth = max 1 (min width (pixels.Width - boundedX))
        let boundedHeight = max 1 (min height (pixels.Height - boundedY))
        let samplePoints =
            [ 1, 1
              3, 1
              1, 3
              3, 3 ]

        for xWeight, yWeight in samplePoints do
            let sampleX = boundedX + boundedWidth * xWeight / 4
            let sampleY = boundedY + boundedHeight * yWeight / 4
            let pixel = pixels.GetPixel(sampleX, sampleY)
            r <- r + int64 pixel.R
            g <- g + int64 pixel.G
            b <- b + int64 pixel.B
            count <- count + 1L

        if count = 0L then
            Color.Black
        else
            Color.FromArgb(int (r / count), int (g / count), int (b / count))

    let scoreCandidate includeEdges minimumSquareSize (pixels: LockedBitmap) left top size =
        let squareSize = size / 8

        if squareSize < minimumSquareSize then
            0.0
        else
            let inset = max 2 (squareSize / 5)

            let colorGrid =
                Array2D.init 8 8 (fun rank file ->
                    sampleAverage
                        pixels
                        (left + file * squareSize + inset)
                        (top + rank * squareSize + inset)
                        (squareSize - inset * 2)
                        (squareSize - inset * 2))

            let colors =
                [ for rank in 0 .. 7 do
                      for file in 0 .. 7 do
                          file, rank, colorGrid[rank, file] ]

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

            let colorAt file rank =
                colorGrid[rank, file]

            let patternPenalty =
                colors
                |> List.averageBy (fun (file, rank, color) ->
                    let expected =
                        if (file + rank) % 2 = 0 then
                            lightAverage
                        else
                            darkAverage

                    colorDistance color expected)

            let adjacentContrast =
                [ for rank in 0 .. 7 do
                      for file in 0 .. 6 do
                          colorDistance (colorAt file rank) (colorAt (file + 1) rank)

                  for rank in 0 .. 6 do
                      for file in 0 .. 7 do
                          colorDistance (colorAt file rank) (colorAt file (rank + 1)) ]
                |> List.average

            let sameColorConsistency =
                [ for rank in 0 .. 7 do
                      for file in 0 .. 5 do
                          colorDistance (colorAt file rank) (colorAt (file + 2) rank)

                  for rank in 0 .. 5 do
                      for file in 0 .. 7 do
                          colorDistance (colorAt file rank) (colorAt file (rank + 2))

                  for rank in 0 .. 6 do
                      for file in 0 .. 6 do
                          colorDistance (colorAt file rank) (colorAt (file + 1) (rank + 1)) ]
                |> List.average

            let alternatingStrength = max 0.0 (adjacentContrast - sameColorConsistency * 0.65)

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
                                  sampleAverage pixels (left + center - edgeThickness / 2) (top + edgeInset) edgeThickness edgeThickness,
                                  sampleAverage pixels (left + center - edgeThickness / 2) (top - outsideOffset) edgeThickness edgeThickness

                              if top + size + outsideOffset < pixels.Height then
                                  sampleAverage pixels (left + center - edgeThickness / 2) (top + size - edgeInset - edgeThickness) edgeThickness edgeThickness,
                                  sampleAverage pixels (left + center - edgeThickness / 2) (top + size + outsideOffset - edgeThickness) edgeThickness edgeThickness

                              if left - outsideOffset >= 0 then
                                  sampleAverage pixels (left + edgeInset) (top + center - edgeThickness / 2) edgeThickness edgeThickness,
                                  sampleAverage pixels (left - outsideOffset) (top + center - edgeThickness / 2) edgeThickness edgeThickness

                              if left + size + outsideOffset < pixels.Width then
                                  sampleAverage pixels (left + size - edgeInset - edgeThickness) (top + center - edgeThickness / 2) edgeThickness edgeThickness,
                                  sampleAverage pixels (left + size + outsideOffset - edgeThickness) (top + center - edgeThickness / 2) edgeThickness edgeThickness ]

                    if List.isEmpty samples then
                        0.0
                    else
                        samples
                        |> List.averageBy (fun (insideColor, outsideColor) -> colorDistance insideColor outsideColor)

            max
                0.0
                (contrast * 0.65
                 + alternatingStrength * 0.85
                 + rowConsistency * 0.15
                 + edgeContrast * 0.25
                 - patternPenalty * 0.55)

    let rememberBest candidates candidate =
        let sorted =
            candidate :: candidates
            |> List.sortByDescending (fun (_, score) -> score)

        if sorted.Length > refinedCandidateCount then
            sorted |> List.take refinedCandidateCount
        else
            sorted

    let scanRange includeEdges minimumBoardSize minimumSquareSize (pixels: LockedBitmap) candidates leftStart leftEnd topStart topEnd sizeStart sizeEnd step =
        let maxSize = min pixels.Width pixels.Height
        let mutable best = candidates
        let actualSizeStart = max minimumBoardSize sizeStart
        let actualSizeEnd = min maxSize sizeEnd

        if actualSizeStart <= actualSizeEnd then
            for size in actualSizeStart .. step .. actualSizeEnd do
                let actualTopStart = max 0 topStart
                let actualTopEnd = min (pixels.Height - size) topEnd

                if actualTopStart <= actualTopEnd then
                    for top in actualTopStart .. step .. actualTopEnd do
                        let actualLeftStart = max 0 leftStart
                        let actualLeftEnd = min (pixels.Width - size) leftEnd

                        if actualLeftStart <= actualLeftEnd then
                            for left in actualLeftStart .. step .. actualLeftEnd do
                                let geometry = { Left = left; Top = top; Size = size }
                                let score = scoreCandidate includeEdges minimumSquareSize pixels left top size
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

    let detectGlobally (bitmap: Bitmap) =
        let coarseBitmap, coarseScale, disposeCoarseBitmap = createCoarseBitmap bitmap
        use disposableCoarseBitmap =
            if disposeCoarseBitmap then
                { new IDisposable with
                    member _.Dispose() = coarseBitmap.Dispose() }
            else
                { new IDisposable with
                    member _.Dispose() = () }

        use coarsePixels = new LockedBitmap(coarseBitmap)
        use pixels = new LockedBitmap(bitmap)
        let coarseMinimumBoardSize = max 80 (int (round (float minimumBoardSize * coarseScale)))
        let coarseMinimumSquareSize = max 8 (coarseMinimumBoardSize / 8 / 2)

        let coarseCandidates =
            scanRange
                false
                coarseMinimumBoardSize
                coarseMinimumSquareSize
                coarsePixels
                []
                0
                (coarsePixels.Width - coarseMinimumBoardSize)
                0
                (coarsePixels.Height - coarseMinimumBoardSize)
                coarseMinimumBoardSize
                (min coarsePixels.Width coarsePixels.Height)
                coarseCandidateStep
            |> List.map (fun (geometry, score) -> toOriginalGeometry coarseScale geometry, score)

        coarseCandidates
        |> List.fold
            (fun candidates (geometry, _) ->
                let margin = max 24 (int (round (float coarseCandidateStep / coarseScale)))
                let sizeMargin = max 32 (int (round (float coarseSizeStep / coarseScale)))

                scanRange
                    true
                    minimumBoardSize
                    24
                    pixels
                    candidates
                    (geometry.Left - margin)
                    (geometry.Left + margin)
                    (geometry.Top - margin)
                    (geometry.Top + margin)
                    (geometry.Size - sizeMargin)
                    (geometry.Size + sizeMargin)
                    refineStep)
            coarseCandidates

    let detectNearCachedGeometry (pixels: LockedBitmap) geometry =
        let margin = max 12 (geometry.Size / 32)
        let sizeMargin = max 12 (geometry.Size / 48)

        scanRange
            true
            minimumBoardSize
            24
            pixels
            []
            (geometry.Left - margin)
            (geometry.Left + margin)
            (geometry.Top - margin)
            (geometry.Top + margin)
            (geometry.Size - sizeMargin)
            (geometry.Size + sizeMargin)
            cachedRefineStep

    let bestCandidate candidates =
        candidates
        |> List.tryHead
        |> Option.defaultValue ({ Left = 0; Top = 0; Size = 0 }, 0.0)

    let rememberHit geometry =
        lastGeometry <- Some geometry
        cachedMisses <- 0
        BoardDetected geometry

    let rememberMiss () =
        cachedMisses <- cachedMisses + 1

        if cachedMisses >= maximumCachedMisses then
            lastGeometry <- None

        BoardNotFound

    member _.CachedGeometry = lastGeometry

    member _.ResetCache() =
        lastGeometry <- None
        cachedMisses <- 0

    member this.Detect(bitmap: Bitmap) =
        (this :> IBoardDetector).Detect(bitmap)

    interface IBoardDetector with
        member _.Detect(bitmap: Bitmap) =
            let maxSize = min bitmap.Width bitmap.Height

            if maxSize < minimumBoardSize then
                rememberMiss ()
            else
                match lastGeometry with
                | Some geometry ->
                    let cachedDetection =
                        use pixels = new LockedBitmap(bitmap)
                        let cachedGeometry, cachedScore = detectNearCachedGeometry pixels geometry |> bestCandidate

                        if cachedScore >= validationThreshold then
                            Some cachedGeometry
                        else
                            None

                    match cachedDetection with
                    | Some cachedGeometry -> rememberHit cachedGeometry
                    | None ->
                        let globalGeometry, globalScore = detectGlobally bitmap |> bestCandidate

                        if globalScore >= detectionThreshold then
                            rememberHit globalGeometry
                        else
                            rememberMiss ()
                | None ->
                    let globalGeometry, globalScore = detectGlobally bitmap |> bestCandidate

                    if globalScore >= detectionThreshold then
                        rememberHit globalGeometry
                    else
                        rememberMiss ()

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
    member _.Geometry = geometry

    interface IBoardDetector with
        member _.Detect(_) = BoardDetected geometry

[<ExcludeFromCodeCoverage>]
module ScreenCapture =
    let captureVirtualScreen () =
        let bounds = System.Windows.Forms.SystemInformation.VirtualScreen
        let bitmap = new Bitmap(bounds.Width, bounds.Height)

        use graphics = Graphics.FromImage bitmap
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size)
        bitmap, bounds.Location
