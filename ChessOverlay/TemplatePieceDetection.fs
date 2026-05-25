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

    let private tryParsePiece (filename: string) =
        let name = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant()

        let parts =
            if name.Length = 2 then
                [ string name[0]; string name[1] ]
            else
                name.Split('_', StringSplitOptions.RemoveEmptyEntries)
                |> Array.toList

        match parts with
        | colorName :: kindName :: _ ->
            Option.map2
                (fun color kind -> { Color = color; Kind = kind })
                (Map.tryFind colorName colors)
                (Map.tryFind kindName kinds)
        | _ -> None

    let loadAllFromDirectory (path: string) : (Piece * Bitmap) array =
        if not (Directory.Exists path) then
            Array.empty
        else
            [| yield! Directory.GetFiles(path, "*.png")
               yield! Directory.GetFiles(path, "*.bmp") |]
            |> Array.choose (fun filePath ->
                tryParsePiece filePath
                |> Option.map (fun piece -> piece, new Bitmap(filePath)))

    let loadFromDirectory (path: string) : Map<Piece, Bitmap> =
        loadAllFromDirectory path |> Map.ofArray

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

    let private colorName piece =
        match piece.Color with
        | Black -> "black"
        | White -> "white"

    let private kindName piece =
        match piece.Kind with
        | King -> "king"
        | Queen -> "queen"
        | Rook -> "rook"
        | Bishop -> "bishop"
        | Knight -> "knight"
        | Pawn -> "pawn"

    let private templateFileName piece square =
        sprintf "%s_%s_%s.png" (colorName piece) (kindName piece) (Squares.name square)

    let saveStartingPositionTemplates (bitmap: Bitmap) (geometry: BoardGeometry) (path: string) =
        match Fen.parseBoard startingPosition with
        | Error _ -> 0
        | Ok board ->
            Directory.CreateDirectory path |> ignore

            board
            |> Map.toSeq
            |> Seq.fold
                (fun savedCount (square, piece) ->
                    match PieceBitmap.extractSquareBitmap bitmap geometry square with
                    | None -> savedCount
                    | Some template ->
                        use template = template
                        let filePath = Path.Combine(path, templateFileName piece square)
                        template.Save(filePath, ImageFormat.Png)
                        savedCount + 1)
                0

module BackgroundIsolation =
    // Every piece is drawn with a dark enclosing outline. A captured square is
    // the surrounding board colour (which may span two square shades when the
    // crop overlaps neighbours) up to that outline. Flood-filling inward from
    // the four edges through pixels lighter than the outline therefore stops at
    // the outline regardless of board colour, so everything the flood does not
    // reach is the piece. Marking the reached pixels transparent isolates the
    // piece from whatever square it happens to sit on.
    let private darkThreshold = 90.0

    let private luminance (r: float) (g: float) (b: float) = (r + g + b) / 3.0

    let hasTransparency (bitmap: Bitmap) : bool =
        if not (Image.IsAlphaPixelFormat bitmap.PixelFormat) then
            false
        else
            let bounds = Rectangle(0, 0, bitmap.Width, bitmap.Height)
            let data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)

            try
                let bytes = Array.zeroCreate<byte> (data.Stride * bitmap.Height)
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)

                let mutable found = false
                let mutable i = 3

                while not found && i < bytes.Length do
                    if bytes[i] < 250uy then found <- true
                    i <- i + 4

                found
            finally
                bitmap.UnlockBits(data)

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
                let b = float bytes[o]
                let gr = float bytes[o + 1]
                let r = float bytes[o + 2]
                luminance r gr b >= darkThreshold

            let visited = Array.zeroCreate<bool> (w * h)
            let queue = System.Collections.Generic.Queue<struct (int * int)>()

            let trySeed x y =
                let p = y * w + x
                if not visited[p] && isBackground x y then
                    visited[p] <- true
                    queue.Enqueue(struct (x, y))

            for x in 0 .. w - 1 do
                trySeed x 0
                trySeed x (h - 1)

            for y in 1 .. h - 2 do
                trySeed 0 y
                trySeed (w - 1) y

            let tryVisit x y =
                if x >= 0 && x < w && y >= 0 && y < h then
                    let p = y * w + x
                    if not visited[p] && isBackground x y then
                        visited[p] <- true
                        queue.Enqueue(struct (x, y))

            while queue.Count > 0 do
                let struct (x, y) = queue.Dequeue()
                tryVisit (x - 1) y
                tryVisit (x + 1) y
                tryVisit x (y - 1)
                tryVisit x (y + 1)

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

    // Down to 32x32 the discriminative outline is barely a pixel wide, so an
    // integer offset search can never align it sub-pixel and a one-pixel board
    // misalignment destroys the correlation. A light blur widens those features
    // so matching tolerates the imperfect framing of a hand-selected board.
    let private blurOnce (src: float[]) : float[] =
        let horizontal = Array.zeroCreate<float> (sampleSize * sampleSize)

        for y in 0 .. sampleSize - 1 do
            for x in 0 .. sampleSize - 1 do
                let c = src[y * sampleSize + x]
                let l = if x > 0 then src[y * sampleSize + x - 1] else c
                let r = if x < sampleSize - 1 then src[y * sampleSize + x + 1] else c
                horizontal[y * sampleSize + x] <- (l + 2.0 * c + r) / 4.0

        let result = Array.zeroCreate<float> (sampleSize * sampleSize)

        for y in 0 .. sampleSize - 1 do
            for x in 0 .. sampleSize - 1 do
                let c = horizontal[y * sampleSize + x]
                let u = if y > 0 then horizontal[(y - 1) * sampleSize + x] else c
                let d = if y < sampleSize - 1 then horizontal[(y + 1) * sampleSize + x] else c
                result[y * sampleSize + x] <- (u + 2.0 * c + d) / 4.0

        result

    let private blur (src: float[]) : float[] = blurOnce src

    // Blur restricted to piece pixels, so the transparent background never bleeds
    // into the template and matching stays independent of board colour.
    let private maskedBlur (src: float[]) (mask: bool[]) : float[] =
        let result = Array.copy src

        for y in 0 .. sampleSize - 1 do
            for x in 0 .. sampleSize - 1 do
                let i = y * sampleSize + x
                if mask[i] then
                    let mutable sum = 0.0
                    let mutable weight = 0.0

                    for dy in -1 .. 1 do
                        for dx in -1 .. 1 do
                            let nx = x + dx
                            let ny = y + dy
                            if nx >= 0 && nx < sampleSize && ny >= 0 && ny < sampleSize then
                                let ni = ny * sampleSize + nx
                                if mask[ni] then
                                    let w = if dx = 0 && dy = 0 then 4.0 elif dx = 0 || dy = 0 then 2.0 else 1.0
                                    sum <- sum + w * src[ni]
                                    weight <- weight + w

                    result[i] <- sum / weight

        result

    // Drop the one-pixel rim of the mask. Blur mixes those edge samples with the
    // surrounding board, so excluding them keeps the comparison on clean piece
    // interior and removes the last of the background's influence.
    let private erodeMask (mask: bool[]) : bool[] =
        let result = Array.zeroCreate<bool> (sampleSize * sampleSize)

        for y in 0 .. sampleSize - 1 do
            for x in 0 .. sampleSize - 1 do
                let i = y * sampleSize + x
                if mask[i] then
                    let inside =
                        x > 0 && x < sampleSize - 1 && y > 0 && y < sampleSize - 1
                        && mask[i - 1] && mask[i + 1]
                        && mask[i - sampleSize] && mask[i + sampleSize]
                    result[i] <- inside

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

    // A template is reduced to its grayscale samples plus a mask marking which
    // samples belong to the piece (vs the transparent/isolated background). The
    // mask lets matching ignore whatever colour the live square happens to be.
    let private minPieceFraction = 0.1

    let toGrayscaleAndMask (bitmap: Bitmap) : float[] * bool[] =
        let isolated =
            if BackgroundIsolation.hasTransparency bitmap then None
            else Some(BackgroundIsolation.isolate bitmap)

        let source = isolated |> Option.defaultValue bitmap

        let gray, rawMask =
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

        isolated |> Option.iter (fun b -> b.Dispose())

        // If isolation leaves almost nothing (e.g. a borderless or full-bleed
        // template), fall back to comparing the whole square.
        let pieceCount = rawMask |> Array.sumBy (fun m -> if m then 1 else 0)

        if float pieceCount < float (sampleSize * sampleSize) * minPieceFraction then
            blur gray, Array.create (sampleSize * sampleSize) true
        else
            maskedBlur gray rawMask, erodeMask rawMask

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

        if count = 0 then
            0.0
        else
            let mean = total / float count
            let variance = max 0.0 (totalSquared / float count - mean * mean)
            sqrt variance

    // Manual board selection is never pixel-perfect, so the cropped square is
    // often shifted a few pixels from the calibrated template. Compare the
    // template against the sample at a small range of integer offsets and keep
    // the best correlation, restoring alignment without re-cropping.
    let searchRadius = 3

    let private minComparedPixels = 16

    let private nccAtOffset (template: float[]) (mask: bool[]) (sample: float[]) (dx: int) (dy: int) =
        let xStart = max 0 (-dx)
        let xEnd = min sampleSize (sampleSize - dx)
        let yStart = max 0 (-dy)
        let yEnd = min sampleSize (sampleSize - dy)

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

        if count < minComparedPixels then
            0.0
        else
            let tmean = tsum / float count
            let smean = ssum / float count
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

type TemplateBoardReader(templates: seq<Piece * Bitmap>, threshold: float) =
    let preparedTemplates = SimilarityComparison.prepareTemplates templates
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

    new(templates: Map<Piece, Bitmap>, threshold: float) =
        TemplateBoardReader(templates |> Map.toSeq, threshold)

    interface IBoardReader with
        member _.Read(bitmap: Bitmap, geometry: BoardGeometry) =
            if Array.isEmpty preparedTemplates then
                None
            else
                let mutable board = Map.empty
                let mutable candidates = Map.empty
                let mutable matchCount = 0

                for square in Squares.all do
                    match PieceBitmap.extractSquareBitmap bitmap geometry square with
                    | None -> ()
                    | Some squareBmp ->
                        use squareBmp = squareBmp
                        let squareGray = SimilarityComparison.toGrayscaleArray squareBmp
                        let rankedMatches = SimilarityComparison.rankMatches preparedTemplates squareGray

                        let squareCandidates =
                            rankedMatches
                            |> List.truncate 3
                            |> List.map (fun (piece, score) -> { Piece = piece; Score = score })

                        candidates <- Map.add square squareCandidates candidates

                        match SimilarityComparison.tryAcceptBestCandidate threshold rankedMatches with
                        | Some(piece, score) ->
                            if SimilarityComparison.piecePresenceScore squareGray >= piecePresenceThreshold || score >= threshold then
                                board <- Map.add square piece board
                                matchCount <- matchCount + 1
                        | None -> ()

                let confidence =
                    if matchCount = 0 then 0.0
                    elif matchCount >= 4 then 1.0
                    else 0.5

                Some { Board = board; Confidence = confidence; Candidates = candidates }
