namespace ChessOverlay.Tests

open System
open System.Drawing
open System.IO
open Microsoft.ML.OnnxRuntime.Tensors
open Xunit
open ChessOverlay

module YoloPieceDetectionTests =
    type private StubDetector(detections: YoloRawDetection list) =
        interface IYoloObjectDetector with
            member _.Detect(_) = detections

    let private labels =
        [ 0, "black_queen"
          1, "white_knight"
          2, "black_pawn" ]
        |> Map.ofList

    [<Fact>]
    let ``Labels convert common class names to pieces`` () =
        Assert.Equal(Some { Color = Top; Kind = Queen }, YoloLabels.tryPiece "black_queen")
        Assert.Equal(Some { Color = Bottom; Kind = Knight }, YoloLabels.tryPiece "white-knight")

    [<Fact>]
    let ``Labels load array and object formats`` () =
        let tempDirectory = Path.Combine(Path.GetTempPath(), "ChessOverlayYoloTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDirectory) |> ignore
        let arrayPath = Path.Combine(tempDirectory, "array.json")
        let objectPath = Path.Combine(tempDirectory, "object.json")

        File.WriteAllText(arrayPath, """["black_queen", "white_knight"]""")
        File.WriteAllText(objectPath, """{"2":{"name":"black_pawn"},"ignored":"nope"}""")

        Assert.Equal(Some "black_queen", YoloLabels.load arrayPath |> Map.tryFind 0)
        Assert.Equal(Some "white_knight", YoloLabels.load arrayPath |> Map.tryFind 1)
        Assert.Equal(Some "black_pawn", YoloLabels.load objectPath |> Map.tryFind 2)

    [<Fact>]
    let ``Labels ignore unsupported JSON label values`` () =
        let tempDirectory = Path.Combine(Path.GetTempPath(), "ChessOverlayYoloTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDirectory) |> ignore
        let path = Path.Combine(tempDirectory, "labels.json")

        File.WriteAllText(path, """{"0":{},"1":{"other":"black_queen"},"2":42}""")

        Assert.Empty(YoloLabels.load path)

    [<Fact>]
    let ``Post processing maps box centers to board squares`` () =
        let detections =
            [ {
                  ClassIndex = 0
                  Confidence = 0.91
                  Bounds = RectangleF(160.0f, 80.0f, 40.0f, 40.0f)
              }
              {
                  ClassIndex = 1
                  Confidence = 0.93
                  Bounds = RectangleF(400.0f, 480.0f, 40.0f, 40.0f)
              } ]

        match YoloPostProcessing.toBoardReading labels 0.45 0.45 640 detections with
        | Some reading ->
            Assert.Equal(2, reading.Board.Count)
            Assert.Equal(Some { Color = Top; Kind = Queen }, BoardState.tryPieceAt { File = 2; Rank = 1 } reading.Board)
            Assert.Equal(Some { Color = Bottom; Kind = Knight }, BoardState.tryPieceAt { File = 5; Rank = 6 } reading.Board)
        | None -> failwith "Expected confident detections to produce a board reading."

    [<Fact>]
    let ``Post processing rejects duplicate square detections`` () =
        let detections =
            [ {
                  ClassIndex = 0
                  Confidence = 0.91
                  Bounds = RectangleF(160.0f, 80.0f, 40.0f, 40.0f)
              }
              {
                  ClassIndex = 1
                  Confidence = 0.92
                  Bounds = RectangleF(170.0f, 90.0f, 30.0f, 30.0f)
              } ]

        Assert.True(YoloPostProcessing.toBoardReading labels 0.45 0.45 640 detections |> Option.isNone)

    [<Fact>]
    let ``Non max suppression keeps highest confidence overlapping class box`` () =
        let detections =
            [ {
                  ClassIndex = 2
                  Confidence = 0.50
                  Bounds = RectangleF(10.0f, 10.0f, 50.0f, 50.0f)
              }
              {
                  ClassIndex = 2
                  Confidence = 0.90
                  Bounds = RectangleF(12.0f, 12.0f, 50.0f, 50.0f)
              } ]

        let kept = YoloPostProcessing.nonMaxSuppress 0.45 detections

        Assert.Single kept |> ignore
        Assert.Equal(0.90, kept.Head.Confidence, 2)

    [<Fact>]
    let ``Output parser reads normalized feature-major tensor`` () =
        let tensor = DenseTensor<float32>([| 1; 6; 20 |])
        tensor[0, 0, 0] <- 0.5f
        tensor[0, 1, 0] <- 0.25f
        tensor[0, 2, 0] <- 0.125f
        tensor[0, 3, 0] <- 0.25f
        tensor[0, 4, 0] <- 0.10f
        tensor[0, 5, 0] <- 0.90f

        let detections =
            YoloOutputParser.parse { InputSize = 640; ConfidenceThreshold = 0.45 } 640 tensor

        let detection = Assert.Single detections
        Assert.Equal(1, detection.ClassIndex)
        Assert.Equal(0.90, detection.Confidence, 2)
        Assert.Equal(RectangleF(280.0f, 80.0f, 80.0f, 160.0f), detection.Bounds)

    [<Fact>]
    let ``Output parser handles anchor-major tensors with objectness`` () =
        let tensor = DenseTensor<float32>([| 1; 20; 17 |])
        tensor[0, 0, 0] <- 320.0f
        tensor[0, 0, 1] <- 160.0f
        tensor[0, 0, 2] <- 80.0f
        tensor[0, 0, 3] <- 160.0f
        tensor[0, 0, 4] <- 0.5f
        tensor[0, 0, 5] <- 0.2f
        tensor[0, 0, 6] <- 0.9f

        let detections =
            YoloOutputParser.parse { InputSize = 640; ConfidenceThreshold = 0.45 } 640 tensor

        let detection = Assert.Single detections
        Assert.Equal(1, detection.ClassIndex)
        Assert.Equal(0.45, detection.Confidence, 2)
        Assert.Equal(RectangleF(280.0f, 80.0f, 80.0f, 160.0f), detection.Bounds)

    [<Fact>]
    let ``Output parser rejects unsupported shapes and low confidence`` () =
        let invalid = DenseTensor<float32>([| 1; 5; 20 |])
        let lowConfidence = DenseTensor<float32>([| 1; 6; 20 |])
        lowConfidence[0, 4, 0] <- 0.1f
        lowConfidence[0, 5, 0] <- 0.2f

        Assert.Empty(YoloOutputParser.parse { InputSize = 640; ConfidenceThreshold = 0.45 } 640 invalid)
        Assert.Empty(YoloOutputParser.parse { InputSize = 640; ConfidenceThreshold = 0.45 } 640 lowConfidence)

    [<Fact>]
    let ``Board reader crops board area and rejects out of bounds geometry`` () =
        let detections =
            [ {
                  ClassIndex = 0
                  Confidence = 0.95
                  Bounds = RectangleF(40.0f, 40.0f, 20.0f, 20.0f)
              } ]

        let reader = YoloBoardReader(StubDetector detections, labels, confidenceThreshold = 0.45) :> IBoardReader
        use bitmap = new Bitmap(200, 200)

        match reader.Read(bitmap, { Left = 50; Top = 50; Size = 80 }) with
        | Some reading ->
            Assert.Equal(Some { Color = Top; Kind = Queen }, BoardState.tryPieceAt { File = 5; Rank = 5 } reading.Board)
            Assert.Equal(0.95, reading.Confidence, 2)
        | None -> failwith "Expected in-bounds crop to produce a reading."

        Assert.True(reader.Read(bitmap, { Left = 150; Top = 150; Size = 80 }) |> Option.isNone)
