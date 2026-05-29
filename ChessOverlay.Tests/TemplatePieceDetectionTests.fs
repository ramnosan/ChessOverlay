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
        saveBitmap (Path.Combine(root, "unknown.bmp")) Color.Red

        let templates = PieceTemplates.loadFromDirectory root

        try
            Assert.Equal(2, templates.Count)
            Assert.True(templates.ContainsKey { Color = White; Kind = King })
            Assert.True(templates.ContainsKey { Color = Black; Kind = Queen })
        finally
            disposeTemplates templates

    [<Fact>]
    let ``Template loader reports invalid image files`` () =
        let root = tempRoot ()
        File.WriteAllText(Path.Combine(root, "white_king.png"), "not an image")

        Assert.ThrowsAny<System.Exception>(fun () -> PieceTemplates.loadAllFromDirectory root |> ignore) |> ignore

    [<Fact>]
    let ``extractSquareBitmap returns none when square is outside bitmap`` () =
        use bitmap = new Bitmap(10, 10)

        Assert.True(PieceBitmap.extractSquareBitmap bitmap { Left = 100; Top = 100; Size = 80 } { File = 0; Rank = 0 } |> Option.isNone)

    [<Fact>]
    let ``hasTransparency returns false for non-alpha bitmaps`` () =
        use bitmap = new Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format24bppRgb)

        Assert.False(BackgroundIsolation.hasTransparency bitmap)

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
    let ``findBestMatch identifies a rendered piece against prepared templates`` () =
        let pieceImages = loadPieceImages ()
        let piece = { Color = White; Kind = Knight }

        try
            let prepared = SimilarityComparison.prepareTemplates (pieceImages |> Map.toSeq)
            let squarePixels = 96
            use square = new Bitmap(squarePixels, squarePixels)

            (use graphics = Graphics.FromImage(square)
             graphics.Clear(Color.FromArgb(119, 149, 86))
             graphics.DrawImage(pieceImages[piece], Rectangle(0, 0, squarePixels, squarePixels)))

            Assert.Equal(Some piece, SimilarityComparison.findBestMatch prepared square 0.35)
        finally
            disposeTemplates pieceImages

    [<Fact>]
    let ``findBestMatch rejects an empty square`` () =
        let pieceImages = loadPieceImages ()

        try
            let prepared = SimilarityComparison.prepareTemplates (pieceImages |> Map.toSeq)
            use square = new Bitmap(96, 96)

            (use graphics = Graphics.FromImage(square)
             graphics.Clear(Color.FromArgb(119, 149, 86)))

            Assert.True((SimilarityComparison.findBestMatch prepared square 0.35).IsNone)
        finally
            disposeTemplates pieceImages

    [<Fact>]
    let ``Thin transparent templates keep their raw mask when erosion removes the piece`` () =
        use bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb)

        for y in 0 .. 15 do
            for x in 0 .. 15 do
                if (x + y) % 2 = 0 then
                    bitmap.SetPixel(x, y, Color.FromArgb(255, 20, 20, 20))

        let _, mask = SimilarityComparison.toGrayscaleAndMask bitmap

        Assert.True(mask |> Array.exists id)

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
    let ``Template calibration skips squares outside the bitmap`` () =
        let root = tempRoot ()
        use bitmap = new Bitmap(10, 10)

        let savedCount =
            PieceTemplateCalibration.saveStartingPositionTemplates bitmap { Left = 100; Top = 100; Size = 80 } root

        Assert.Equal(0, savedCount)

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

    // Draw a Lichess-style move-hint dot (a filled circle using 65% of the square's background
    // brightness, the same visual weight shown in the bug-report screenshot).
    let private drawMoveHintDot (g: Graphics) (squareSize: int) (file: int) (rank: int) (board: Bitmap) =
        let squareX = file * squareSize
        let squareY = rank * squareSize
        let bg = board.GetPixel(squareX + squareSize / 2, squareY + squareSize / 2)
        let dot = Color.FromArgb(int (float bg.R * 0.65), int (float bg.G * 0.65), int (float bg.B * 0.65))
        use brush = new SolidBrush(dot)
        let margin = squareSize / 4
        g.FillEllipse(brush, squareX + margin, squareY + margin, squareSize - margin * 2, squareSize - margin * 2)

    [<Fact>]
    let ``Diagnostic dot square scores`` () =
        // Use the committed fixture templates (chess.com style) to check NCC/presence scores.
        let templates = PieceTemplates.loadAllFromDirectory(fixturePath "templates")

        try
            let prepared = SimilarityComparison.prepareTemplates (templates |> Array.toSeq)
            let size = 800
            let squareSize = size / 8

            // Chess.com board colours matching what the fixture templates were calibrated from.
            use bgBitmap = new Bitmap(size, size)
            (use g = Graphics.FromImage(bgBitmap)
             fillSquares g size Set.empty)

            // Draw a prominent circle on a dark square (file 0, rank 4 = a4).
            use testBitmap = new Bitmap(size, size)
            (use g = Graphics.FromImage(testBitmap)
             g.DrawImage(bgBitmap, 0, 0)
             drawMoveHintDot g squareSize 0 4 bgBitmap)

            let geometry = { Left = 0; Top = 0; Size = size }
            let darkSquare = { File = 0; Rank = 4 }

            match PieceBitmap.extractSquareBitmap testBitmap geometry darkSquare with
            | None -> failwith "Could not extract square"
            | Some squareBmp ->
                use squareBmp = squareBmp
                let gray = SimilarityComparison.toGrayscaleArray squareBmp
                let presence = SimilarityComparison.piecePresenceScore gray
                let matches = SimilarityComparison.rankMatches prepared gray |> List.truncate 5

                let minPresenceScore =
                    prepared
                    |> Array.map (fun (_, g, _) -> SimilarityComparison.piecePresenceScore g)
                    |> Array.filter (fun s -> s > 0.0)
                    |> Array.min

                let topMatchInfo =
                    matches
                    |> List.map (fun (p, s) -> sprintf "%A=%.3f" p.Kind s)
                    |> String.concat " "

                Assert.True(
                    presence >= minPresenceScore * 0.35,
                    sprintf "presence=%.2f minTemplate=%.2f threshold(0.35)=%.2f threshold(0.55)=%.2f | %s"
                        presence minPresenceScore (minPresenceScore * 0.35) (minPresenceScore * 0.55)
                        topMatchInfo)
        finally
            disposeAllTemplates templates

    [<Fact>]
    let ``Template reader does not detect pawn on dark empty square with move-hint dot`` () =
        // Regression: dark squares with Lichess move-hint dots were falsely detected as pawns.
        let root = tempRoot ()
        use srcBitmap = new Bitmap(fixturePath "chess_board_start2.png")
        let size = srcBitmap.Width
        let squareSize = size / 8
        let geometry = { Left = 0; Top = 0; Size = size }
        PieceTemplateCalibration.saveStartingPositionTemplates srcBitmap geometry root |> ignore
        let templates = PieceTemplates.loadAllFromDirectory root

        try
            let reader = TemplateBoardReader(templates, 0.35) :> IBoardReader

            // Draw a move-hint dot on a known empty dark square (rank 4, file 0 → a5, dark square).
            use testBitmap = new Bitmap(size, size)
            (use g = Graphics.FromImage(testBitmap)
             g.DrawImage(srcBitmap, 0, 0)
             drawMoveHintDot g squareSize 0 4 srcBitmap)

            let dotSquare = { File = 0; Rank = 4 }

            match reader.Read(testBitmap, geometry) with
            | Some reading ->
                Assert.True(
                    BoardState.tryPieceAt dotSquare reading.Board |> Option.isNone,
                    sprintf "%s has a move-hint dot but was detected as %A"
                        (Squares.name dotSquare)
                        (BoardState.tryPieceAt dotSquare reading.Board))
            | None -> failwith "Expected template reader output."
        finally
            disposeAllTemplates templates

    // Crop the two stacked squares of the premove fixture into field-template
    // reference bitmaps (top = dark-square dot, bottom = light-square dot).
    let private loadFieldTemplatesFromFixture () : Bitmap array =
        let bitmap = new Bitmap(fixturePath "two_premove_fields_that should not be classified as pawns.png")
        try
            let squareSize = bitmap.Height / 2
            let geometry = { Left = 0; Top = 0; Size = squareSize * 8 }
            [| 0; 1 |]
            |> Array.choose (fun rank -> PieceBitmap.extractSquareBitmap bitmap geometry { File = 0; Rank = rank })
        finally
            bitmap.Dispose()

    // Read the premove fixture (two vertically stacked highlighted squares) and
    // return any of the two squares that came back occupied. A board of eight
    // half-height squares makes the reader crop exactly those two and clamp the
    // remaining off-image squares to nothing.
    let private misreadPremoveSquares (reader: IBoardReader) =
        use bitmap = new Bitmap(fixturePath "two_premove_fields_that should not be classified as pawns.png")
        let squareSize = bitmap.Height / 2
        let geometry = { Left = 0; Top = 0; Size = squareSize * 8 }

        match reader.Read(bitmap, geometry) with
        | Some reading ->
            [ 0; 1 ]
            |> List.choose (fun rank ->
                let square = { File = 0; Rank = rank }
                BoardState.tryPieceAt square reading.Board
                |> Option.map (fun piece -> sprintf "%s=%A" (Squares.name square) piece))
        | None -> failwith "Expected template reader output."

    [<Fact>]
    let ``Template reader does not classify premove highlight squares as pawns`` () =
        // Regression from a real capture: two empty squares carrying chess.com
        // premove highlight dots (one dark, one light) were falsely read as pawns.
        // With no field templates this exercises the symmetry fallback.
        let templates = PieceTemplates.loadAllFromDirectory(fixturePath "templates")

        try
            let reader = TemplateBoardReader(templates, 0.35) :> IBoardReader
            let misread = misreadPremoveSquares reader

            Assert.True(
                List.isEmpty misread,
                sprintf "Premove highlight squares were detected as pieces: %s" (String.concat ", " misread))
        finally
            disposeAllTemplates templates

    [<Fact>]
    let ``Field templates classify premove dots as empty squares`` () =
        // The "field" class competes with the pieces: a move-hint dot reference
        // out-scores every piece on a dot square, so the square stays empty.
        let templates = PieceTemplates.loadAllFromDirectory(fixturePath "templates")
        let fields = loadFieldTemplatesFromFixture ()

        try
            let reader = TemplateBoardReader(templates, fields, 0.35) :> IBoardReader
            let misread = misreadPremoveSquares reader

            Assert.True(
                List.isEmpty misread,
                sprintf "Premove dots beat the field templates and were read as pieces: %s" (String.concat ", " misread))
        finally
            disposeAllTemplates templates
            fields |> Array.iter (fun b -> b.Dispose())

    [<Fact>]
    let ``Field templates leave a full starting position detected`` () =
        // The field class must only win on empty highlights: with field templates
        // loaded, every real piece on a normal board must still be detected.
        use bitmap = new Bitmap(fixturePath "chess_screenshot_starting position.png")
        let geometry = { Left = 0; Top = 0; Size = bitmap.Width }
        let templates = PieceTemplates.loadAllFromDirectory(fixturePath "templates")
        let fields = loadFieldTemplatesFromFixture ()

        try
            let reader = TemplateBoardReader(templates, fields, 0.35) :> IBoardReader

            match reader.Read(bitmap, geometry), Fen.parseBoard "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR" with
            | Some reading, Ok expected ->
                Assert.Equal(1.0, reading.Confidence)
                Assert.True((expected = reading.Board), candidateSummary reading expected)
            | None, _ -> failwith "Expected template reader output."
            | _, Error message -> failwith message
        finally
            disposeAllTemplates templates
            fields |> Array.iter (fun b -> b.Dispose())

    [<Fact>]
    let ``Template reader does not detect pawn on light empty square with move-hint dot`` () =
        // Regression: light squares with Lichess move-hint dots were falsely detected as pawns.
        let root = tempRoot ()
        use srcBitmap = new Bitmap(fixturePath "chess_board_start2.png")
        let size = srcBitmap.Width
        let squareSize = size / 8
        let geometry = { Left = 0; Top = 0; Size = size }
        PieceTemplateCalibration.saveStartingPositionTemplates srcBitmap geometry root |> ignore
        let templates = PieceTemplates.loadAllFromDirectory root

        try
            let reader = TemplateBoardReader(templates, 0.35) :> IBoardReader

            // Draw a move-hint dot on a known empty light square (rank 4, file 1 → b5, light square).
            use testBitmap = new Bitmap(size, size)
            (use g = Graphics.FromImage(testBitmap)
             g.DrawImage(srcBitmap, 0, 0)
             drawMoveHintDot g squareSize 1 4 srcBitmap)

            let dotSquare = { File = 1; Rank = 4 }

            match reader.Read(testBitmap, geometry) with
            | Some reading ->
                Assert.True(
                    BoardState.tryPieceAt dotSquare reading.Board |> Option.isNone,
                    sprintf "%s has a move-hint dot but was detected as %A"
                        (Squares.name dotSquare)
                        (BoardState.tryPieceAt dotSquare reading.Board))
            | None -> failwith "Expected template reader output."
        finally
            disposeAllTemplates templates

    [<Fact>]
    let ``loadFieldTemplates returns empty when directory does not exist`` () =
        let missing = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N"))
        Assert.Empty(PieceTemplates.loadFieldTemplates missing)

    [<Fact>]
    let ``loadFieldTemplates returns empty when directory has no image files`` () =
        let root = tempRoot ()
        Assert.Empty(PieceTemplates.loadFieldTemplates root)

    [<Fact>]
    let ``loadFieldTemplates loads PNG files`` () =
        let root = tempRoot ()
        saveBitmap (Path.Combine(root, "field.png")) Color.Green
        let result = PieceTemplates.loadFieldTemplates root
        try
            Assert.Equal(1, result.Length)
        finally
            result |> Array.iter (fun b -> b.Dispose())

    [<Fact>]
    let ``loadFieldTemplates loads BMP files`` () =
        let root = tempRoot ()
        saveBitmap (Path.Combine(root, "field.bmp")) Color.Blue
        let result = PieceTemplates.loadFieldTemplates root
        try
            Assert.Equal(1, result.Length)
        finally
            result |> Array.iter (fun b -> b.Dispose())

    [<Fact>]
    let ``loadFieldTemplates loads both PNG and BMP files`` () =
        let root = tempRoot ()
        saveBitmap (Path.Combine(root, "field1.png")) Color.Green
        saveBitmap (Path.Combine(root, "field2.bmp")) Color.Blue
        let result = PieceTemplates.loadFieldTemplates root
        try
            Assert.Equal(2, result.Length)
        finally
            result |> Array.iter (fun b -> b.Dispose())
