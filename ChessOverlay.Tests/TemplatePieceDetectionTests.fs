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

    let private disposeTemplates (templates: Map<Piece, Bitmap>) =
        templates
        |> Map.iter (fun _ bitmap -> bitmap.Dispose())

    let private disposeAllTemplates (templates: (Piece * Bitmap) array) =
        templates
        |> Array.iter (fun (_, bitmap) -> bitmap.Dispose())

    let private fixturePath fileName =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName)

    let private pieceNotation piece =
        let letter =
            match piece.Kind with
            | King -> "K"
            | Queen -> "Q"
            | Rook -> "R"
            | Bishop -> "B"
            | Knight -> "N"
            | Pawn -> "P"

        if piece.Color = Black then letter.ToLowerInvariant() else letter

    let private candidateSummary (reading: BoardReading) (expected: BoardState) =
        expected
        |> Map.toSeq
        |> Seq.filter (fun (square, piece) -> BoardState.tryPieceAt square reading.Board <> Some piece)
        |> Seq.map (fun (square, expectedPiece) ->
            let candidates =
                reading.Candidates
                |> Map.tryFind square
                |> Option.defaultValue []
                |> List.truncate 3
                |> List.map (fun candidate -> sprintf "%s %.3f" (pieceNotation candidate.Piece) candidate.Score)
                |> String.concat ", "

            sprintf "%s expected=%s candidates=[%s]" (Squares.name square) (pieceNotation expectedPiece) candidates)
        |> String.concat "; "

    // The real isolated piece artwork (transparent background) is composited
    // onto synthetic boards so detection is exercised against genuine piece
    // silhouettes with their dark outline, not stand-in glyphs.
    let private loadPieceImages () : Map<Piece, Bitmap> =
        PieceTemplates.loadFromDirectory (fixturePath "templates")

    let private fillSquares (graphics: Graphics) size (highlights: Set<Square>) =
        let squareSize = size / 8
        use lightBrush = new SolidBrush(Color.FromArgb(235, 236, 208))
        use darkBrush = new SolidBrush(Color.FromArgb(119, 149, 86))
        use highlightBrush = new SolidBrush(Color.FromArgb(150, 215, 170, 120))

        for rank in 0 .. 7 do
            for file in 0 .. 7 do
                let brush = if (rank + file) % 2 = 0 then lightBrush else darkBrush
                graphics.FillRectangle(brush, file * squareSize, rank * squareSize, squareSize, squareSize)

        for square in highlights do
            graphics.FillRectangle(highlightBrush, square.File * squareSize, square.Rank * squareSize, squareSize, squareSize)

    let private drawBoardFromFen (fen: string) size (highlights: Set<Square>) =
        let bitmap = new Bitmap(size, size)
        let squareSize = size / 8
        use graphics = Graphics.FromImage(bitmap)
        fillSquares graphics size highlights
        let pieceImages = loadPieceImages ()

        try
            match Fen.parseBoard fen with
            | Error message -> failwith message
            | Ok board ->
                for KeyValue(square, piece) in board do
                    match Map.tryFind piece pieceImages with
                    | Some image ->
                        graphics.DrawImage(image, Rectangle(square.File * squareSize, square.Rank * squareSize, squareSize, squareSize))
                    | None -> failwithf "Missing piece image for %A" piece
        finally
            disposeTemplates pieceImages

        bitmap

    let private drawStartingPositionBoard size =
        drawBoardFromFen "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR" size (Set.singleton { File = 4; Rank = 4 })

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
            Assert.True(templates.ContainsKey { Color = White; Kind = King })
            Assert.True(templates.ContainsKey { Color = Black; Kind = Queen })
        finally
            disposeTemplates templates

    [<Fact>]
    let ``Template reader matches a single real piece square`` () =
        let pieceImages = loadPieceImages ()
        let piece = { Color = White; Kind = King }

        try
            // White king alone on a1.
            use boardBitmap = drawBoardFromFen "8/8/8/8/8/8/8/K7" 256 Set.empty
            let reader = TemplateBoardReader(Map.ofList [ piece, pieceImages[piece] ], 0.35) :> IBoardReader
            let geometry = { Left = 0; Top = 0; Size = 256 }

            match reader.Read(boardBitmap, geometry) with
            | Some reading ->
                Assert.Equal(0.5, reading.Confidence)
                Assert.Equal(Some piece, BoardState.tryPieceAt { File = 0; Rank = 7 } reading.Board)
                let candidates = Map.find { File = 0; Rank = 7 } reading.Candidates
                Assert.Equal(piece, candidates.Head.Piece)
                Assert.True(candidates.Head.Score >= 0.4)
            | None -> failwith "Expected template reader output."
        finally
            disposeTemplates pieceImages

    [<Fact>]
    let ``Detection ignores the square background colour`` () =
        let pieceImages = loadPieceImages ()
        let piece = { Color = White; Kind = Knight }

        try
            let knight = pieceImages[piece]
            let reader = TemplateBoardReader(Map.ofList [ piece, knight ], 0.35) :> IBoardReader
            let squarePixels = 96
            // The geometry treats the whole bitmap as a single square.
            let geometry = { Left = 0; Top = 0; Size = squarePixels * 8 }

            // The template was isolated from a green square; the piece must still
            // be recognised on backgrounds that differ wildly from that.
            let backgrounds =
                [ Color.FromArgb(119, 149, 86) // original green dark square
                  Color.FromArgb(235, 236, 208) // light square
                  Color.FromArgb(181, 136, 99) // brown theme
                  Color.FromArgb(200, 40, 40) // nothing like a board at all
                  Color.FromArgb(40, 40, 200) ]

            for background in backgrounds do
                use square = new Bitmap(squarePixels, squarePixels)

                (use graphics = Graphics.FromImage(square)
                 graphics.Clear background
                 graphics.DrawImage(knight, Rectangle(0, 0, squarePixels, squarePixels)))

                match reader.Read(square, geometry) with
                | Some reading ->
                    Assert.Equal(Some piece, BoardState.tryPieceAt { File = 0; Rank = 0 } reading.Board)
                | None -> failwithf "Expected detection on background %A" background
        finally
            disposeTemplates pieceImages

    [<Fact>]
    let ``Template matcher accepts lower-scoring pawns before other pieces`` () =
        let pawn = { Color = White; Kind = Pawn }
        let knight = { Color = White; Kind = Knight }

        Assert.Equal(Some pawn, SimilarityComparison.tryAcceptBestMatch 0.9 [ pawn, 0.78 ])
        Assert.True(SimilarityComparison.tryAcceptBestMatch 0.9 [ knight, 0.78 ] |> Option.isNone)

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
            Assert.All(templates, fun (piece, _) -> Assert.Equal({ Color = White; Kind = Pawn }, piece))
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
    let ``Template reader detects all pieces from freshly calibrated templates`` () =
        // Calibrate templates from a real screenshot and detect that same board:
        // exercises the calibrate-then-read flow end to end on genuine artwork.
        let root = tempRoot ()
        use bitmap = new Bitmap(fixturePath "chess_screenshot_starting position.png")
        let geometry = { Left = 0; Top = 0; Size = bitmap.Width }
        let savedCount = PieceTemplateCalibration.saveStartingPositionTemplates bitmap geometry root
        let templates = PieceTemplates.loadAllFromDirectory root

        try
            let reader = TemplateBoardReader(templates, 0.35) :> IBoardReader

            match reader.Read(bitmap, geometry), Fen.parseBoard "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR" with
            | Some reading, Ok expected ->
                Assert.Equal(32, savedCount)
                Assert.Equal(32, templates.Length)
                Assert.Equal(1.0, reading.Confidence)
                Assert.True((expected = reading.Board), candidateSummary reading expected)
            | None, _ -> failwith "Expected template reader output."
            | _, Error message -> failwith message
        finally
            disposeAllTemplates templates

    [<Fact>]
    let ``Template reader detects all pieces on selected board screenshot with saved templates`` () =
        use bitmap = new Bitmap(fixturePath "chess_screenshot_starting position.png")
        let geometry = { Left = 0; Top = 0; Size = bitmap.Width }
        let templates = PieceTemplates.loadAllFromDirectory(fixturePath "templates")

        try
            let reader = TemplateBoardReader(templates, 0.35) :> IBoardReader

            match reader.Read(bitmap, geometry), Fen.parseBoard "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR" with
            | Some reading, Ok expected ->
                Assert.Equal(32, templates.Length)
                Assert.Equal(1.0, reading.Confidence)
                Assert.True((expected = reading.Board), candidateSummary reading expected)
            | None, _ -> failwith "Expected template reader output."
            | _, Error message -> failwith message
        finally
            disposeAllTemplates templates

    [<Fact>]
    let ``Template reader detects all pieces on alternate starting board screenshot`` () =
        use bitmap = new Bitmap(fixturePath "chess_board_start2.png")
        let geometry = { Left = 0; Top = 0; Size = bitmap.Width }
        let templates = PieceTemplates.loadAllFromDirectory(fixturePath "templates")

        try
            let reader = TemplateBoardReader(templates, 0.35) :> IBoardReader

            match reader.Read(bitmap, geometry), Fen.parseBoard "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR" with
            | Some reading, Ok expected ->
                Assert.Equal(32, templates.Length)
                Assert.Equal(1.0, reading.Confidence)
                Assert.True((expected = reading.Board), candidateSummary reading expected)
            | None, _ -> failwith "Expected template reader output."
            | _, Error message -> failwith message
        finally
            disposeAllTemplates templates

    [<Fact>]
    let ``Template reader tolerates a few pixels of board misalignment`` () =
        // The committed templates were calibrated from this exact screenshot, so
        // reading it back with the geometry shifted a few pixels mimics imperfect
        // manual board selection.
        use bitmap = new Bitmap(fixturePath "chess_screenshot_starting position.png")
        let templates = PieceTemplates.loadAllFromDirectory(fixturePath "templates")

        try
            let reader = TemplateBoardReader(templates, 0.35) :> IBoardReader
            let misalignedGeometry = { Left = 3; Top = 3; Size = bitmap.Width }

            match reader.Read(bitmap, misalignedGeometry), Fen.parseBoard "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR" with
            | Some reading, Ok expected ->
                Assert.Equal(1.0, reading.Confidence)
                Assert.True((expected = reading.Board), candidateSummary reading expected)
            | None, _ -> failwith "Expected template reader output."
            | _, Error message -> failwith message
        finally
            disposeAllTemplates templates

    [<Fact>]
    let ``Template reader leaves empty squares unoccupied`` () =
        let root = tempRoot ()
        use startingBitmap = drawStartingPositionBoard 800
        use emptyBitmap = drawEmptyBoard 800
        let geometry = { Left = 0; Top = 0; Size = 800 }
        PieceTemplateCalibration.saveStartingPositionTemplates startingBitmap geometry root |> ignore
        let templates = PieceTemplates.loadAllFromDirectory root

        try
            let reader = TemplateBoardReader(templates, 0.35) :> IBoardReader

            match reader.Read(emptyBitmap, geometry) with
            | Some reading ->
                Assert.Empty(reading.Board)
                Assert.Equal(0.0, reading.Confidence)
            | None -> failwith "Expected template reader output."
        finally
            disposeAllTemplates templates
