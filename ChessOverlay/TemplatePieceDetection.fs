namespace ChessOverlay

open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Drawing.Imaging
open System.IO
open System.Runtime.InteropServices

module PieceTemplates =
    let private colors =
        [|
            "white", White
            "black", Black
            "w", White
            "b", Black
        |]
        |> Map.ofArray

    let private kinds =
        Map.ofList [
            "king", King
            "queen", Queen
            "rook", Rook
            "bishop", Bishop
            "knight", Knight
            "pawn", Pawn
            "k", King
            "q", Queen
            "r", Rook
            "b", Bishop
            "n", Knight
            "p", Pawn
        ]

    let private tryParseCompactPieceName (name: string) =
        if name.Length = 2 then
            Some(string name[0], string name[1])
        else
            None

    let private tryParseDelimitedPieceName (name: string) =
        let parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries)

        if parts.Length >= 2 then
            Some(parts[0], parts[1])
        else
            None

    let private tryParsePieceName name =
        tryParseCompactPieceName name |> Option.orElseWith (fun () -> tryParseDelimitedPieceName name)

    let private tryParsePiece (filename: string) =
        let name = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant()

        tryParsePieceName name
        |> Option.bind (fun (colorName, kindName) ->
            Option.map2
                (fun color kind -> { Color = color; Kind = kind })
                (Map.tryFind colorName colors)
                (Map.tryFind kindName kinds))

    let private templateFiles path =
        [| yield! Directory.GetFiles(path, "*.png")
           yield! Directory.GetFiles(path, "*.bmp") |]

    let private loadTemplate filePath =
        tryParsePiece filePath
        |> Option.map (fun piece -> piece, new Bitmap(filePath))

    let loadAllFromDirectory (path: string) : (Piece * Bitmap) array =
        if not (Directory.Exists path) then
            Array.empty
        else
            templateFiles path |> Array.choose loadTemplate

    let loadFromDirectory (path: string) : Map<Piece, Bitmap> =
        loadAllFromDirectory path |> Map.ofArray

    // Field templates are unlabelled reference images of non-piece square markers
    // (move-hint / premove dots), so every image in the directory is loaded as-is.
    let loadFieldTemplates (path: string) : Bitmap array =
        if not (Directory.Exists path) then
            Array.empty
        else
            [| yield! Directory.GetFiles(path, "*.png")
               yield! Directory.GetFiles(path, "*.bmp") |]
            |> Array.map (fun filePath -> new Bitmap(filePath))

module PieceBitmap =
    let extractSquareBitmap (bitmap: Bitmap) (geometry: BoardGeometry) (square: Square) : Bitmap option =
        let rect = geometry.GetSquareRectangle square
        let x = int rect.X
        let y = int rect.Y
        let w = int rect.Width
        let h = int rect.Height
        let clampedX = max 0 x
        let clampedY = max 0 y
        let clampedW = min w (bitmap.Width - clampedX)
        let clampedH = min h (bitmap.Height - clampedY)

        if clampedW <= 0 || clampedH <= 0 then
            None
        else
            let crop = new Bitmap(clampedW, clampedH)

            use g = Graphics.FromImage(crop)
            g.DrawImage(bitmap, Rectangle(0, 0, clampedW, clampedH), Rectangle(clampedX, clampedY, clampedW, clampedH), GraphicsUnit.Pixel)
            Some crop

module PieceTemplateCalibration =
    let private startingPosition = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"

    let private startingBoard =
        Fen.parseBoard startingPosition |> Result.defaultValue Map.empty

    let private kindNames =
        Map.ofList [ King, "king"; Queen, "queen"; Rook, "rook"; Bishop, "bishop"; Knight, "knight"; Pawn, "pawn" ]

    let private kindName piece = Map.find piece.Kind kindNames

    let private templateFileName piece square =
        let color = match piece.Color with Black -> "black" | White -> "white"
        sprintf "%s_%s_%s.png" color (kindName piece) (Squares.name square)

    let private saveTemplate (bitmap: Bitmap) (geometry: BoardGeometry) (path: string) (square: Square) (piece: Piece) =
        match PieceBitmap.extractSquareBitmap bitmap geometry square with
        | None -> 0
        | Some template ->
            use template = template
            let filePath = Path.Combine(path, templateFileName piece square)
            template.Save(filePath, ImageFormat.Png)
            1

    let saveStartingPositionTemplates (bitmap: Bitmap) (geometry: BoardGeometry) (path: string) =
        Directory.CreateDirectory path |> ignore

        startingBoard
        |> Map.toSeq
        |> Seq.sumBy (fun (square, piece) -> saveTemplate bitmap geometry path square piece)

module BackgroundIsolation =
    // Every piece is drawn with a dark enclosing outline. A captured square is
    // the surrounding board colour (which may span two square shades when the
    // crop overlaps neighbours) up to that outline. Flood-filling inward from
    // the four edges through pixels lighter than the outline therefore stops at
    // the outline regardless of board colour, so everything the flood does not
    // reach is the piece. Marking the reached pixels transparent isolates the
    // piece from whatever square it happens to sit on.
    let private darkThreshold = 90.0

    let private scanForTransparentPixel (bytes: byte[]) =
        let mutable found = false
        let mutable i = 3

        while not found && i < bytes.Length do
            if bytes[i] < 250uy then found <- true
            i <- i + 4

        found

    let hasTransparency (bitmap: Bitmap) : bool =
        if not (Image.IsAlphaPixelFormat bitmap.PixelFormat) then
            false
        else
            let bounds = Rectangle(0, 0, bitmap.Width, bitmap.Height)
            let data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)

            try
                let bytes = Array.zeroCreate<byte> (data.Stride * bitmap.Height)
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)
                scanForTransparentPixel bytes
            finally
                bitmap.UnlockBits(data)

    let private enqueueIfBackground (x: int)
            (y: int)
            (w: int)
            (isBackground: int -> int -> bool)
            (visited: bool[])
            (queue: System.Collections.Generic.Queue<struct (int * int)>)
            =
        let p = y * w + x
        if not visited[p] && isBackground x y then
            visited[p] <- true
            queue.Enqueue(struct (x, y))

    let private enqueueNeighbours (w: int) (h: int) (tryVisit: int -> int -> unit) x y =
        tryVisit (x - 1) y
        tryVisit (x + 1) y
        tryVisit x (y - 1)
        tryVisit x (y + 1)

    let private tryVisitBackground (w: int) (h: int) (isBackground: int -> int -> bool) (visited: bool[]) (queue: System.Collections.Generic.Queue<struct (int * int)>) x y =
        if x >= 0 && x < w && y >= 0 && y < h then
            enqueueIfBackground x y w isBackground visited queue

    let private seedBorderQueue (w: int) (h: int) (enqueue: int -> int -> unit) =
        for x in 0 .. w - 1 do
            enqueue x 0
            enqueue x (h - 1)
        for y in 1 .. h - 2 do
            enqueue 0 y
            enqueue (w - 1) y

    let private floodFill (w: int)
            (h: int)
            (isBackground: int -> int -> bool)
            (visited: bool[])
            (queue: System.Collections.Generic.Queue<struct (int * int)>)
            =
        while queue.Count > 0 do
            let struct (x, y) = queue.Dequeue()
            enqueueNeighbours w h (tryVisitBackground w h isBackground visited queue) x y

    let isolate (bitmap: Bitmap) : Bitmap =
        let w = bitmap.Width
        let h = bitmap.Height
        let result = new Bitmap(w, h, PixelFormat.Format32bppArgb)

        (use g = Graphics.FromImage(result)
         g.DrawImage(bitmap, Rectangle(0, 0, w, h)))

        let bounds = Rectangle(0, 0, w, h)
        let data = result.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)

        try
            let stride = data.Stride
            let bytes = Array.zeroCreate<byte> (stride * h)
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)

            let inline offsetOf x y = y * stride + x * 4

            let isBackground x y =
                let o = offsetOf x y
                (float bytes[o + 2] + float bytes[o + 1] + float bytes[o]) / 3.0 >= darkThreshold

            let visited = Array.zeroCreate<bool> (w * h)
            let queue = System.Collections.Generic.Queue<struct (int * int)>()

            seedBorderQueue w h (fun x y -> enqueueIfBackground x y w isBackground visited queue)
            floodFill w h isBackground visited queue

            for y in 0 .. h - 1 do
                for x in 0 .. w - 1 do
                    if visited[y * w + x] then
                        bytes[offsetOf x y + 3] <- 0uy

            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length)
        finally
            result.UnlockBits(data)

        result

module SimilarityComparison =
    let private sampleSize = 32

    type private GrayscaleSource =
        {
            OriginX: int
            OriginY: int
            Width: int
            Height: int
            Stride: int
            Bytes: byte[]
        }

    // Down to 32x32 the discriminative outline is barely a pixel wide, so an
    // integer offset search can never align it sub-pixel and a one-pixel board
    // misalignment destroys the correlation. A light blur widens those features
    // so matching tolerates the imperfect framing of a hand-selected board.
    let private blurHorizontal (src: float[]) : float[] =
        let result = Array.zeroCreate<float> (sampleSize * sampleSize)

        for y in 0 .. sampleSize - 1 do
            for x in 0 .. sampleSize - 1 do
                let c = src[y * sampleSize + x]
                let l = if x > 0 then src[y * sampleSize + x - 1] else c
                let r = if x < sampleSize - 1 then src[y * sampleSize + x + 1] else c
                result[y * sampleSize + x] <- (l + 2.0 * c + r) / 4.0

        result

    let private blurVertical (src: float[]) : float[] =
        let result = Array.zeroCreate<float> (sampleSize * sampleSize)

        for y in 0 .. sampleSize - 1 do
            for x in 0 .. sampleSize - 1 do
                let c = src[y * sampleSize + x]
                let u = if y > 0 then src[(y - 1) * sampleSize + x] else c
                let d = if y < sampleSize - 1 then src[(y + 1) * sampleSize + x] else c
                result[y * sampleSize + x] <- (u + 2.0 * c + d) / 4.0

        result

    let private blurOnce (src: float[]) : float[] = src |> blurHorizontal |> blurVertical

    let private blur (src: float[]) : float[] = blurOnce src

    let private kernelWeight dx dy =
        if dx = 0 && dy = 0 then 4.0
        elif dx = 0 || dy = 0 then 2.0
        else 1.0

    let private isValidSampleIndex nx ny =
        nx >= 0 && nx < sampleSize && ny >= 0 && ny < sampleSize

    let private kernelBlurWeight (src: float[]) (mask: bool[]) x y =
        let mutable sum = 0.0
        let mutable weight = 0.0

        for dy in -1 .. 1 do
            for dx in -1 .. 1 do
                let nx = x + dx
                let ny = y + dy
                if isValidSampleIndex nx ny then
                    let ni = ny * sampleSize + nx
                    if mask[ni] then
                        let w = kernelWeight dx dy
                        sum <- sum + w * src[ni]
                        weight <- weight + w

        sum / weight

    let private maskedBlur (src: float[]) (mask: bool[]) : float[] =
        let result = Array.copy src

        for y in 0 .. sampleSize - 1 do
            for x in 0 .. sampleSize - 1 do
                let i = y * sampleSize + x
                if mask[i] then
                    result[i] <- kernelBlurWeight src mask x y

        result

    let private isInteriorCoord x y =
        x > 0 && x < sampleSize - 1 && y > 0 && y < sampleSize - 1

    let private hasAllFourNeighboursMasked (mask: bool[]) i =
        mask[i - 1] && mask[i + 1] && mask[i - sampleSize] && mask[i + sampleSize]

    // Drop the one-pixel rim of the mask. Blur mixes those edge samples with the
    // surrounding board, so excluding them keeps the comparison on clean piece
    // interior and removes the last of the background's influence.
    let private erodeMask (mask: bool[]) : bool[] =
        let result = Array.zeroCreate<bool> (sampleSize * sampleSize)

        for y in 0 .. sampleSize - 1 do
            for x in 0 .. sampleSize - 1 do
                let i = y * sampleSize + x
                if mask[i] then
                    result[i] <- isInteriorCoord x y && hasAllFourNeighboursMasked mask i

        result

    let toGrayscaleArray (bitmap: Bitmap) : float[] =
        use scaled = new Bitmap(sampleSize, sampleSize)
        use g = Graphics.FromImage(scaled)
        g.InterpolationMode <- InterpolationMode.Bilinear
        g.PixelOffsetMode <- PixelOffsetMode.HighQuality
        g.DrawImage(bitmap, 0, 0, sampleSize, sampleSize)

        let bounds = Rectangle(0, 0, sampleSize, sampleSize)
        let data = scaled.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb)

        try
            let stride = data.Stride
            let bytes = Array.zeroCreate<byte> (stride * sampleSize)
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)

            Array.init (sampleSize * sampleSize) (fun i ->
                let x = i % sampleSize
                let y = i / sampleSize
                let offset = y * stride + x * 3
                let b = float bytes[offset]
                let gr = float bytes[offset + 1]
                let r = float bytes[offset + 2]
                (r + gr + b) / 3.0)
            |> blur
        finally
            scaled.UnlockBits(data)

    let private grayscaleAt (source: GrayscaleSource) x y =
        let clampedX = min (source.OriginX + source.Width - 1) (max source.OriginX x)
        let clampedY = min (source.OriginY + source.Height - 1) (max source.OriginY y)
        let offset = (clampedY - source.OriginY) * source.Stride + (clampedX - source.OriginX) * 4
        let b = float source.Bytes[offset]
        let gr = float source.Bytes[offset + 1]
        let r = float source.Bytes[offset + 2]
        (r + gr + b) / 3.0

    let private bilinearGrayscaleAt source x y =
        let x0 = int (floor x)
        let y0 = int (floor y)
        let xWeight = x - float x0
        let yWeight = y - float y0

        let top =
            grayscaleAt source x0 y0 * (1.0 - xWeight)
            + grayscaleAt source (x0 + 1) y0 * xWeight

        let bottom =
            grayscaleAt source x0 (y0 + 1) * (1.0 - xWeight)
            + grayscaleAt source (x0 + 1) (y0 + 1) * xWeight

        top * (1.0 - yWeight) + bottom * yWeight

    let private readGrayscaleSource (bitmap: Bitmap) =
        let bounds = Rectangle(0, 0, bitmap.Width, bitmap.Height)
        let data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)

        try
            let stride = abs data.Stride
            let bytes = Array.zeroCreate<byte> (stride * bounds.Height)

            for row in 0 .. bounds.Height - 1 do
                Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), bytes, row * stride, stride)

            {
                OriginX = bounds.X
                OriginY = bounds.Y
                Width = bounds.Width
                Height = bounds.Height
                Stride = stride
                Bytes = bytes
            }
        finally
            bitmap.UnlockBits(data)

    let private squareSampleBounds (bitmap: Bitmap) (geometry: BoardGeometry) (square: Square) =
        let rect = geometry.GetSquareRectangle square
        let x = int rect.X
        let y = int rect.Y
        let w = int rect.Width
        let h = int rect.Height
        let clampedX = max 0 x
        let clampedY = max 0 y
        let clampedW = min w (bitmap.Width - clampedX)
        let clampedH = min h (bitmap.Height - clampedY)

        if clampedW <= 0 || clampedH <= 0 then
            None
        else
            Some(float clampedX, float clampedY, float clampedW, float clampedH)

    let private toGrayscaleArrayFromSource source bitmap geometry square =
        squareSampleBounds bitmap geometry square
        |> Option.map (fun (left, top, width, height) ->
            Array.init (sampleSize * sampleSize) (fun i ->
                let sampleX = i % sampleSize
                let sampleY = i / sampleSize
                let sourceX = left + (float sampleX + 0.5) * width / float sampleSize - 0.5
                let sourceY = top + (float sampleY + 0.5) * height / float sampleSize - 0.5
                bilinearGrayscaleAt source sourceX sourceY)
            |> blur)

    let readSquareGrayscaleSamples (bitmap: Bitmap) (geometry: BoardGeometry) =
        let source = readGrayscaleSource bitmap

        [ for square in Squares.all do
              match toGrayscaleArrayFromSource source bitmap geometry square with
              | Some gray -> square, gray
              | None -> () ]

    // A template is reduced to its grayscale samples plus a mask marking which
    // samples belong to the piece (vs the transparent/isolated background). The
    // mask lets matching ignore whatever colour the live square happens to be.
    let private minPieceFraction = 0.1
    let private minComparedPixels = 16

    let private readScaledGrayAndMask (source: Bitmap) =
        use scaled = new Bitmap(sampleSize, sampleSize, PixelFormat.Format32bppArgb)
        (use g = Graphics.FromImage(scaled)
         g.InterpolationMode <- InterpolationMode.Bilinear
         g.PixelOffsetMode <- PixelOffsetMode.HighQuality
         g.Clear(Color.Transparent)
         g.DrawImage(source, 0, 0, sampleSize, sampleSize))

        let bounds = Rectangle(0, 0, sampleSize, sampleSize)
        let data = scaled.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)

        try
            let stride = data.Stride
            let bytes = Array.zeroCreate<byte> (stride * sampleSize)
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)

            let gray = Array.zeroCreate<float> (sampleSize * sampleSize)
            let mask = Array.zeroCreate<bool> (sampleSize * sampleSize)

            for i in 0 .. sampleSize * sampleSize - 1 do
                let x = i % sampleSize
                let y = i / sampleSize
                let offset = y * stride + x * 4
                let b = float bytes[offset]
                let gr = float bytes[offset + 1]
                let r = float bytes[offset + 2]
                gray[i] <- (r + gr + b) / 3.0
                mask[i] <- bytes[offset + 3] > 127uy

            gray, mask
        finally
            scaled.UnlockBits(data)

    let private normalizePieceMask gray rawMask =
        let pieceCount = rawMask |> Array.sumBy (fun m -> if m then 1 else 0)

        if float pieceCount < float (sampleSize * sampleSize) * minPieceFraction then
            blur gray, Array.create (sampleSize * sampleSize) true
        else
            let erodedMask = erodeMask rawMask
            let erodedCount = erodedMask |> Array.sumBy (fun m -> if m then 1 else 0)

            if erodedCount < minComparedPixels then
                blur gray, rawMask
            else
                maskedBlur gray rawMask, erodedMask

    let toGrayscaleAndMask (bitmap: Bitmap) : float[] * bool[] =
        let isolated =
            if BackgroundIsolation.hasTransparency bitmap then None
            else Some(BackgroundIsolation.isolate bitmap)

        let source = isolated |> Option.defaultValue bitmap

        let gray, rawMask = readScaledGrayAndMask source

        isolated |> Option.iter (fun b -> b.Dispose())

        // If isolation leaves almost nothing (e.g. a borderless or full-bleed
        // template), fall back to comparing the whole square.
        normalizePieceMask gray rawMask

    let piecePresenceScore (sample: float[]) =
        let centerStart = sampleSize / 4
        let centerEnd = sampleSize - centerStart

        let mutable total = 0.0
        let mutable totalSquared = 0.0
        let mutable count = 0

        for y in centerStart .. centerEnd - 1 do
            for x in centerStart .. centerEnd - 1 do
                let value = sample[y * sampleSize + x]
                total <- total + value
                totalSquared <- totalSquared + value * value
                count <- count + 1

        let mean = total / float count
        let variance = max 0.0 (totalSquared / float count - mean * mean)
        sqrt variance

    // Correlation of the centre region against its own vertical mirror. A
    // move-hint / premove dot is a filled disk, symmetric about the square's
    // horizontal midline, so it scores ~1. Every real piece is bottom-heavy
    // (wider base than top) and never approaches that, so a near-perfect mirror
    // marks an empty highlighted square rather than a piece.
    let private verticalSymmetrySums (sample: float[]) centerStart centerEnd =
        let mutable topSum = 0.0
        let mutable bottomSum = 0.0
        let mutable count = 0

        for y in centerStart .. centerEnd - 1 do
            for x in centerStart .. centerEnd - 1 do
                topSum <- topSum + sample[y * sampleSize + x]
                bottomSum <- bottomSum + sample[(sampleSize - 1 - y) * sampleSize + x]
                count <- count + 1

        topSum, bottomSum, count

    let private verticalSymmetryNcc (sample: float[]) centerStart centerEnd topMean bottomMean =
        let mutable num = 0.0
        let mutable topSq = 0.0
        let mutable bottomSq = 0.0

        for y in centerStart .. centerEnd - 1 do
            for x in centerStart .. centerEnd - 1 do
                let t = sample[y * sampleSize + x] - topMean
                let b = sample[(sampleSize - 1 - y) * sampleSize + x] - bottomMean
                num <- num + t * b
                topSq <- topSq + t * t
                bottomSq <- bottomSq + b * b

        if topSq < 1.0 || bottomSq < 1.0 then 1.0 else num / sqrt (topSq * bottomSq)

    let verticalSymmetry (sample: float[]) =
        let centerStart = sampleSize / 4
        let centerEnd = sampleSize - centerStart
        let topSum, bottomSum, count = verticalSymmetrySums sample centerStart centerEnd

        let topMean = topSum / float count
        let bottomMean = bottomSum / float count
        verticalSymmetryNcc sample centerStart centerEnd topMean bottomMean

    // A move-hint / premove dot is a round disk, so among the pieces it can only
    // resemble a pawn (the one compact, rounded silhouette) and the matcher duly
    // ranks it as one. Real pawns are bottom-heavy and stay well under this
    // symmetry even with a few pixels of board misalignment, so a pawn-shaped
    // match this symmetric is an empty highlighted square. Restricting the test
    // to pawns leaves the rook — the most symmetric real piece — untouched.
    let private moveHintSymmetryThreshold = 0.95

    let looksLikeMoveHintDot (piece: Piece) (sample: float[]) =
        piece.Kind = Pawn && verticalSymmetry sample >= moveHintSymmetryThreshold

    // Manual board selection is never pixel-perfect, so the cropped square is
    // often shifted a few pixels from the calibrated template. Compare the
    // template against the sample at a small range of integer offsets and keep
    // the best correlation, restoring alignment without re-cropping.
    let searchRadius = 3

    let private nccSumPass (template: float[]) (mask: bool[]) (sample: float[]) xStart xEnd yStart yEnd dx dy =
        let mutable count = 0
        let mutable tsum = 0.0
        let mutable ssum = 0.0

        for y in yStart .. yEnd - 1 do
            for x in xStart .. xEnd - 1 do
                let ti = y * sampleSize + x

                if mask[ti] then
                    tsum <- tsum + template[ti]
                    ssum <- ssum + sample[(y + dy) * sampleSize + (x + dx)]
                    count <- count + 1

        count, tsum, ssum

    let private nccCorrelationPass (template: float[]) (mask: bool[]) (sample: float[]) xStart xEnd yStart yEnd dx dy tmean smean =
        let mutable num = 0.0
        let mutable tsq = 0.0
        let mutable ssq = 0.0

        for y in yStart .. yEnd - 1 do
            for x in xStart .. xEnd - 1 do
                let ti = y * sampleSize + x

                if mask[ti] then
                    let tv = template[ti] - tmean
                    let sv = sample[(y + dy) * sampleSize + (x + dx)] - smean
                    num <- num + tv * sv
                    tsq <- tsq + tv * tv
                    ssq <- ssq + sv * sv

        num, tsq, ssq

    let private nccAtOffset (template: float[]) (mask: bool[]) (sample: float[]) (dx: int) (dy: int) =
        let xStart = max 0 (-dx)
        let xEnd = min sampleSize (sampleSize - dx)
        let yStart = max 0 (-dy)
        let yEnd = min sampleSize (sampleSize - dy)
        let count, tsum, ssum = nccSumPass template mask sample xStart xEnd yStart yEnd dx dy

        let tmean = tsum / float count
        let smean = ssum / float count
        let num, tsq, ssq = nccCorrelationPass template mask sample xStart xEnd yStart yEnd dx dy tmean smean

        if tsq < 1.0 || ssq < 1.0 then
            0.0
        else
            let shape = num / sqrt (tsq * ssq)
            // Zero-mean correlation is invariant to contrast magnitude, so a
            // bright white piece and a dark black piece of the same shape
            // correlate equally well. Scaling by how closely their contrasts
            // match restores the light/dark distinction while staying
            // invariant to board colour and lighting.
            let tStd = sqrt (tsq / float count)
            let sStd = sqrt (ssq / float count)
            let contrastAgreement = min tStd sStd / max tStd sStd
            shape * contrastAgreement

    let private nccTolerant (template: float[]) (mask: bool[]) (sample: float[]) =
        let mutable best = -1.0

        for dy in -searchRadius .. searchRadius do
            for dx in -searchRadius .. searchRadius do
                let score = nccAtOffset template mask sample dx dy
                if score > best then best <- score

        best

    let prepareTemplates (bitmaps: seq<Piece * Bitmap>) : (Piece * float[] * bool[]) array =
        bitmaps
        |> Seq.map (fun (piece, bmp) ->
            let gray, mask = toGrayscaleAndMask bmp
            piece, gray, mask)
        |> Seq.toArray

    let rankMatches (templates: (Piece * float[] * bool[]) array) (squareGray: float[]) : (Piece * float) list =
        templates
        |> Array.map (fun (piece, templateGray, mask) -> piece, nccTolerant templateGray mask squareGray)
        |> Array.sortByDescending snd
        |> Array.toList

    // Field templates are non-piece reference images (move-hint / premove dots).
    // They carry no piece label: they exist only to out-score the pieces on the
    // squares that are actually empty highlights, so the reader can tell a dot
    // apart from a piece by letting both compete on the same correlation.
    //
    // Unlike a piece, a dot is a solid disk with no internal texture, so the
    // background-isolating, texture-matching path used for pieces collapses to
    // zero variance on it. What identifies a dot is its shape against the square,
    // so it is matched as the whole square (full mask) instead.
    let prepareFieldTemplates (bitmaps: seq<Bitmap>) : (float[] * bool[]) array =
        bitmaps
        |> Seq.map (fun bmp -> toGrayscaleArray bmp, Array.create (sampleSize * sampleSize) true)
        |> Seq.toArray

    let bestMatchScore (templates: (float[] * bool[]) array) (squareGray: float[]) : float =
        templates
        |> Array.fold (fun best (templateGray, mask) -> max best (nccTolerant templateGray mask squareGray)) -1.0

    let private acceptanceThreshold threshold piece =
        match piece.Kind with
        | Pawn -> threshold * 0.85
        | _ -> threshold

    let tryAcceptBestMatch (threshold: float) (matches: (Piece * float) list) : Piece option =
        matches
        |> List.tryFind (fun (piece, score) -> score >= acceptanceThreshold threshold piece)
        |> Option.map fst

    let tryAcceptBestCandidate (threshold: float) (matches: (Piece * float) list) : (Piece * float) option =
        matches
        |> List.tryFind (fun (piece, score) -> score >= acceptanceThreshold threshold piece)

    let findBestMatch (templates: (Piece * float[] * bool[]) array) (squareBitmap: Bitmap) (threshold: float) : Piece option =
        squareBitmap
        |> toGrayscaleArray
        |> rankMatches templates
        |> tryAcceptBestMatch threshold

type TemplateBoardReader(templates: seq<Piece * Bitmap>, fieldTemplates: seq<Bitmap>, threshold: float) =
    let preparedTemplates = SimilarityComparison.prepareTemplates templates
    let preparedFieldTemplates = SimilarityComparison.prepareFieldTemplates fieldTemplates
    let piecePresenceThreshold =
        preparedTemplates
        |> Array.map (fun (_, gray, _) -> SimilarityComparison.piecePresenceScore gray)
        |> Array.filter (fun score -> score > 0.0)
        |> fun scores ->
            if Array.isEmpty scores then
                Double.PositiveInfinity
            else
                Array.sortInPlace scores
                Array.min scores * 0.35

    new(templates: seq<Piece * Bitmap>, threshold: float) =
        TemplateBoardReader(templates, Seq.empty, threshold)

    new(templates: Map<Piece, Bitmap>, threshold: float) =
        TemplateBoardReader(templates |> Map.toSeq, Seq.empty, threshold)

    interface IBoardReader with
        member _.Read(bitmap: Bitmap, geometry: BoardGeometry) =
            if Array.isEmpty preparedTemplates then
                None
            else
                let mutable board = Map.empty
                let mutable candidates = Map.empty
                let mutable matchCount = 0

                for square, squareGray in SimilarityComparison.readSquareGrayscaleSamples bitmap geometry do
                    let presenceScore = SimilarityComparison.piecePresenceScore squareGray
                    let rankedMatches = SimilarityComparison.rankMatches preparedTemplates squareGray

                    let squareCandidates =
                        rankedMatches
                        |> List.truncate 3
                        |> List.map (fun (piece, score) -> { Piece = piece; Score = score })

                    candidates <- Map.add square squareCandidates candidates

                    // Let the field (move-hint dot) templates compete with the
                    // pieces: when a dot reference matches this square better
                    // than any piece, the square is an empty highlight, not a
                    // piece, so leave it unoccupied.
                    let bestPieceScore = rankedMatches |> List.head |> snd

                    let isField =
                        SimilarityComparison.bestMatchScore preparedFieldTemplates squareGray >= bestPieceScore

                    match SimilarityComparison.tryAcceptBestCandidate threshold rankedMatches with
                    | Some(piece, score) when not isField ->
                        let presentEnough =
                            presenceScore >= piecePresenceThreshold || score >= threshold

                        if presentEnough && not (SimilarityComparison.looksLikeMoveHintDot piece squareGray) then
                            board <- Map.add square piece board
                            matchCount <- matchCount + 1
                    | _ -> ()

                let confidence =
                    if matchCount = 0 then 0.0
                    elif matchCount >= 4 then 1.0
                    else 0.5

                Some { Board = board; Confidence = confidence; Candidates = candidates; Strategy = "Template" }
