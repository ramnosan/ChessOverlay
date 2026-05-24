namespace ChessOverlay

open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Drawing.Imaging
open System.IO
open System.Runtime.InteropServices

module PieceTemplates =
    let private tryParsePiece (filename: string) =
        let name = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant()

        let shortFormat () =
            if name.Length = 2 then
                let color =
                    match name[0] with
                    | 'w' -> Some Bottom
                    | 'b' -> Some Top
                    | _ -> None

                let kind =
                    match name[1] with
                    | 'k' -> Some King
                    | 'q' -> Some Queen
                    | 'r' -> Some Rook
                    | 'b' -> Some Bishop
                    | 'n' -> Some Knight
                    | 'p' -> Some Pawn
                    | _ -> None

                Option.map2 (fun c k -> { Color = c; Kind = k }) color kind
            else
                None

        let longFormat () =
            let color =
                if name.StartsWith("white_") then Some Bottom
                elif name.StartsWith("black_") then Some Top
                else None

            let rest =
                name.Replace("white_", "").Replace("black_", "")

            let kind =
                match rest with
                | "king" -> Some King
                | "queen" -> Some Queen
                | "rook" -> Some Rook
                | "bishop" -> Some Bishop
                | "knight" -> Some Knight
                | "pawn" -> Some Pawn
                | _ -> None

            Option.map2 (fun c k -> { Color = c; Kind = k }) color kind

        match shortFormat () with
        | Some piece -> Some piece
        | None -> longFormat ()

    let loadFromDirectory (path: string) : Map<Piece, Bitmap> =
        if not (Directory.Exists path) then
            Map.empty
        else
            [| yield! Directory.GetFiles(path, "*.png")
               yield! Directory.GetFiles(path, "*.bmp") |]
            |> Array.choose (fun filePath ->
                tryParsePiece filePath
                |> Option.map (fun piece -> piece, new Bitmap(filePath)))
            |> Map.ofArray

module SimilarityComparison =
    let private sampleSize = 32

    let private toGrayscaleArray (bitmap: Bitmap) : float[] =
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

    let prepareTemplates (bitmaps: Map<Piece, Bitmap>) : Map<Piece, float[]> =
        bitmaps |> Map.map (fun _ bmp -> toGrayscaleArray bmp)

    let findBestMatch (templates: Map<Piece, float[]>) (squareBitmap: Bitmap) (threshold: float) : Piece option =
        let squareGray = toGrayscaleArray squareBitmap

        templates
        |> Map.toSeq
        |> Seq.map (fun (piece, templateGray) -> piece, ncc templateGray squareGray)
        |> Seq.filter (fun (_, score) -> score >= threshold)
        |> Seq.sortByDescending snd
        |> Seq.tryHead
        |> Option.map fst

type TemplateBoardReader(templates: Map<Piece, Bitmap>, threshold: float) =
    let preparedTemplates = SimilarityComparison.prepareTemplates templates

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

    interface IBoardReader with
        member _.Read(bitmap: Bitmap, geometry: BoardGeometry) =
            if preparedTemplates.IsEmpty then
                None
            else
                let mutable board = Map.empty
                let mutable matchCount = 0

                for square in Squares.all do
                    match extractSquareBitmap bitmap geometry square with
                    | None -> ()
                    | Some squareBmp ->
                        use squareBmp = squareBmp

                        match SimilarityComparison.findBestMatch preparedTemplates squareBmp threshold with
                        | Some piece ->
                            board <- Map.add square piece board
                            matchCount <- matchCount + 1
                        | None -> ()

                let confidence =
                    if matchCount = 0 then 0.0
                    elif matchCount >= 4 then 1.0
                    else 0.5

                Some { Board = board; Confidence = confidence }
