namespace ChessOverlay.Tests.PropertyTests

open Xunit
open FsCheck
open FsCheck.FSharp.GenBuilder
open ChessOverlay.Tests
open ChessOverlay

module DomainPropertyTests =

    module G = FsCheck.FSharp.Gen
    module A = FsCheck.FSharp.Arb
    module P = FsCheck.FSharp.Prop

    let private runConfig =
        Config.QuickThrowOnFailure.WithMaxTest(2000).WithQuietOnSuccess(true)

    let private squareArb = A.fromGen Generators.genSquare

    // ---- Squares -------------------------------------------------------------

    [<Fact>]
    let ``property: tryCreate succeeds exactly on board coordinates and preserves them`` () =
        let coordArb =
            A.fromGen (
                gen {
                    let! file = G.choose (-4, 11)
                    let! rank = G.choose (-4, 11)
                    return file, rank
                })

        Check.One(
            runConfig,
            P.forAll coordArb (fun (file, rank) ->
                let onBoard = file >= 0 && file < 8 && rank >= 0 && rank < 8

                match Squares.tryCreate file rank with
                | Some square ->
                    onBoard
                    && Squares.isValid square
                    && square.File = file
                    && square.Rank = rank
                | None -> not onBoard))

    [<Fact>]
    let ``property: square names map to file a-h and rank 1-8`` () =
        Check.One(
            runConfig,
            P.forAll squareArb (fun square ->
                let name = Squares.name square

                name.Length = 2
                && name[0] >= 'a'
                && name[0] <= 'h'
                && name[1] >= '1'
                && name[1] <= '8'))

    [<Fact>]
    let ``property: square name is injective over valid squares`` () =
        let pairArb =
            A.fromGen (
                gen {
                    let! left = Generators.genSquare
                    let! right = Generators.genSquare
                    return left, right
                })

        Check.One(
            runConfig,
            P.forAll pairArb (fun (left, right) ->
                (Squares.name left = Squares.name right) = (left = right)))

    // ---- Fen.parseBoard ------------------------------------------------------

    let private pieceChar (piece: Piece) =
        let letter =
            match piece.Kind with
            | Pawn -> 'p'
            | Knight -> 'n'
            | Bishop -> 'b'
            | Rook -> 'r'
            | Queen -> 'q'
            | King -> 'k'

        if piece.Color = White then System.Char.ToUpperInvariant letter else letter

    let private rankToFen (cells: Piece option[]) =
        let builder = System.Text.StringBuilder()
        let mutable emptyRun = 0

        let flush () =
            if emptyRun > 0 then
                builder.Append(string emptyRun) |> ignore
                emptyRun <- 0

        for cell in cells do
            match cell with
            | None -> emptyRun <- emptyRun + 1
            | Some piece ->
                flush ()
                builder.Append(pieceChar piece) |> ignore

        flush ()
        builder.ToString()

    let private gridToFen (grid: Piece option[][]) =
        grid |> Array.map rankToFen |> String.concat "/"

    let private gridToBoard (grid: Piece option[][]) : BoardState =
        [ for rank in 0..7 do
              for file in 0..7 do
                  match grid[rank][file] with
                  | Some piece -> yield { File = file; Rank = rank }, piece
                  | None -> () ]
        |> Map.ofList

    let private genGrid =
        let genCell =
            G.frequency [ 2, G.constant None; 1, Generators.genPiece |> G.map Some ]

        G.arrayOfLength 8 (G.arrayOfLength 8 genCell)

    let private gridArb = A.fromGen genGrid

    [<Fact>]
    let ``property: parseBoard reconstructs every generated board placement`` () =
        Check.One(
            runConfig,
            P.forAll gridArb (fun grid ->
                Fen.parseBoard (gridToFen grid) = Ok(gridToBoard grid)))

    [<Fact>]
    let ``property: parseBoard never yields off-board squares or oversized boards`` () =
        Check.One(
            runConfig,
            fun (text: string) ->
                match Fen.parseBoard text with
                | Error _ -> true
                | Ok board ->
                    Map.count board <= 64
                    && board |> Map.forall (fun square _ -> Squares.isValid square))

    [<Fact>]
    let ``property: parseBoard is deterministic`` () =
        Check.One(runConfig, fun (text: string) -> Fen.parseBoard text = Fen.parseBoard text)
