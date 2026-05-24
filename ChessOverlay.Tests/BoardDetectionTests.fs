namespace ChessOverlay.Tests

open System
open System.Drawing
open Xunit
open ChessOverlay

module BoardDetectionTests =
    let private drawSyntheticBoard (bitmap: Bitmap) left top size =
        use graphics = Graphics.FromImage bitmap
        graphics.Clear(Color.FromArgb(32, 34, 38))

        let squareSize = size / 8
        use lightBrush = new SolidBrush(Color.FromArgb(238, 238, 210))
        use darkBrush = new SolidBrush(Color.FromArgb(118, 150, 86))

        for rank in 0 .. 7 do
            for file in 0 .. 7 do
                let brush =
                    if (file + rank) % 2 = 0 then
                        lightBrush
                    else
                        darkBrush

                graphics.FillRectangle(
                    brush,
                    left + file * squareSize,
                    top + rank * squareSize,
                    squareSize,
                    squareSize)

    let private drawChessComLikeScreenshot (bitmap: Bitmap) left top size =
        use graphics = Graphics.FromImage bitmap
        graphics.Clear(Color.FromArgb(48, 46, 43))

        use sideBrush = new SolidBrush(Color.FromArgb(36, 36, 34))
        use panelBrush = new SolidBrush(Color.FromArgb(28, 27, 25))
        use darkPanelBrush = new SolidBrush(Color.FromArgb(32, 31, 29))
        use buttonBrush = new SolidBrush(Color.FromArgb(119, 185, 73))
        let scaled value total = int (round (float value * float total))
        graphics.FillRectangle(sideBrush, 0, scaled 0.061 bitmap.Height, scaled 0.066 bitmap.Width, bitmap.Height - scaled 0.122 bitmap.Height)
        graphics.FillRectangle(panelBrush, scaled 0.584 bitmap.Width, scaled 0.071 bitmap.Height, scaled 0.195 bitmap.Width, scaled 0.884 bitmap.Height)
        graphics.FillRectangle(darkPanelBrush, scaled 0.589 bitmap.Width, scaled 0.205 bitmap.Height, scaled 0.186 bitmap.Width, scaled 0.486 bitmap.Height)
        graphics.FillRectangle(buttonBrush, scaled 0.589 bitmap.Width, scaled 0.901 bitmap.Height, scaled 0.186 bitmap.Width, scaled 0.045 bitmap.Height)

        let squareSize = size / 8
        use lightBrush = new SolidBrush(Color.FromArgb(235, 236, 208))
        use darkBrush = new SolidBrush(Color.FromArgb(119, 149, 86))

        for rank in 0 .. 7 do
            for file in 0 .. 7 do
                let brush =
                    if (file + rank) % 2 = 0 then
                        lightBrush
                    else
                        darkBrush

                graphics.FillRectangle(
                    brush,
                    left + file * squareSize,
                    top + rank * squareSize,
                    squareSize,
                    squareSize)

        use labelFont = new Font("Arial", 18.0f, FontStyle.Bold, GraphicsUnit.Pixel)
        use greenLabelBrush = new SolidBrush(Color.FromArgb(110, 145, 80))
        use lightLabelBrush = new SolidBrush(Color.FromArgb(235, 236, 208))

        for rank in 0 .. 7 do
            let label = string (8 - rank)
            let brush = if rank % 2 = 0 then greenLabelBrush else lightLabelBrush
            graphics.DrawString(label, labelFont, brush, float32 (left + 5), float32 (top + rank * squareSize + 6))

        for file in 0 .. 7 do
            let label = string (char (int 'a' + file))
            let brush = if file % 2 = 0 then lightLabelBrush else greenLabelBrush
            graphics.DrawString(label, labelFont, brush, float32 (left + file * squareSize + squareSize - 20), float32 (top + size - 22))

        let drawPiece file rank isWhite =
            let centerX = left + file * squareSize + squareSize / 2
            let centerY = top + rank * squareSize + squareSize / 2
            let bodyColor =
                if isWhite then
                    Color.White
                else
                    Color.FromArgb(70, 70, 70)

            use bodyBrush = new SolidBrush(bodyColor)
            use outlinePen = new Pen(Color.FromArgb(55, 55, 55), 3.0f)
            let width = squareSize * 5 / 10
            let height = squareSize * 7 / 10
            let x = centerX - width / 2
            let y = centerY - height / 2
            graphics.FillEllipse(bodyBrush, x, y, width, height)
            graphics.DrawEllipse(outlinePen, x, y, width, height)
            graphics.FillRectangle(bodyBrush, x + width / 4, y + height * 2 / 3, width / 2, height / 4)
            graphics.DrawRectangle(outlinePen, x + width / 4, y + height * 2 / 3, width / 2, height / 4)

        for file in 0 .. 7 do
            drawPiece file 1 false
            drawPiece file 6 true

        for file in 0 .. 7 do
            drawPiece file 0 false
            drawPiece file 7 true

    let private drawLowContrastBoardScreenshot (bitmap: Bitmap) left top size =
        use graphics = Graphics.FromImage bitmap
        graphics.Clear(Color.FromArgb(39, 41, 45))

        use panelBrush = new SolidBrush(Color.FromArgb(48, 50, 55))
        use textStripBrush = new SolidBrush(Color.FromArgb(58, 60, 66))
        graphics.FillRectangle(panelBrush, bitmap.Width - 310, 40, 270, bitmap.Height - 80)
        graphics.FillRectangle(textStripBrush, 24, 24, bitmap.Width - 380, 36)
        graphics.FillRectangle(textStripBrush, 24, bitmap.Height - 60, bitmap.Width - 380, 32)

        let squareSize = size / 8
        use lightBrush = new SolidBrush(Color.FromArgb(170, 174, 180))
        use darkBrush = new SolidBrush(Color.FromArgb(126, 132, 141))

        for rank in 0 .. 7 do
            for file in 0 .. 7 do
                let brush =
                    if (file + rank) % 2 = 0 then
                        lightBrush
                    else
                        darkBrush

                graphics.FillRectangle(
                    brush,
                    left + file * squareSize,
                    top + rank * squareSize,
                    squareSize,
                    squareSize)

        use labelFont = new Font("Arial", 16.0f, FontStyle.Bold, GraphicsUnit.Pixel)
        use lightLabelBrush = new SolidBrush(Color.FromArgb(216, 218, 220))
        use darkLabelBrush = new SolidBrush(Color.FromArgb(84, 90, 99))

        for rank in 0 .. 7 do
            let brush = if rank % 2 = 0 then darkLabelBrush else lightLabelBrush
            graphics.DrawString(string (8 - rank), labelFont, brush, float32 (left + 4), float32 (top + rank * squareSize + 4))

        for file in 0 .. 7 do
            let brush = if file % 2 = 0 then lightLabelBrush else darkLabelBrush
            graphics.DrawString(string (char (int 'a' + file)), labelFont, brush, float32 (left + file * squareSize + squareSize - 18), float32 (top + size - 20))

        let drawPiece file rank isWhite =
            let centerX = left + file * squareSize + squareSize / 2
            let centerY = top + rank * squareSize + squareSize / 2
            let fill =
                if isWhite then
                    Color.FromArgb(235, 235, 230)
                else
                    Color.FromArgb(62, 64, 68)

            use bodyBrush = new SolidBrush(fill)
            use outlinePen = new Pen(Color.FromArgb(35, 35, 38), 3.0f)
            let width = squareSize * 56 / 100
            let height = squareSize * 76 / 100
            let x = centerX - width / 2
            let y = centerY - height / 2
            graphics.FillEllipse(bodyBrush, x, y, width, height)
            graphics.DrawEllipse(outlinePen, x, y, width, height)
            graphics.FillRectangle(bodyBrush, x + width / 5, y + height * 2 / 3, width * 3 / 5, height / 4)
            graphics.DrawRectangle(outlinePen, x + width / 5, y + height * 2 / 3, width * 3 / 5, height / 4)

        for file in 0 .. 7 do
            drawPiece file 1 false
            drawPiece file 6 true

        drawPiece 0 0 false
        drawPiece 3 0 false
        drawPiece 4 7 true
        drawPiece 7 7 true

    [<Fact>]
    let ``Conservative detector finds a visible synthetic chessboard`` () =
        use bitmap = new Bitmap(640, 480)
        let left = 137
        let top = 89
        let size = 296

        drawSyntheticBoard bitmap left top size

        let detector = ConservativeBoardDetector() :> IBoardDetector

        match detector.Detect bitmap with
        | BoardDetected geometry ->
            Assert.InRange(Math.Abs(geometry.Left - left), 0, 32)
            Assert.InRange(Math.Abs(geometry.Top - top), 0, 32)
            Assert.InRange(Math.Abs(geometry.Size - size), 0, 32)
        | BoardNotFound -> failwith "Expected visible synthetic chessboard to be detected."

    [<Fact>]
    let ``Conservative detector finds chessboard in chess com screenshot layout`` () =
        use bitmap = new Bitmap(1024, 576)
        let left = 240
        let top = 61
        let size = 344

        drawChessComLikeScreenshot bitmap left top size

        let detector = ConservativeBoardDetector() :> IBoardDetector

        match detector.Detect bitmap with
        | BoardDetected geometry ->
            Assert.InRange(Math.Abs(geometry.Left - left), 0, 24)
            Assert.InRange(Math.Abs(geometry.Top - top), 0, 24)
            Assert.InRange(Math.Abs(geometry.Size - size), 0, 24)
        | BoardNotFound -> failwith "Expected chess.com screenshot layout chessboard to be detected."

    [<Fact>]
    let ``Conservative detector finds low contrast labelled board`` () =
        use bitmap = new Bitmap(1366, 768)
        let left = 96
        let top = 82
        let size = 592

        drawLowContrastBoardScreenshot bitmap left top size

        let detector = ConservativeBoardDetector() :> IBoardDetector

        match detector.Detect bitmap with
        | BoardDetected geometry ->
            Assert.InRange(Math.Abs(geometry.Left - left), 0, 28)
            Assert.InRange(Math.Abs(geometry.Top - top), 0, 28)
            Assert.InRange(Math.Abs(geometry.Size - size), 0, 28)
        | BoardNotFound -> failwith "Expected low contrast labelled chessboard to be detected."

    [<Fact>]
    let ``Fixed detector returns configured geometry`` () =
        let expected = { Left = 10; Top = 20; Size = 320 }
        let concreteDetector = FixedBoardDetector expected
        let detector = concreteDetector :> IBoardDetector

        use bitmap = new Bitmap(400, 400)

        Assert.Equal(expected, concreteDetector.Geometry)
        Assert.Equal(BoardDetected expected, detector.Detect bitmap)

    [<Fact>]
    let ``Board readers expose FEN uncertain and fixed behavior`` () =
        use bitmap = new Bitmap(20, 20)
        let geometry = { Left = 0; Top = 0; Size = 20 }
        let fenReader = FenBoardReader("8/8/8/8/8/8/8/8 w - - 0 1") :> IBoardReader
        let invalidFenReader = FenBoardReader("invalid") :> IBoardReader
        let uncertainReader = UncertainBoardReader() :> IBoardReader

        match fenReader.Read(bitmap, geometry) with
        | Some reading ->
            Assert.Empty(reading.Board)
            Assert.Equal(1.0, reading.Confidence)
        | None -> failwith "Expected valid FEN reader output."

        Assert.True(invalidFenReader.Read(bitmap, geometry) |> Option.isNone)
        Assert.True(uncertainReader.Read(bitmap, geometry) |> Option.isNone)
