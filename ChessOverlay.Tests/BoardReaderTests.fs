namespace ChessOverlay.Tests

open System.Drawing
open Xunit
open ChessOverlay

module BoardReaderTests =
    let private boardFromFen fen =
        match Fen.parseBoard fen with
        | Ok board -> board
        | Error message -> failwith message

    type private FixedBoardReader(reading: BoardReading option) =
        interface IBoardReader with
            member _.Read(_, _) = reading

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

    [<Fact>]
    let ``LastBoardStateReader saves successful live readings`` () =
        use bitmap = new Bitmap(20, 20)
        let geometry = { Left = 0; Top = 0; Size = 20 }
        let expected = boardFromFen "8/8/8/8/8/8/4K3/8 w - - 0 1"
        let live = FenBoardReader("8/8/8/8/8/8/4K3/8 w - - 0 1") :> IBoardReader
        let mutable saved = None
        let reader = LastBoardStateReader(live, (fun () -> None), (fun board -> saved <- Some board)) :> IBoardReader

        match reader.Read(bitmap, geometry) with
        | Some reading ->
            Assert.Equal<BoardState>(expected, reading.Board)
            Assert.Equal(Some expected, saved)
        | None -> failwith "Expected the live reading."

    [<Fact>]
    let ``LastBoardStateReader loads saved board when live reading is unavailable`` () =
        use bitmap = new Bitmap(20, 20)
        let geometry = { Left = 0; Top = 0; Size = 20 }
        let savedBoard = boardFromFen "8/8/8/8/3q4/8/4K3/8 w - - 0 1"
        let live = UncertainBoardReader() :> IBoardReader
        let reader = LastBoardStateReader(live, (fun () -> Some savedBoard), ignore) :> IBoardReader

        match reader.Read(bitmap, geometry) with
        | Some reading ->
            Assert.Equal<BoardState>(savedBoard, reading.Board)
            Assert.Equal(1.0, reading.Confidence)
            Assert.Equal("Last board state", reading.Strategy)
        | None -> failwith "Expected the saved board reading."

    [<Fact>]
    let ``LastBoardStateReader loads saved board when live reading has low confidence`` () =
        use bitmap = new Bitmap(20, 20)
        let geometry = { Left = 0; Top = 0; Size = 20 }
        let lowConfidenceBoard = boardFromFen "8/8/8/8/8/8/4k3/8 w - - 0 1"
        let savedBoard = boardFromFen "8/8/8/8/3q4/8/4K3/8 w - - 0 1"

        let live =
            FixedBoardReader(
                Some
                    {
                        Board = lowConfidenceBoard
                        Confidence = 0.0
                        Candidates = Map.empty
                        Strategy = "Template"
                    })
            :> IBoardReader

        let mutable saved = None
        let reader = LastBoardStateReader(live, (fun () -> Some savedBoard), (fun board -> saved <- Some board)) :> IBoardReader

        match reader.Read(bitmap, geometry) with
        | Some reading ->
            Assert.Equal<BoardState>(savedBoard, reading.Board)
            Assert.Equal(None, saved)
            Assert.Equal("Last board state", reading.Strategy)
        | None -> failwith "Expected the saved board reading."
