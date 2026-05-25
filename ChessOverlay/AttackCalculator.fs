namespace ChessOverlay

module AttackCalculator =
    // A ray is the ordered list of squares a piece reaches in a single
    // direction, nearest first. Non-sliding pieces produce one single-square
    // ray per target. Keeping the per-direction grouping lets us draw a single
    // arrow to the farthest reachable square instead of one per square.
    let private rayLine board square fileDelta rankDelta =
        let rec loop file rank acc =
            match Squares.tryCreate file rank with
            | None -> acc
            | Some target ->
                let acc = target :: acc

                if BoardState.occupied target board then
                    acc
                else
                    loop (file + fileDelta) (rank + rankDelta) acc

        loop (square.File + fileDelta) (square.Rank + rankDelta) []
        |> List.rev

    let private steps deltas square =
        deltas
        |> List.choose (fun (fileDelta, rankDelta) ->
            Squares.tryCreate (square.File + fileDelta) (square.Rank + rankDelta))
        |> List.map List.singleton

    // Only the enemy (top) player's attacks are ever highlighted, and the top
    // player moves down the screen regardless of colour, so pawns always attack
    // toward increasing ranks here.
    let private pawnRays square =
        steps [ -1, 1; 1, 1 ] square

    let private knightRays square =
        steps [ -2, -1; -2, 1; -1, -2; -1, 2; 1, -2; 1, 2; 2, -1; 2, 1 ] square

    let private kingRays square =
        [ for rankDelta in -1 .. 1 do
              for fileDelta in -1 .. 1 do
                  if fileDelta <> 0 || rankDelta <> 0 then
                      fileDelta, rankDelta ]
        |> fun deltas -> steps deltas square

    let private bishopDirections =
        [ -1, -1; -1, 1; 1, -1; 1, 1 ]

    let private rookDirections =
        [ -1, 0; 1, 0; 0, -1; 0, 1 ]

    let private queenDirections = bishopDirections @ rookDirections

    let private slidingRays board square directions =
        directions
        |> List.map (fun (fileDelta, rankDelta) -> rayLine board square fileDelta rankDelta)
        |> List.filter (not << List.isEmpty)

    let attackRaysForPiece board square piece : Square list list =
        match piece.Kind with
        | Pawn -> pawnRays square
        | Knight -> knightRays square
        | Bishop -> slidingRays board square bishopDirections
        | Rook -> slidingRays board square rookDirections
        | Queen -> slidingRays board square queenDirections
        | King -> kingRays square

    let attacksForPiece board square piece =
        attackRaysForPiece board square piece
        |> List.concat
        |> Set.ofList

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

    // One arrow per direction a piece can move, ending at the farthest square
    // it can see/attack along that ray.
    let enemyAttackArrows (board: BoardState) : (Square * Square) list =
        match enemyColor board with
        | None -> []
        | Some color ->
            board
            |> Map.toSeq
            |> Seq.collect (fun (square, piece) ->
                if piece.Color = color then
                    attackRaysForPiece board square piece
                    |> List.choose (List.tryLast >> Option.map (fun farthest -> square, farthest))
                    |> Seq.ofList
                else
                    Seq.empty)
            |> Seq.toList
