namespace ChessOverlay.Tests

open Xunit
open ChessOverlay

module DomainTests =
    [<Fact>]
    let ``FEN parser rejects ranks with too many files`` () =
        match Fen.parseBoard "8/8/8/8/8/8/8/9 w - - 0 1" with
        | Ok board -> failwithf "Expected invalid FEN to fail, but parsed %A." board
        | Error _ -> ()

    [<Fact>]
    let ``FEN parser maps upper and lower case pieces to board colors`` () =
        let board =
            match Fen.parseBoard "8/8/3p4/8/8/8/3N4/8 w - - 0 1" with
            | Ok value -> value
            | Error message -> failwith message

        Assert.Equal(Some { Color = Top; Kind = Pawn }, BoardState.tryPieceAt { File = 3; Rank = 2 } board)
        Assert.Equal(Some { Color = Bottom; Kind = Knight }, BoardState.tryPieceAt { File = 3; Rank = 6 } board)

    [<Fact>]
    let ``Square names use chess coordinates from white perspective`` () =
        Assert.Equal("a8", Squares.name { File = 0; Rank = 0 })
        Assert.Equal("h1", Squares.name { File = 7; Rank = 7 })

    [<Fact>]
    let ``Board geometry maps squares to screen rectangles`` () =
        let geometry = { Left = 10; Top = 20; Size = 400 }
        let rectangle = geometry.GetSquareRectangle { File = 2; Rank = 3 }

        Assert.Equal(50.0, geometry.SquareSize, 3)
        Assert.Equal(110.0f, rectangle.X)
        Assert.Equal(170.0f, rectangle.Y)
        Assert.Equal(50.0f, rectangle.Width)
        Assert.Equal(50.0f, rectangle.Height)

    [<Fact>]
    let ``Board state helpers report occupancy`` () =
        let square = { File = 4; Rank = 4 }
        let piece = { Color = Bottom; Kind = King }
        let board = BoardState.empty |> Map.add square piece

        Assert.Equal(Some piece, BoardState.tryPieceAt square board)
        Assert.True(BoardState.occupied square board)
        Assert.False(BoardState.occupied { File = 0; Rank = 0 } board)

    [<Fact>]
    let ``FEN parser rejects malformed input`` () =
        Assert.True(Fen.parseBoard "" |> Result.isError)
        Assert.True(Fen.parseBoard "8/8/8/8/8/8/8 w - - 0 1" |> Result.isError)
        Assert.True(Fen.parseBoard "8/8/8/8/8/8/8/X7 w - - 0 1" |> Result.isError)
