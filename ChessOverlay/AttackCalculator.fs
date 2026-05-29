namespace ChessOverlay

module AttackCalculator =
    // A ray is the ordered list of squares a piece reaches in a single
    // direction, nearest first. Non-sliding pieces produce one single-square
    // ray per target. Keeping the per-direction grouping lets us draw a single
    // arrow to the farthest reachable square instead of one per square.
    let private rayLine board square fileDelta rankDelta =
        let rec loop acc file rank =
            match Squares.tryCreate file rank with
            | None -> List.rev acc
            | Some target ->
                let nextAcc = target :: acc

                if BoardState.occupied target board then
                    List.rev nextAcc
                else
                    loop nextAcc (file + fileDelta) (rank + rankDelta)

        loop [] (square.File + fileDelta) (square.Rank + rankDelta)

    let private steps deltas square =
        deltas
        |> List.choose (fun (fileDelta, rankDelta) ->
            Squares.tryCreate (square.File + fileDelta) (square.Rank + rankDelta))
        |> List.map List.singleton

    let private pawnRaysWithDir pawnRankDelta square =
        steps [ -1, pawnRankDelta; 1, pawnRankDelta ] square

    // Only the enemy (top) player's attacks are ever highlighted, and the top
    // player moves down the screen regardless of colour, so pawns always attack
    // toward increasing ranks here.
    let private oppositeColor color =
        if color = White then Black else White

    let private piecesOfColor board color =
        board
        |> Map.toSeq
        |> Seq.filter (fun (_, piece) -> piece.Color = color)

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

    let private slidingDirectionMap =
        Map.ofList [ Bishop, bishopDirections; Rook, rookDirections; Queen, queenDirections ]

    // Non-pawn stepping pieces map straight to a ray generator. Pawns stay
    // separate because their rays depend on the runtime pawnRankDelta.
    let private stepRayMap =
        Map.ofList [ Knight, knightRays; King, kingRays ]

    let private stepRaysForPiece square piece pawnRankDelta =
        match piece.Kind with
        | Pawn -> pawnRaysWithDir pawnRankDelta square
        | kind ->
            Map.tryFind kind stepRayMap
            |> Option.map (fun rays -> rays square)
            |> Option.defaultValue []

    let attackRaysForPieceWithDir board square piece pawnRankDelta : Square list list =
        match Map.tryFind piece.Kind slidingDirectionMap with
        | Some directions -> slidingRays board square directions
        | None -> stepRaysForPiece square piece pawnRankDelta

    let attackRaysForPiece board square piece : Square list list =
        attackRaysForPieceWithDir board square piece 1

    let attacksForPieceWithDir board square piece pawnRankDelta =
        attackRaysForPieceWithDir board square piece pawnRankDelta
        |> List.concat
        |> Set.ofList

    let attacksForPiece board square piece =
        attacksForPieceWithDir board square piece 1

    let attackedSquaresByColorWithDir (board: BoardState) color pawnRankDelta =
        piecesOfColor board color
        |> Seq.map (fun (square, piece) -> attacksForPieceWithDir board square piece pawnRankDelta)
        |> fun attacks ->
            if Seq.isEmpty attacks then
                Set.empty
            else
                Set.unionMany attacks

    let attackedSquaresByColor (board: BoardState) color =
        attackedSquaresByColorWithDir board color 1

    let private meanRank color (board: BoardState) =
        let ranks =
            piecesOfColor board color
            |> Seq.map (fun (square, _) -> float square.Rank)
            |> Seq.toList

        match ranks with
        | [] -> None
        | _ -> Some(List.average ranks)

    // The top player is always the enemy, but they may be either colour (the
    // user plays both sides).
    let enemyColor (board: BoardState) : PieceColor option =
        let whiteRank = meanRank White board
        let blackRank = meanRank Black board

        Option.map2 (fun wr br -> if wr <= br then White else Black) whiteRank blackRank
        |> Option.orElseWith (fun () ->
            if whiteRank.IsSome then
                Some White
            elif blackRank.IsSome then
                Some Black
            else
                None)

    let enemyAttackedSquares (board: BoardState) =
        match enemyColor board with
        | Some color -> attackedSquaresByColor board color
        | None -> Set.empty

    let private pieceArrows board square piece =
        [ for ray in attackRaysForPiece board square piece do
              match List.tryLast ray with
              | Some far -> square, far
              | None -> () ]

    let private withEnemyColor board (f: PieceColor -> (Square * Square) list) =
        enemyColor board |> Option.map f |> Option.defaultValue []

    // One arrow per direction a piece can move, ending at the farthest square
    // it can see/attack along that ray.
    let enemyAttackArrows (board: BoardState) : (Square * Square) list =
        withEnemyColor board (fun color ->
            piecesOfColor board color
            |> Seq.collect (fun (square, piece) -> pieceArrows board square piece)
            |> Seq.toList)

    let private pieceValueMap =
        Map.ofList [ Pawn, 1; Knight, 3; Bishop, 3; Rook, 5; Queen, 9; King, 100 ]

    let private pieceValue piece = Map.find piece.Kind pieceValueMap

    let private piecesAttackingSquare board attackerColor attackerPawnRankDelta square =
        [ for KeyValue(attackerSquare, attacker) in board do
              if attacker.Color = attackerColor
                 && Set.contains square (attacksForPieceWithDir board attackerSquare attacker attackerPawnRankDelta) then
                  attackerSquare, attacker ]

    let private lowerValueAttacker board attackerColor attackerPawnRankDelta square targetPiece =
        piecesAttackingSquare board attackerColor attackerPawnRankDelta square
        |> Seq.exists (fun (_, attacker) -> pieceValue targetPiece > pieceValue attacker)

    let private protectedAfterCapture board attackerColor attackerPawnRankDelta targetSquare attackerSquare attacker =
        let boardAfterCapture =
            board
            |> Map.remove attackerSquare
            |> Map.add targetSquare attacker

        Set.contains targetSquare (attackedSquaresByColorWithDir boardAfterCapture attackerColor attackerPawnRankDelta)

    let private defendedAgainstAttackers board targetColor targetPawnRankDelta attackerColor attackerPawnRankDelta square =
        let defenders = piecesAttackingSquare board targetColor targetPawnRankDelta square

        if defenders |> Seq.exists (fun (_, defender) -> defender.Kind <> King) then
            true
        elif defenders |> Seq.exists (fun (_, defender) -> defender.Kind = King) then
            piecesAttackingSquare board attackerColor attackerPawnRankDelta square
            |> Seq.forall (fun (attackerSquare, attacker) ->
                not (protectedAfterCapture board attackerColor attackerPawnRankDelta square attackerSquare attacker))
        else
            false

    let private hangingSquaresFor board targetColor targetPawnRankDelta attackerColor attackerPawnRankDelta =
        let attackerAttacks = attackedSquaresByColorWithDir board attackerColor attackerPawnRankDelta

        [ for KeyValue(square, piece) in board do
              if piece.Color = targetColor
                 && Set.contains square attackerAttacks
                 && (not (defendedAgainstAttackers board targetColor targetPawnRankDelta attackerColor attackerPawnRankDelta square)
                     || lowerValueAttacker board attackerColor attackerPawnRankDelta square piece) then
                  square ]
        |> Set.ofList

    let private withEnemyColors board f =
        enemyColor board
        |> Option.map (fun enemy -> f enemy (if enemy = White then Black else White))
        |> Option.defaultValue Set.empty

    // Friendly (bottom) pieces that are attacked by the enemy and either not
    // defended by another friendly piece or attacked by a lower-value piece.
    let hangingSquares (board: BoardState) : Set<Square> =
        withEnemyColors board (fun enemy friendly -> hangingSquaresFor board friendly -1 enemy 1)

    // Enemy (top) pieces that are attacked by the friendly bottom player and
    // either undefended or attacked by a lower-value friendly piece.
    let enemyHangingSquares (board: BoardState) : Set<Square> =
        withEnemyColors board (fun enemy friendly -> hangingSquaresFor board enemy 1 friendly -1)

    let private isOwnPiece board color square =
        match BoardState.tryPieceAt square board with
        | Some piece -> piece.Color = color
        | None -> false

    let private isOpponentPiece board color square =
        isOwnPiece board (oppositeColor color) square

    let private tryPawnAdvance board square pawnRankDelta steps =
        Squares.tryCreate square.File (square.Rank + steps * pawnRankDelta)
        |> Option.filter (fun target -> not (BoardState.occupied target board))

    let private pawnCaptureSquares board square color pawnRankDelta =
        [ for fileDelta in [ -1; 1 ] do
              match Squares.tryCreate (square.File + fileDelta) (square.Rank + pawnRankDelta) with
              | Some target when isOpponentPiece board color target -> target
              | _ -> () ]

    let private pawnAdvanceSquares board square pawnRankDelta =
        let oneStep = tryPawnAdvance board square pawnRankDelta 1

        let twoStep =
            if oneStep.IsSome && square.Rank = (7 - pawnRankDelta * 5) / 2 then
                tryPawnAdvance board square pawnRankDelta 2
            else
                None

        [ oneStep; twoStep ] |> List.choose id

    let private pawnMoveSquares board square color pawnRankDelta =
        pawnAdvanceSquares board square pawnRankDelta
        |> List.append (pawnCaptureSquares board square color pawnRankDelta)

    let private moveSquaresForPiece board square piece pawnRankDelta =
        match piece.Kind with
        | Pawn -> pawnMoveSquares board square piece.Color pawnRankDelta
        | _ ->
            attackRaysForPieceWithDir board square piece pawnRankDelta
            |> List.concat
            |> List.filter (not << isOwnPiece board piece.Color)

    let private kingSquare board color =
        let mutable found = None

        for KeyValue(square, piece) in board do
            if found.IsNone && piece.Color = color && piece.Kind = King then
                found <- Some square

        found

    let private kingSafeAfterMove board fromSquare toSquare piece kingSide attackerColor attackerPawnDelta =
        let movedBoard = board |> Map.remove fromSquare |> Map.add toSquare piece

        match kingSquare movedBoard kingSide with
        | None -> true
        | Some king ->
            not (Set.contains king (attackedSquaresByColorWithDir movedBoard attackerColor attackerPawnDelta))

    let private kingSafeAfterCapture boardAfterCapture kingSide attackerColor attackerPawnDelta =
        match kingSquare boardAfterCapture kingSide with
        | None -> true
        | Some king -> not (Set.contains king (attackedSquaresByColorWithDir boardAfterCapture attackerColor attackerPawnDelta))

    let private legalCaptureWinsForForker movedBoard toSquare piece capturerColor forkerColor forkerPawnDelta forkerDefendsSquare (attackerSquare, attacker) =
        let boardAfterCapture =
            movedBoard
            |> Map.remove attackerSquare
            |> Map.add toSquare attacker

        kingSafeAfterCapture boardAfterCapture capturerColor forkerColor forkerPawnDelta
        && (not forkerDefendsSquare || pieceValue attacker <= pieceValue piece)

    let private pieceIsSafeAfterMove board fromSquare toSquare piece forkerColor forkerPawnDelta capturerColor capturerPawnDelta =
        let movedBoard = board |> Map.remove fromSquare |> Map.add toSquare piece

        piecesAttackingSquare movedBoard capturerColor capturerPawnDelta toSquare
        |> Seq.exists (
            legalCaptureWinsForForker
                movedBoard
                toSquare
                piece
                capturerColor
                forkerColor
                forkerPawnDelta
                (defendedAgainstAttackers movedBoard forkerColor forkerPawnDelta capturerColor capturerPawnDelta toSquare)
        )
        |> not

    let private isVulnerableForcedTarget movedBoard piece targetColor targetPawnRankDelta attackerPawnRankDelta (square, targetPiece) =
        targetPiece.Color = targetColor
        && (targetPiece.Kind = King
            || not (defendedAgainstAttackers movedBoard targetColor targetPawnRankDelta piece.Color attackerPawnRankDelta square)
            || lowerValueAttacker movedBoard piece.Color attackerPawnRankDelta square targetPiece)

    let private forkedVulnerableTargetsAfterMove board fromSquare toSquare piece targetColor targetPawnRankDelta attackerPawnRankDelta =
        let movedBoard = board |> Map.remove fromSquare |> Map.add toSquare piece

        let vulnerableTargets =
            movedBoard
            |> Map.toSeq
            |> Seq.filter (isVulnerableForcedTarget movedBoard piece targetColor targetPawnRankDelta attackerPawnRankDelta)
            |> Seq.map fst
            |> Set.ofSeq

        Set.intersect (attacksForPieceWithDir movedBoard toSquare piece attackerPawnRankDelta) vulnerableTargets

    let private forkMoveArrowsForPiece board fromSquare piece ownColor ownPawnDelta opponentColor opponentPawnDelta =
        [ for toSquare in moveSquaresForPiece board fromSquare piece ownPawnDelta do
              if kingSafeAfterMove board fromSquare toSquare piece ownColor opponentColor opponentPawnDelta
                 && pieceIsSafeAfterMove board fromSquare toSquare piece ownColor ownPawnDelta opponentColor opponentPawnDelta then
                  let forked = forkedVulnerableTargetsAfterMove board fromSquare toSquare piece opponentColor opponentPawnDelta ownPawnDelta

                  if Set.count forked >= 2 then
                      fromSquare, toSquare ]

    let private forkMoveArrowsCore board pickOwn =
        match enemyColor board with
        | None -> []
        | Some enemy ->
            let friendly = oppositeColor enemy
            let ownColor = pickOwn enemy friendly
            let opponentColor = oppositeColor ownColor
            let ownPawnDelta = if ownColor = enemy then 1 else -1
            let opponentPawnDelta = -ownPawnDelta

            [ for KeyValue(fromSquare, piece) in board do
                  if piece.Color = ownColor then
                      yield! forkMoveArrowsForPiece board fromSquare piece ownColor ownPawnDelta opponentColor opponentPawnDelta ]
            |> List.distinct

    let friendlyForkMoveArrows (board: BoardState) : (Square * Square) list =
        forkMoveArrowsCore board (fun _ f -> f)

    // A fork is a single enemy (top) piece that attacks two or more friendly
    // pieces that are hanging or profitably attacked. The forking piece must
    // also survive the same material-aware safety check used for fork moves:
    // if the best capture of the forker wins material, the fork is not shown.
    let private forkForPiece board vulnerableTargets square piece =
        let forked = Set.intersect (attacksForPiece board square piece) vulnerableTargets
        if Set.count forked >= 2 then Some(square, forked) else None

    let private enemyForksForColor board enemy =
        let friendly = oppositeColor enemy
        let vulnerableFriendly = hangingSquaresFor board friendly -1 enemy 1

        piecesOfColor board enemy
        |> Seq.filter (fun (square, piece) -> pieceIsSafeAfterMove board square square piece enemy 1 friendly -1)
        |> Seq.choose (fun (square, piece) -> forkForPiece board vulnerableFriendly square piece)
        |> Seq.toList

    let enemyForks (board: BoardState) : (Square * Set<Square>) list =
        enemyColor board
        |> Option.map (enemyForksForColor board)
        |> Option.defaultValue []

    let enemyForkMoveArrows (board: BoardState) : (Square * Square) list =
        forkMoveArrowsCore board (fun e _ -> e)
