namespace ChessOverlay.Tests

open System
open System.Drawing
open System.IO
open Xunit
open ChessOverlay
open ChessOverlay.Tests

module TemplatePieceDetectionTests =
    let private tempRoot () =
        TestHelpers.tempRoot "ChessOverlayTemplateTests"

    let private saveBitmap path color =
        use bitmap = new Bitmap(6, 6)

        use graphics = Graphics.FromImage(bitmap)
        graphics.Clear color
        bitmap.Save(path)

    let private patternedSquare size =
        let bitmap = new Bitmap(size, size)

        for x in 0 .. size - 1 do
            for y in 0 .. size - 1 do
                let color =
                    if x = y || x + y = size - 1 then
                        Color.White
                    else
                        Color.Black

                bitmap.SetPixel(x, y, color)

        bitmap

    let private disposeTemplates (templates: Map<Piece, Bitmap>) =
        templates
        |> Map.iter (fun _ bitmap -> bitmap.Dispose())

    let private disposeAllTemplates (templates: (Piece * Bitmap) array) =
        templates
        |> Array.iter (fun (_, bitmap) -> bitmap.Dispose())

    let private pieceNotation piece =
        let letter =
            match piece.Kind with
            | King -> "K"
            | Queen -> "Q"
            | Rook -> "R"
            | Bishop -> "B"
            | Knight -> "N"
            | Pawn -> "P"

        if piece.Color = Top then letter.ToLowerInvariant() else letter

    let private drawStartingPositionBoard size =
        let bitmap = new Bitmap(size, size)
        let squareSize = size / 8
        use graphics = Graphics.FromImage(bitmap)
        use lightBrush = new SolidBrush(Color.FromArgb(235, 236, 208))
        use darkBrush = new SolidBrush(Color.FromArgb(119, 149, 86))
        use blackBrush = new SolidBrush(Color.FromArgb(75, 75, 75))
        use whiteBrush = new SolidBrush(Color.White)
        use highlightBrush = new SolidBrush(Color.FromArgb(150, 215, 170, 120))
        use pieceFont = new Font(FontFamily.GenericSansSerif, single squareSize * 0.58f, FontStyle.Bold, GraphicsUnit.Pixel)

        for rank in 0 .. 7 do
            for file in 0 .. 7 do
                let brush =
                    if (rank + file) % 2 = 0 then
                        lightBrush
                    else
                        darkBrush

                graphics.FillRectangle(brush, file * squareSize, rank * squareSize, squareSize, squareSize)

        graphics.FillRectangle(highlightBrush, 4 * squareSize, 4 * squareSize, squareSize, squareSize)

        match Fen.parseBoard "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR" with
        | Error message -> failwith message
        | Ok board ->
            for KeyValue(square, piece) in board do
                let text = pieceNotation piece
                let textSize = graphics.MeasureString(text, pieceFont)
                let x = single (square.File * squareSize) + (single squareSize - textSize.Width) / 2.0f
                let y = single (square.Rank * squareSize) + (single squareSize - textSize.Height) / 2.0f
                let brush = if piece.Color = Top then blackBrush else whiteBrush
                graphics.DrawString(text, pieceFont, brush, x, y)

        bitmap

    let private drawEmptyBoard size =
        let bitmap = new Bitmap(size, size)
        let squareSize = size / 8
        use graphics = Graphics.FromImage(bitmap)
        use lightBrush = new SolidBrush(Color.FromArgb(235, 236, 208))
        use darkBrush = new SolidBrush(Color.FromArgb(119, 149, 86))
        use highlightBrush = new SolidBrush(Color.FromArgb(150, 215, 170, 120))

        for rank in 0 .. 7 do
            for file in 0 .. 7 do
                let brush =
                    if (rank + file) % 2 = 0 then
                        lightBrush
                    else
                        darkBrush

                graphics.FillRectangle(brush, file * squareSize, rank * squareSize, squareSize, squareSize)

        graphics.FillRectangle(highlightBrush, 4 * squareSize, 4 * squareSize, squareSize, squareSize)

        bitmap

    [<Fact>]
    let ``Template loader parses short and long piece names`` () =
        let root = tempRoot ()

        saveBitmap (Path.Combine(root, "wk.png")) Color.White
        saveBitmap (Path.Combine(root, "black_queen.bmp")) Color.Black
        saveBitmap (Path.Combine(root, "unknown.png")) Color.Red

        let templates = PieceTemplates.loadFromDirectory root

        try
            Assert.Equal(2, templates.Count)
            Assert.True(templates.ContainsKey { Color = Bottom; Kind = King })
            Assert.True(templates.ContainsKey { Color = Top; Kind = Queen })
        finally
            disposeTemplates templates

    [<Fact>]
    let ``Template reader matches a patterned piece square`` () =
        use templateBitmap = patternedSquare 16
        use boardBitmap = new Bitmap(128, 128)

        use graphics = Graphics.FromImage(boardBitmap)
        graphics.Clear Color.Black
        graphics.DrawImage(templateBitmap, Rectangle(0, 0, 16, 16))

        let piece = { Color = Bottom; Kind = King }
        let templates = Map.ofList [ piece, templateBitmap ]
        let reader = TemplateBoardReader(templates, 0.9) :> IBoardReader
        let geometry = { Left = 0; Top = 0; Size = 128 }

        match reader.Read(boardBitmap, geometry) with
        | Some reading ->
            Assert.Equal(0.5, reading.Confidence)
            Assert.Equal(Some piece, BoardState.tryPieceAt { File = 0; Rank = 0 } reading.Board)
        | None -> failwith "Expected template reader output."

    [<Fact>]
    let ``Template reader reports no board when no templates are configured`` () =
        use bitmap = new Bitmap(20, 20)
        let reader = TemplateBoardReader(Map.empty, 0.9) :> IBoardReader

        let result =
            reader.Read(bitmap, { Left = 0; Top = 0; Size = 20 })

        Assert.True(result.IsNone)

    [<Fact>]
    let ``Template loader accepts square-specific calibrated names`` () =
        let root = tempRoot ()

        saveBitmap (Path.Combine(root, "white_pawn_a2.png")) Color.White
        saveBitmap (Path.Combine(root, "white_pawn_b2.png")) Color.White

        let templates = PieceTemplates.loadAllFromDirectory root

        try
            Assert.Equal(2, templates.Length)
            Assert.All(templates, fun (piece, _) -> Assert.Equal({ Color = Bottom; Kind = Pawn }, piece))
        finally
            disposeAllTemplates templates

    [<Fact>]
    let ``Template calibration saves every starting-position piece sample`` () =
        let root = tempRoot ()
        use bitmap = new Bitmap(80, 80)
        use graphics = Graphics.FromImage(bitmap)
        graphics.Clear Color.Black

        let savedCount =
            PieceTemplateCalibration.saveStartingPositionTemplates bitmap { Left = 0; Top = 0; Size = 80 } root

        let templates = PieceTemplates.loadFromDirectory root

        try
            Assert.Equal(32, savedCount)
            Assert.Equal(12, templates.Count)
        finally
            disposeTemplates templates

    [<Fact>]
    let ``Template reader detects all pieces on a starting-position board`` () =
        let root = tempRoot ()
        use bitmap = drawStartingPositionBoard 800
        let geometry = { Left = 0; Top = 0; Size = 800 }
        let savedCount = PieceTemplateCalibration.saveStartingPositionTemplates bitmap geometry root
        let templates = PieceTemplates.loadAllFromDirectory root

        try
            let reader = TemplateBoardReader(templates, 0.99) :> IBoardReader

            match reader.Read(bitmap, geometry), Fen.parseBoard "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR" with
            | Some reading, Ok expected ->
                Assert.Equal(32, savedCount)
                Assert.Equal(32, templates.Length)
                Assert.Equal(1.0, reading.Confidence)
                Assert.Equal<BoardState>(expected, reading.Board)
            | None, _ -> failwith "Expected template reader output."
            | _, Error message -> failwith message
        finally
            disposeAllTemplates templates

    let ``Template reader leaves empty squares unoccupied`` () =
        let root = tempRoot ()
        use startingBitmap = drawStartingPositionBoard 800
        use emptyBitmap = drawEmptyBoard 800
        let geometry = { Left = 0; Top = 0; Size = 800 }
        PieceTemplateCalibration.saveStartingPositionTemplates startingBitmap geometry root |> ignore
        let templates = PieceTemplates.loadAllFromDirectory root

        try
            let reader = TemplateBoardReader(templates, 0.75) :> IBoardReader

            match reader.Read(emptyBitmap, geometry) with
            | Some reading ->
                Assert.Empty(reading.Board)
                Assert.Equal(0.0, reading.Confidence)
            | None -> failwith "Expected template reader output."
        finally
            disposeAllTemplates templates
