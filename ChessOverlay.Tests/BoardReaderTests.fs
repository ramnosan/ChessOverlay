namespace ChessOverlay.Tests

open System.Drawing
open Xunit
open ChessOverlay

module BoardReaderTests =
    [<Fact>]
    let ``Board readers expose FEN and uncertain behavior`` () =
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

    [<Fact>]
    let ``readingFromFen yields a confident reading for a valid FEN`` () =
        match BoardReaderHelpers.readingFromFen "8/8/8/8/8/8/8/4K3 w - - 0 1" with
        | Some reading ->
            Assert.Equal(1.0, reading.Confidence)
            Assert.Equal(Some { Color = White; Kind = King }, BoardState.tryPieceAt { File = 4; Rank = 7 } reading.Board)
        | None -> failwith "Expected a reading for a valid FEN."

    [<Fact>]
    let ``readingFromFen returns None for an invalid FEN`` () =
        Assert.True(BoardReaderHelpers.readingFromFen "not-a-fen" |> Option.isNone)

    [<Fact>]
    let ``FallbackBoardReader uses the primary reading when present`` () =
        use bitmap = new Bitmap(20, 20)
        let geometry = { Left = 0; Top = 0; Size = 20 }
        let primary = FenBoardReader("8/8/8/8/8/8/8/4K3 w - - 0 1") :> IBoardReader
        let fallback = FenBoardReader("8/8/8/8/8/8/8/4k3 w - - 0 1") :> IBoardReader
        let reader = FallbackBoardReader(primary, fallback) :> IBoardReader

        match reader.Read(bitmap, geometry) with
        | Some reading ->
            Assert.Equal(Some { Color = White; Kind = King }, BoardState.tryPieceAt { File = 4; Rank = 7 } reading.Board)
        | None -> failwith "Expected the primary reading."

    [<Fact>]
    let ``FallbackBoardReader falls back when the primary returns None`` () =
        use bitmap = new Bitmap(20, 20)
        let geometry = { Left = 0; Top = 0; Size = 20 }
        let primary = UncertainBoardReader() :> IBoardReader
        let fallback = FenBoardReader("8/8/8/8/8/8/8/4k3 w - - 0 1") :> IBoardReader
        let reader = FallbackBoardReader(primary, fallback) :> IBoardReader

        match reader.Read(bitmap, geometry) with
        | Some reading ->
            Assert.Equal(Some { Color = Black; Kind = King }, BoardState.tryPieceAt { File = 4; Rank = 7 } reading.Board)
        | None -> failwith "Expected the fallback reading."

    [<Fact>]
    let ``FallbackBoardReader returns None when neither reader produces a reading`` () =
        use bitmap = new Bitmap(20, 20)
        let geometry = { Left = 0; Top = 0; Size = 20 }
        let reader =
            FallbackBoardReader(UncertainBoardReader(), UncertainBoardReader()) :> IBoardReader

        Assert.True(reader.Read(bitmap, geometry) |> Option.isNone)
