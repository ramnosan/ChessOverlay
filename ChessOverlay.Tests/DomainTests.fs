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
