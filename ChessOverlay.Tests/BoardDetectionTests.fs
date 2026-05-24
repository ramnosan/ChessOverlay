namespace ChessOverlay.Tests

open System.Drawing
open Xunit
open ChessOverlay

module BoardDetectionTests =
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
