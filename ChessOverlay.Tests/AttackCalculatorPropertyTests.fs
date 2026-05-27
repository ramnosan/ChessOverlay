namespace ChessOverlay.Tests

open Xunit
open FsCheck
open FsCheck.FSharp.GenBuilder
open ChessOverlay

module AttackCalculatorPropertyTests =

    module G = FsCheck.FSharp.Gen
    module A = FsCheck.FSharp.Arb
    module P = FsCheck.FSharp.Prop

    let genSquare = gen {
        let! file = G.choose (0, 7)
        let! rank = G.choose (0, 7)
        return { File = file; Rank = rank }
    }

    let genPieceColor = G.elements [White; Black]

    let genPieceKind = G.elements [Pawn; Knight; Bishop; Rook; Queen; King]

    let genPiece = gen {
        let! color = genPieceColor
        let! kind = genPieceKind
        return { Color = color; Kind = kind }
    }

    let genBoardState =
        gen {
            let! pieceCount = G.choose (3, 16)
            let! shuffledSquares =
                G.shuffle Squares.all
                |> G.map (Array.toList >> List.truncate pieceCount)
            let! pieces = G.arrayOfLength pieceCount genPiece
            let board = List.zip shuffledSquares (List.ofArray pieces) |> Map.ofList
            return board
        }

    let boardArb : Arbitrary<BoardState> = A.fromGen genBoardState

    let private runConfig =
        Config.QuickThrowOnFailure.WithMaxTest(2000)

    let private checkProp (propFunc: BoardState -> bool) =
        Check.One(runConfig, P.forAll boardArb propFunc)

    let private opposite (color: PieceColor) =
        if color = White then Black else White

    let private countEnemyPiecesAttackedAfterMove (board: BoardState) fromSquare toSquare piece enemy =
        let moved = board |> Map.remove fromSquare |> Map.add toSquare piece
        let attacked =
            AttackCalculator.attacksForPieceWithDir moved toSquare piece -1
        moved
        |> Map.filter (fun sq p -> p.Color = enemy && Set.contains sq attacked)
        |> Map.count

    let private isKingSafeAfterMove (board: BoardState) fromSquare toSquare piece friendly enemy =
        let moved = board |> Map.remove fromSquare |> Map.add toSquare piece
        let kingSquare =
            moved
            |> Map.toSeq
            |> Seq.tryPick (fun (sq, p) ->
                if p.Color = friendly && p.Kind = King then Some sq else None)
        match kingSquare with
        | None -> true
        | Some king ->
            AttackCalculator.attackedSquaresByColorWithDir moved enemy 1
            |> Set.contains king
            |> not

    let private bruteForceForkArrows (board: BoardState) =
        match AttackCalculator.enemyColor board with
        | None -> []
        | Some enemy ->
            let friendly = opposite enemy
            board
            |> Map.toSeq
            |> Seq.filter (fun (_, p) -> p.Color = friendly)
            |> Seq.collect (fun (fromSq, piece) ->
                Squares.all
                |> Seq.filter (fun toSq ->
                    toSq <> fromSq
                    && (match Map.tryFind toSq board with
                        | Some p -> p.Color <> friendly
                        | None -> true))
                |> Seq.filter (fun toSq ->
                    isKingSafeAfterMove board fromSq toSq piece friendly enemy)
                |> Seq.filter (fun toSq ->
                    countEnemyPiecesAttackedAfterMove board fromSq toSq piece enemy >= 2)
                |> Seq.map (fun toSq -> (fromSq, toSq)))
            |> Seq.distinct
            |> Seq.toList

    [<Fact>]
    let ``property: all fork arrows reference squares within the board`` () =
        checkProp (fun board ->
            AttackCalculator.friendlyForkMoveArrows board
            |> List.forall (fun (fromSq, toSq) ->
                Squares.isValid fromSq && Squares.isValid toSq))

    [<Fact>]
    let ``property: fork from-square differs from to-square`` () =
        checkProp (fun board ->
            AttackCalculator.friendlyForkMoveArrows board
            |> List.forall (fun (fromSq, toSq) -> fromSq <> toSq))

    [<Fact>]
    let ``property: fork from-square is occupied`` () =
        checkProp (fun board ->
            AttackCalculator.friendlyForkMoveArrows board
            |> List.forall (fun (fromSq, _) -> Map.containsKey fromSq board))

    [<Fact>]
    let ``property: fork from-square holds a friendly piece`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> true
            | Some enemy ->
                let friendly = opposite enemy
                AttackCalculator.friendlyForkMoveArrows board
                |> List.forall (fun (fromSq, _) ->
                    match Map.tryFind fromSq board with
                    | Some p -> p.Color = friendly
                    | None -> false))

    [<Fact>]
    let ``property: fork to-square is empty or has an enemy piece`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> true
            | Some enemy ->
                let friendly = opposite enemy
                AttackCalculator.friendlyForkMoveArrows board
                |> List.forall (fun (_, toSq) ->
                    match Map.tryFind toSq board with
                    | Some p -> p.Color <> friendly
                    | None -> true))

    [<Fact>]
    let ``property: fork arrows contain no duplicates`` () =
        checkProp (fun board ->
            let arrows = AttackCalculator.friendlyForkMoveArrows board
            arrows.Length = (arrows |> Set.ofList).Count)

    [<Fact>]
    let ``property: friendly king is safe after every fork move`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> true
            | Some enemy ->
                let friendly = opposite enemy
                AttackCalculator.friendlyForkMoveArrows board
                |> List.forall (fun (fromSq, toSq) ->
                    match Map.tryFind fromSq board with
                    | Some piece when piece.Color = friendly ->
                        isKingSafeAfterMove board fromSq toSq piece friendly enemy
                    | _ -> true))

    [<Fact>]
    let ``property: each fork arrow attacks at least 2 enemy pieces`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> true
            | Some enemy ->
                AttackCalculator.friendlyForkMoveArrows board
                |> List.forall (fun (fromSq, toSq) ->
                    match Map.tryFind fromSq board with
                    | Some piece ->
                        countEnemyPiecesAttackedAfterMove board fromSq toSq piece enemy >= 2
                    | None -> false))

    [<Fact>]
    let ``property: real fork arrows are a subset of the brute-force reference`` () =
        checkProp (fun board ->
            let real = AttackCalculator.friendlyForkMoveArrows board |> Set.ofList
            let reference = bruteForceForkArrows board |> Set.ofList
            Set.isSubset real reference)

    [<Fact>]
    let ``property: all hanging squares contain a piece`` () =
        checkProp (fun board ->
            AttackCalculator.hangingSquares board
            |> Set.forall (fun sq -> Map.containsKey sq board))

    [<Fact>]
    let ``property: all enemy hanging squares contain a piece`` () =
        checkProp (fun board ->
            AttackCalculator.enemyHangingSquares board
            |> Set.forall (fun sq -> Map.containsKey sq board))

    [<Fact>]
    let ``property: hanging squares contain friendly pieces`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> AttackCalculator.hangingSquares board |> Set.isEmpty
            | Some enemy ->
                let friendly = opposite enemy
                let hanging = AttackCalculator.hangingSquares board
                hanging |> Set.forall (fun sq ->
                    match Map.tryFind sq board with
                    | Some p -> p.Color = friendly
                    | None -> false))

    [<Fact>]
    let ``property: enemy forks reference valid squares`` () =
        checkProp (fun board ->
            AttackCalculator.enemyForks board
            |> List.forall (fun (sq, forked) ->
                Squares.isValid sq
                && forked |> Set.forall Squares.isValid))

    [<Fact>]
    let ``property: enemy forks originate from enemy pieces`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> AttackCalculator.enemyForks board |> List.isEmpty
            | Some enemy ->
                AttackCalculator.enemyForks board
                |> List.forall (fun (sq, _) ->
                    match Map.tryFind sq board with
                    | Some p -> p.Color = enemy
                    | None -> false))

    [<Fact>]
    let ``property: enemy fork forked squares contain friendly pieces`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> true
            | Some enemy ->
                let friendly = opposite enemy
                AttackCalculator.enemyForks board
                |> List.forall (fun (_, forked) ->
                    forked |> Set.forall (fun sq ->
                        match Map.tryFind sq board with
                        | Some p -> p.Color = friendly
                        | None -> false)))

    [<Fact>]
    let ``property: enemy attack arrows reference valid squares`` () =
        checkProp (fun board ->
            AttackCalculator.enemyAttackArrows board
            |> List.forall (fun (src, dst) ->
                Squares.isValid src && Squares.isValid dst))

    [<Fact>]
    let ``property: enemy attack arrows originate from enemy pieces`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> AttackCalculator.enemyAttackArrows board |> List.isEmpty
            | Some enemy ->
                AttackCalculator.enemyAttackArrows board
                |> List.forall (fun (src, _) ->
                    match Map.tryFind src board with
                    | Some p -> p.Color = enemy
                    | None -> false))

    [<Fact>]
    let ``property: enemy attack arrows have no duplicates`` () =
        checkProp (fun board ->
            let arrows = AttackCalculator.enemyAttackArrows board
            arrows.Length = (arrows |> Set.ofList).Count)

    [<Fact>]
    let ``property: attacksForPiece equals union of rays from attackRaysForPiece`` () =
        checkProp (fun board ->
            board
            |> Map.forall (fun sq piece ->
                let raysUnion =
                    AttackCalculator.attackRaysForPiece board sq piece
                    |> List.concat
                    |> Set.ofList
                let direct = AttackCalculator.attacksForPiece board sq piece
                raysUnion = direct))

    [<Fact>]
    let ``property: all attacked squares are valid board squares`` () =
        checkProp (fun board ->
            board
            |> Map.forall (fun sq piece ->
                AttackCalculator.attacksForPiece board sq piece
                |> Set.forall Squares.isValid))

    [<Fact>]
    let ``property: hanging squares are attacked by the enemy`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> AttackCalculator.hangingSquares board |> Set.isEmpty
            | Some enemy ->
                let enemyAttacked = AttackCalculator.attackedSquaresByColor board enemy
                AttackCalculator.hangingSquares board
                |> Set.forall (fun sq -> Set.contains sq enemyAttacked))

    [<Fact>]
    let ``property: enemy color is absent only when board is empty of one color`` () =
        checkProp (fun board ->
            match AttackCalculator.enemyColor board with
            | None -> Map.isEmpty board
            | Some _ -> not (Map.isEmpty board))
