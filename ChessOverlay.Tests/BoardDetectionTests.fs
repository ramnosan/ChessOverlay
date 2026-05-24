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
    let ``Fixed detector returns configured geometry`` () =
        let expected = { Left = 10; Top = 20; Size = 320 }
        let detector = FixedBoardDetector expected :> IBoardDetector

        use bitmap = new Bitmap(400, 400)

        Assert.Equal(BoardDetected expected, detector.Detect bitmap)
