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

    let private slidingAttacks board square directions =
        directions
        |> List.map (fun (fileDelta, rankDelta) -> rayAttacks board square fileDelta rankDelta)
        |> Set.unionMany

    let attacksForPiece board square piece =
        match piece.Kind with
        | Pawn -> pawnAttacks square
        | Knight -> knightAttacks square
        | Bishop -> slidingAttacks board square bishopDirections
        | Rook -> slidingAttacks board square rookDirections
        | Queen -> slidingAttacks board square (bishopDirections @ rookDirections)
        | King -> kingAttacks square

    let enemyAttackedSquares (board: BoardState) =
        board
        |> Map.toSeq
        |> Seq.choose (fun (square, piece) ->
            if piece.Color = Top then
                Some(attacksForPiece board square piece)
            else
                None)
        |> fun attacks ->
            if Seq.isEmpty attacks then
                Set.empty
            else
                Set.unionMany attacks
