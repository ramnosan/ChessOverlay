namespace ChessOverlay.Tests

open System
open System.Drawing
open Xunit
open ChessOverlay

module BoardDetectorCacheTests =
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
    let ``Conservative detector caches a detected board`` () =
        use bitmap = new Bitmap(640, 480)
        let detector = ConservativeBoardDetector()

        drawSyntheticBoard bitmap 137 89 296

        match detector.Detect bitmap with
        | BoardDetected _ -> Assert.True(detector.CachedGeometry.IsSome)
        | BoardNotFound -> failwith "Expected visible synthetic chessboard to be detected."

    [<Fact>]
    let ``Conservative detector clears stale cache after repeated misses`` () =
        use boardBitmap = new Bitmap(640, 480)
        use blankBitmap = new Bitmap(640, 480)
        let detector = ConservativeBoardDetector()

        drawSyntheticBoard boardBitmap 137 89 296

        match detector.Detect boardBitmap with
        | BoardDetected _ -> ()
        | BoardNotFound -> failwith "Expected visible synthetic chessboard to be detected."

        use graphics = Graphics.FromImage blankBitmap
        graphics.Clear(Color.FromArgb(32, 34, 38))

        for _ in 1 .. 3 do
            detector.Detect blankBitmap |> ignore

        Assert.True(detector.CachedGeometry.IsNone)
