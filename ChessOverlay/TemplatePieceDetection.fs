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
            "white", Bottom
            "black", Top
            "w", Bottom
            "b", Top
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
        | Top -> "black"
        | Bottom -> "white"

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

module SimilarityComparison =
    let private sampleSize = 32

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
        finally
            scaled.UnlockBits(data)

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

    let private ncc (template: float[]) (sample: float[]) =
        let n = template.Length
        let mutable tmean = 0.0
        let mutable smean = 0.0

        for i in 0 .. n - 1 do
            tmean <- tmean + template[i]
            smean <- smean + sample[i]

        tmean <- tmean / float n
        smean <- smean / float n

        let mutable num = 0.0
        let mutable tsq = 0.0
        let mutable ssq = 0.0

        for i in 0 .. n - 1 do
            let tv = template[i] - tmean
            let sv = sample[i] - smean
            num <- num + tv * sv
            tsq <- tsq + tv * tv
            ssq <- ssq + sv * sv

        if tsq < 1.0 || ssq < 1.0 then 0.0
        else num / sqrt (tsq * ssq)

    let prepareTemplates (bitmaps: seq<Piece * Bitmap>) : (Piece * float[]) array =
        bitmaps
        |> Seq.map (fun (piece, bmp) -> piece, toGrayscaleArray bmp)
        |> Seq.toArray

    let rankMatches (templates: (Piece * float[]) array) (squareGray: float[]) : (Piece * float) list =
        templates
        |> Array.map (fun (piece, templateGray) -> piece, ncc templateGray squareGray)
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

    let findBestMatch (templates: (Piece * float[]) array) (squareBitmap: Bitmap) (threshold: float) : Piece option =
        squareBitmap
        |> toGrayscaleArray
        |> rankMatches templates
        |> tryAcceptBestMatch threshold

type TemplateBoardReader(templates: seq<Piece * Bitmap>, threshold: float) =
    let preparedTemplates = SimilarityComparison.prepareTemplates templates
    let piecePresenceThreshold =
        preparedTemplates
        |> Array.map (snd >> SimilarityComparison.piecePresenceScore)
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
