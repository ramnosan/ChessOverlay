namespace ChessOverlay

module AttackCalculator =
    let private addIfValid file rank (attacks: Set<Square>) =
        match Squares.tryCreate file rank with
        | Some square -> Set.add square attacks
        | None -> attacks

    let private rayAttacks board square fileDelta rankDelta =
        let rec loop file rank attacks =
            match Squares.tryCreate file rank with
            | None -> attacks
            | Some target ->
                let next = Set.add target attacks

                if BoardState.occupied target board then
                    next
                else
                    loop (file + fileDelta) (rank + rankDelta) next

        loop (square.File + fileDelta) (square.Rank + rankDelta) Set.empty

    // Only the enemy (top) player's attacks are ever highlighted, and the top
    // player moves down the screen regardless of colour, so pawns always attack
    // toward increasing ranks here.
    let private pawnAttacks square =
        Set.empty
        |> addIfValid (square.File - 1) (square.Rank + 1)
        |> addIfValid (square.File + 1) (square.Rank + 1)

    let private knightAttacks square =
        [ -2, -1
          -2, 1
          -1, -2
          -1, 2
          1, -2
          1, 2
          2, -1
          2, 1 ]
        |> List.choose (fun (fileDelta, rankDelta) ->
            Squares.tryCreate (square.File + fileDelta) (square.Rank + rankDelta))
        |> Set.ofList

    let private kingAttacks square =
        [ for rankDelta in -1 .. 1 do
              for fileDelta in -1 .. 1 do
                  if fileDelta <> 0 || rankDelta <> 0 then
                      fileDelta, rankDelta ]
        |> List.choose (fun (fileDelta, rankDelta) ->
            Squares.tryCreate (square.File + fileDelta) (square.Rank + rankDelta))
        |> Set.ofList

    let private bishopDirections =
        [ -1, -1; -1, 1; 1, -1; 1, 1 ]

    let private rookDirections =
        [ -1, 0; 1, 0; 0, -1; 0, 1 ]

    let private queenDirections = bishopDirections @ rookDirections

    let private slidingAttacks board square directions =
        directions
        |> List.map (fun (fileDelta, rankDelta) -> rayAttacks board square fileDelta rankDelta)
        |> Set.unionMany

    let attacksForPiece board square piece =
        Map.ofList [
            Pawn, pawnAttacks
            Knight, knightAttacks
            Bishop, fun target -> slidingAttacks board target bishopDirections
            Rook, fun target -> slidingAttacks board target rookDirections
            Queen, fun target -> slidingAttacks board target queenDirections
            King, kingAttacks
        ]
        |> Map.find piece.Kind
        <| square

    let attackedSquaresByColor (board: BoardState) color =
        board
        |> Map.toSeq
        |> Seq.choose (fun (square, piece) ->
            if piece.Color = color then
                Some(attacksForPiece board square piece)
            else
                None)
        |> fun attacks ->
            if Seq.isEmpty attacks then
                Set.empty
            else
                Set.unionMany attacks

    let private meanRank color (board: BoardState) =
        let ranks =
            board
            |> Map.toSeq
            |> Seq.choose (fun (square, piece) -> if piece.Color = color then Some(float square.Rank) else None)
            |> Seq.toList

        match ranks with
        | [] -> None
        | _ -> Some(List.average ranks)

    // The top player is always the enemy, but they may be either colour (the
    // user plays both sides). Rank 0 is the top of the screen, so the colour
    // whose pieces sit at the lower mean rank is the side on top.
    let enemyColor (board: BoardState) : PieceColor option =
        match meanRank White board, meanRank Black board with
        | Some white, Some black -> Some(if white <= black then White else Black)
        | Some _, None -> Some White
        | None, Some _ -> Some Black
        | None, None -> None

    let enemyAttackedSquares (board: BoardState) =
        match enemyColor board with
        | Some color -> attackedSquaresByColor board color
        | None -> Set.empty
