namespace ChessOverlay.Tests

open Xunit
open ChessOverlay

module AttackCalculatorTests =
    let private parse fen =
        match Fen.parseBoard fen with
        | Ok board -> board
        | Error message -> failwith message

    [<Fact>]
    let ``Top pawns attack diagonally toward increasing ranks`` () =
        let board = parse "8/8/3p4/8/8/8/8/8 w - - 0 1"
        let attacks = AttackCalculator.enemyAttackedSquares board

        Assert.Contains({ File = 2; Rank = 3 }, attacks)
        Assert.Contains({ File = 4; Rank = 3 }, attacks)
        Assert.DoesNotContain({ File = 3; Rank = 3 }, attacks)

    [<Fact>]
    let ``Sliding pieces include blocker square and stop behind it`` () =
        let board = parse "8/8/3q4/8/3P4/8/8/8 w - - 0 1"
        let attacks = AttackCalculator.enemyAttackedSquares board

        Assert.Contains({ File = 3; Rank = 4 }, attacks)
        Assert.DoesNotContain({ File = 3; Rank = 5 }, attacks)

    [<Fact>]
    let ``Bottom player's pieces are ignored when calculating enemy attacks`` () =
        // Black sits on top (enemy); white is the bottom player.
        let board = parse "n7/8/8/8/3N4/8/8/8 w - - 0 1"
        let attacks = AttackCalculator.enemyAttackedSquares board

        Assert.Contains({ File = 1; Rank = 2 }, attacks)
        Assert.Contains({ File = 2; Rank = 1 }, attacks)
        Assert.DoesNotContain({ File = 2; Rank = 2 }, attacks)
        Assert.DoesNotContain({ File = 4; Rank = 6 }, attacks)

    [<Fact>]
    let ``A white enemy on top is highlighted when the user plays black`` () =
        // User is black: white sits on the top ranks, black on the bottom.
        let board = parse "3N4/8/8/8/8/8/8/7n w - - 0 1"
        let attacks = AttackCalculator.enemyAttackedSquares board

        Assert.Contains({ File = 2; Rank = 2 }, attacks)
        Assert.Contains({ File = 4; Rank = 2 }, attacks)
        Assert.DoesNotContain({ File = 6; Rank = 5 }, attacks)

    [<Fact>]
    let ``enemyColor picks the colour sitting on top`` () =
        Assert.Equal(Some White, AttackCalculator.enemyColor (parse "3N4/8/8/8/8/8/8/7n w - - 0 1"))
        Assert.Equal(Some Black, AttackCalculator.enemyColor (parse "n7/8/8/8/3N4/8/8/8 w - - 0 1"))
        Assert.Equal(None, AttackCalculator.enemyColor (parse "8/8/8/8/8/8/8/8 w - - 0 1"))

    [<Fact>]
    let ``Knight attacks stay inside the board`` () =
        let board = parse "n7/8/8/8/8/8/8/8 w - - 0 1"
        let attacks = AttackCalculator.enemyAttackedSquares board

        Assert.Equal<Set<Square>>(set [ { File = 1; Rank = 2 }; { File = 2; Rank = 1 } ], attacks)

    [<Fact>]
    let ``King attacks all adjacent squares from center`` () =
        let board = parse "8/8/8/3k4/8/8/8/8 w - - 0 1"
        let attacks = AttackCalculator.enemyAttackedSquares board

        Assert.Equal(8, attacks.Count)
        Assert.Contains({ File = 2; Rank = 2 }, attacks)
        Assert.Contains({ File = 4; Rank = 4 }, attacks)

    [<Fact>]
    let ``Bishop rook and queen use their sliding directions`` () =
        let bishopBoard = parse "8/8/8/3b4/8/8/8/8 w - - 0 1"
        let rookBoard = parse "8/8/8/3r4/8/8/8/8 w - - 0 1"
        let queenBoard = parse "8/8/8/3q4/8/8/8/8 w - - 0 1"

        Assert.Contains({ File = 0; Rank = 0 }, AttackCalculator.enemyAttackedSquares bishopBoard)
        Assert.DoesNotContain({ File = 3; Rank = 0 }, AttackCalculator.enemyAttackedSquares bishopBoard)
        Assert.Contains({ File = 3; Rank = 0 }, AttackCalculator.enemyAttackedSquares rookBoard)
        Assert.DoesNotContain({ File = 0; Rank = 0 }, AttackCalculator.enemyAttackedSquares rookBoard)
        Assert.Contains({ File = 0; Rank = 0 }, AttackCalculator.enemyAttackedSquares queenBoard)
        Assert.Contains({ File = 3; Rank = 0 }, AttackCalculator.enemyAttackedSquares queenBoard)

    [<Fact>]
    let ``enemyAttackArrows returns empty list for empty board`` () =
        let board = parse "8/8/8/8/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.enemyAttackArrows board)

    [<Fact>]
    let ``enemyAttackArrows produces one arrow per ray for a rook`` () =
        // Black rook on d6 (file=3, rank=5 in screen coords) — 4 rays
        let board = parse "8/8/8/8/8/3r4/8/8 w - - 0 1"
        let arrows = AttackCalculator.enemyAttackArrows board
        Assert.Equal(4, arrows.Length)
        Assert.All(arrows, fun (src, _) -> Assert.Equal({ File = 3; Rank = 5 }, src))

    [<Fact>]
    let ``enemyAttackArrows arrow ends at farthest reachable square`` () =
        // Black rook on a8 (file=0, rank=0): east ray ends at file 7, south ray ends at rank 7
        let board = parse "r7/8/8/8/8/8/8/8 w - - 0 1"
        let arrows = AttackCalculator.enemyAttackArrows board
        Assert.Contains(({ File = 0; Rank = 0 }, { File = 7; Rank = 0 }), arrows)
        Assert.Contains(({ File = 0; Rank = 0 }, { File = 0; Rank = 7 }), arrows)

    [<Fact>]
    let ``enemyAttackArrows does not include friendly pieces`` () =
        // Black rook on top (rank=0, enemy); white rook on bottom (rank=7, friendly)
        let board = parse "r7/8/8/8/8/8/8/R7 w - - 0 1"
        let arrows = AttackCalculator.enemyAttackArrows board
        Assert.All(arrows, fun (src, _) -> Assert.Equal({ File = 0; Rank = 0 }, src))

    [<Fact>]
    let ``enemyAttackArrows knight produces arrows to each reachable square`` () =
        // Black knight on a8 (file=0, rank=0): 2 reachable squares from corner
        let board = parse "n7/8/8/8/8/8/8/8 w - - 0 1"
        let arrows = AttackCalculator.enemyAttackArrows board
        Assert.Equal(2, arrows.Length)
        Assert.All(arrows, fun (src, _) -> Assert.Equal({ File = 0; Rank = 0 }, src))

    [<Fact>]
    let ``enemyForks reports a knight attacking two friendly pieces`` () =
        // Black knight on d7 (file=3, rank=1) sits on top (enemy); two white
        // bishops on c5/e5 (rank=3) are both attacked and cannot defend each
        // other, so both are hanging. Bishops are used so no white piece can
        // reach the black knight and trigger the fork-validity filter.
        let board = parse "8/3n4/8/2B1B3/8/8/8/8 w - - 0 1"
        let forks = AttackCalculator.enemyForks board

        Assert.Equal(1, forks.Length)
        let forker, forked = List.head forks
        Assert.Equal({ File = 3; Rank = 1 }, forker)
        Assert.Equal<Set<Square>>(set [ { File = 2; Rank = 3 }; { File = 4; Rank = 3 } ], forked)

    [<Fact>]
    let ``enemyForks reports a sliding piece forking along two rays`` () =
        // Black bishop on d8 (file=3, rank=0): white rooks on e7 (file=4, rank=1)
        // and b6 (file=1, rank=2) are the first pieces it sees on each diagonal
        // ray. White rooks cannot reach d8 via file or rank, so the bishop is not
        // capturable and the fork is valid.
        let board = parse "3b4/4R3/1R6/8/8/8/8/8 w - - 0 1"
        let forks = AttackCalculator.enemyForks board

        Assert.Equal(1, forks.Length)
        let forker, forked = List.head forks
        Assert.Equal({ File = 3; Rank = 0 }, forker)
        Assert.Equal<Set<Square>>(set [ { File = 4; Rank = 1 }; { File = 1; Rank = 2 } ], forked)

    [<Fact>]
    let ``enemyForks ignores a piece attacking only one friendly piece`` () =
        // Black knight on d7 with a single white rook in range — an attack, not a fork.
        let board = parse "8/3n4/8/2R5/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.enemyForks board)

    [<Fact>]
    let ``enemyForks ignores forks made by the friendly bottom player`` () =
        // White knight on d2 (file=3, rank=6) forks two black pawns on c4/e4,
        // but only enemy (top) forks are reported, so the result is empty.
        let board = parse "8/8/8/8/2p1p3/8/3N4/8 w - - 0 1"
        Assert.Empty(AttackCalculator.enemyForks board)

    [<Fact>]
    let ``enemyForks returns empty for an empty board`` () =
        let board = parse "8/8/8/8/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.enemyForks board)

    [<Fact>]
    let ``enemyForks ignores mutually defended equal value targets`` () =
        // enemyForks reports only forks against hanging or profitably attacked
        // pieces. These rooks defend each other along the 5th rank, so Nxc5 or
        // Nxe5 would be recaptured by the other rook.
        let board = parse "8/3n4/8/2R1R3/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.enemyForks board)

    [<Fact>]
    let ``enemyForks counts only the undefended pieces in a mixed fork`` () =
        // Black knight on d7 (file=3, rank=1) attacks white bishops on c5/e5
        // (undefended) plus a white rook on f6 (file=5, rank=2) defended by a
        // white rook on f1 (file=5, rank=7). The defended rook is excluded, leaving
        // the two bishops as the fork. Bishops are used so no white piece can reach
        // d7 and trigger the fork-validity filter.
        let board = parse "8/3n4/5R2/2B1B3/8/8/8/5R2 w - - 0 1"
        let forks = AttackCalculator.enemyForks board

        Assert.Equal(1, forks.Length)
        let forker, forked = List.head forks
        Assert.Equal({ File = 3; Rank = 1 }, forker)
        Assert.Equal<Set<Square>>(set [ { File = 2; Rank = 3 }; { File = 4; Rank = 3 } ], forked)

    // fork-validity-filter-001
    [<Fact>]
    let ``enemyForks does not highlight fork when the forking piece can be captured`` () =
        // Black knight on d7 (file=3, rank=1) attacks two undefended white knights
        // on c5/e5, but the white knight on c5 can immediately take the black knight.
        // Resolving the fork by capturing the forking piece, so the fork is not shown.
        let board = parse "8/3n4/8/2N1N3/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.enemyForks board)

    // fork-validity-filter-002
    [<Fact>]
    let ``enemyForks highlights fork when the forking piece cannot be captured`` () =
        // Black knight on d7 (file=3, rank=1) attacks white bishops on c5/e5.
        // Bishops cannot reach d7, so the fork is a genuine threat.
        let board = parse "8/3n4/8/2B1B3/8/8/8/8 w - - 0 1"
        let forks = AttackCalculator.enemyForks board
        Assert.Equal(1, forks.Length)
        let forker, _ = List.head forks
        Assert.Equal({ File = 3; Rank = 1 }, forker)

    [<Fact>]
    let ``hangingSquares returns empty for an empty board`` () =
        let board = parse "8/8/8/8/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.hangingSquares board)

    [<Fact>]
    let ``An undefended friendly piece attacked by the enemy is hanging`` () =
        // Black rook on d8 (screen rank=0, file=3) attacks down the d-file.
        // White rook on d4 (screen rank=4, file=3) is the first piece it hits and has no defender.
        let board = parse "3r4/8/8/8/3R4/8/8/8 w - - 0 1"
        let hanging = AttackCalculator.hangingSquares board

        Assert.Equal<Set<Square>>(set [ { File = 3; Rank = 4 } ], hanging)

    [<Fact>]
    let ``A friendly piece attacked but defended by another friendly piece is not hanging`` () =
        // Black rook on d8 attacks white rook on d5 (screen rank=3, file=3).
        // White rook on a5 (rank=3, file=0) defends d5 along the same rank.
        let board = parse "3r4/8/8/R2R4/8/8/8/8 w - - 0 1"

        Assert.Empty(AttackCalculator.hangingSquares board)

    [<Fact>]
    let ``A friendly piece defended only by the king is not hanging when the attacker is loose`` () =
        // Black queen on d8 attacks the white rook on d3. The white king on e2
        // can recapture on d3 if the queen takes because no black piece protects d3.
        let board = parse "3q4/8/8/8/8/3R4/4K3/8 w - - 0 1"

        Assert.Empty(AttackCalculator.hangingSquares board)

    [<Fact>]
    let ``A friendly piece defended only by the king is hanging when a protected attacker can take it`` () =
        // The white king defends the rook on d3, but black's queen and rook both
        // attack d3. After either capture, the other black piece protects d3, so
        // the king cannot legally recapture.
        let board = parse "3q4/8/8/8/8/r2R4/4K3/8 w - - 0 1"
        let hanging = AttackCalculator.hangingSquares board

        Assert.Equal<Set<Square>>(set [ { File = 3; Rank = 5 } ], hanging)

    [<Fact>]
    let ``A defended friendly piece is hanging when attacked by a lower-value enemy piece`` () =
        // Black knight on d7 attacks the white queen on e5. The queen is defended
        // by the rook on a5, but trading a knight for a queen still wins material.
        let board = parse "8/3n4/8/R3Q3/8/8/8/8 w - - 0 1"
        let hanging = AttackCalculator.hangingSquares board

        Assert.Equal<Set<Square>>(set [ { File = 4; Rank = 3 } ], hanging)

    [<Fact>]
    let ``A friendly piece that is not attacked is not hanging`` () =
        // Black knight on a8 (rank=0, file=0) attacks only b6 and c7 in screen coords.
        // White rook on d1 (rank=7, file=3) is nowhere near its reach.
        let board = parse "n7/8/8/8/8/8/8/3R4 w - - 0 1"

        Assert.Empty(AttackCalculator.hangingSquares board)

    [<Fact>]
    let ``All hanging pieces are reported when multiple exist`` () =
        // Black rook on a8 attacks white rook on a6 (rank=2, file=0).
        // Black rook on h8 attacks white rook on h5 (rank=3, file=7).
        // The two white rooks are on different ranks and cannot defend each other.
        let board = parse "r6r/8/R7/7R/8/8/8/8 w - - 0 1"
        let hanging = AttackCalculator.hangingSquares board

        Assert.Equal<Set<Square>>(set [ { File = 0; Rank = 2 }; { File = 7; Rank = 3 } ], hanging)

    [<Fact>]
    let ``A friendly pawn defends a piece one rank above it`` () =
        // Black rook on d8 attacks white rook on d3 (screen rank=5, file=3).
        // White pawn on e2 (screen rank=6, file=4) attacks (file-1, rank-1) = (3,5) and covers d3.
        let board = parse "3r4/8/8/8/8/3R4/4P3/8 w - - 0 1"

        Assert.Empty(AttackCalculator.hangingSquares board)

    [<Fact>]
    let ``A friendly piece unprotected by a pawn is hanging even when a pawn is nearby`` () =
        // Same as above but the pawn is shifted one file away and no longer covers d3.
        // White pawn on f2 (screen rank=6, file=5) attacks (4,5) and (6,5) — not (3,5).
        let board = parse "3r4/8/8/8/8/3R4/5P2/8 w - - 0 1"
        let hanging = AttackCalculator.hangingSquares board

        Assert.Equal<Set<Square>>(set [ { File = 3; Rank = 5 } ], hanging)

    [<Fact>]
    let ``hangingSquares works when white is the enemy and black is the friendly player`` () =
        // White rook on d8 sits on top (enemy); black rook on d3 (screen rank=5, file=3) is
        // the friendly piece. It is attacked and undefended so it should be reported.
        let board = parse "3R4/8/8/8/8/3r4/8/8 w - - 0 1"
        let hanging = AttackCalculator.hangingSquares board

        Assert.Equal<Set<Square>>(set [ { File = 3; Rank = 5 } ], hanging)

    [<Fact>]
    let ``enemyHangingSquares returns empty for an empty board`` () =
        let board = parse "8/8/8/8/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.enemyHangingSquares board)

    [<Fact>]
    let ``An undefended enemy piece attacked by the friendly player is hanging`` () =
        // White rook on d1 attacks upward to the black rook on d5. The black
        // rook is not defended by another black piece, so it is available.
        let board = parse "8/8/8/3r4/8/8/8/3R4 w - - 0 1"
        let hanging = AttackCalculator.enemyHangingSquares board

        Assert.Equal<Set<Square>>(set [ { File = 3; Rank = 3 } ], hanging)

    [<Fact>]
    let ``A defended enemy piece attacked by the friendly player is not hanging`` () =
        // The black rook on a5 defends the black rook on d5, so the d5 rook is
        // not hanging even though the bottom player's rook attacks it.
        let board = parse "8/8/8/r2r4/8/8/8/3R4 w - - 0 1"

        Assert.Empty(AttackCalculator.enemyHangingSquares board)

    [<Fact>]
    let ``A defended enemy piece is hanging when attacked by a lower-value friendly piece`` () =
        // The black queen on e4 is defended by a black rook on a4, but the
        // bottom player's knight attacks it, so the material trade is favorable.
        let board = parse "8/8/8/8/r3q3/8/3N4/8 w - - 0 1"
        let hanging = AttackCalculator.enemyHangingSquares board

        Assert.Equal<Set<Square>>(set [ { File = 4; Rank = 4 } ], hanging)

    [<Fact>]
    let ``friendlyForkMoveArrows returns empty for an empty board`` () =
        let board = parse "8/8/8/8/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.friendlyForkMoveArrows board)

    [<Fact>]
    let ``friendlyForkMoveArrows reports a player move that forks two enemy pieces`` () =
        // The bottom player's knight on d2 can move to f3, where it attacks
        // the undefended black bishop on g5 and rook on d4.
        let board = parse "8/8/8/6b1/3r4/8/3N4/8 w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.Contains(({ File = 3; Rank = 6 }, { File = 5; Rank = 5 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows reports a real game royal fork with defended targets`` () =
        // ChessWorld, Papas vs Oreopoulos, Puzzle ID 160: 1.Nxd6+ forks the
        // black king on f7 and queen on c4 from the published FEN.
        // However the black knight on f5 can capture the white knight on d6;
        // this is a multi-move tactic, not a one-move fork.
        let board = parse "2r2r2/1n3kb1/p2p2p1/1p1p1nBp/1PqPN2P/2P3P1/Q4PB1/R1R3K1 w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 4; Rank = 4 }, { File = 3; Rank = 2 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows reports Bonin Alburt royal fork from published puzzle FEN`` () =
        // W.T. Harvey's Bonin vs Alburt puzzle gives this FEN with solution
        // Nf5+, a fork of the defended black king on g7 and queen on e7.
        // However the black pawn on g6 can capture the knight on f5;
        // this is a multi-move tactic, not a one-move fork.
        let board = parse "5b2/2r1q1k1/p2pQ1p1/P1pPp2p/4P3/2P1NR1P/6PK/8 w - - 1 0"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 4; Rank = 5 }, { File = 5; Rank = 3 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows reports Capablanca Graham rook fork from published puzzle FEN`` () =
        // ChessWorld, Capablanca vs Graham, Puzzle ID 714: 1.Rxc6+ forks the
        // black king on c8 and queen on d6 from the published FEN.
        // However the black queen on d6 can capture the rook on c6;
        // the rook captured a knight on c6 but loses the exchange.
        let board = parse "r1k5/p6p/2nqrp2/p2N4/3p4/5QP1/1P3P1P/R1R3K1 w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 2; Rank = 7 }, { File = 2; Rank = 2 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows does not report Korchnoi Smirin setup retreat as an immediate fork`` () =
        // ChessWorld, Korchnoi vs Smirin, Puzzle ID 1563: 1.Nc1 is a real-game
        // setup move, but the premove overlay should only show immediate forks.
        let board = parse "r3r1k1/ppp3b1/3p3p/3P3Q/1qP2P2/3b1B1P/P3NPK1/4R2R w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 4; Rank = 6 }, { File = 2; Rank = 7 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows does not report pseudo forks by absolutely pinned pieces`` () =
        // Absolute pins cannot be premoved as fork tactics: moving this knight
        // from e2 to f4 would attack d5 and h5, but it exposes the white king
        // on e1 to the black rook on e8.
        let board = parse "k3r3/8/8/3r3q/8/8/4N3/4K3 w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 4; Rank = 6 }, { File = 5; Rank = 4 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows ignores defended equal-value targets`` () =
        // From f3 the knight would attack both black knights, but the bishop on
        // f6 defends both and the exchange is not favorable.
        let board = parse "8/8/5b2/8/3n3n/8/3N4/8 w - - 0 1"

        Assert.Empty(AttackCalculator.friendlyForkMoveArrows board)

    [<Fact>]
    let ``friendlyForkMoveArrows does not flag a fork when all targets are defended (1.e4 d5 2.Qh5 Nf6 3.Qf3 dxe4 4.Qf4 Bg4)`` () =
        // Real game position after 4...Bg4. The system was incorrectly reporting
        // a fork next move even though every apparent target is defended (e.g.
        // d2 is protected by the White queen on f4).
        let board = parse "rn1qkb1r/ppp1pppp/5n2/8/4pQb1/8/PPPP1PPP/RNB1KBNR w KQkq - 2 5"
        Assert.Empty(AttackCalculator.friendlyForkMoveArrows board)

    [<Fact>]
    let ``friendlyForkMoveArrows does not flag the same Bg4 position from black's board orientation`` () =
        // Same position as above, but in the screen orientation used when the
        // user plays black: White pieces are on the top and Black pieces are on
        // the bottom. The apparent queen move Qxg4 must not become a fork hint.
        let board = parse "RNBK1BNR/PPP1PPPP/8/1bQp4/8/2n5/pppp1ppp/r1bkq1nr w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.Empty(arrows)
        Assert.DoesNotContain(({ File = 4; Rank = 7 }, { File = 4; Rank = 1 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows still rejects the flipped Bg4 queen check when the knight defender is missed`` () =
        // Same screen position, but without the black knight on c6. This matches
        // a realistic partial board read: even if a defender is missed, Qe7+
        // is not a fork because the white king can take the unprotected queen.
        let board = parse "RNBK1BNR/PPP1PPPP/8/1bQp4/8/8/pppp1ppp/r1bkq1nr w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 4; Rank = 7 }, { File = 4; Rank = 1 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows rejects queen checks where the king can capture the forking queen`` () =
        // Minimal version of the same bug: the bottom queen can move next to
        // the top king and attack a rook, but the queen lands undefended and the
        // checked king can capture it.
        let board = parse "3K4/8/3R4/8/8/8/8/4q3 w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 4; Rank = 7 }, { File = 4; Rank = 1 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows rejects loose queen forks capturable by a bishop`` () =
        // Qe4 would attack both loose rooks, but the bishop on b7 can simply
        // take the queen on e4. The premove overlay should not suggest that as
        // a fork.
        let board = parse "8/1b6/8/8/r6r/8/8/4Q3 w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 4; Rank = 7 }, { File = 4; Rank = 4 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows rejects defended queen forks when a lower value piece can take the queen`` () =
        // The bishop on h1 protects e4, but Bxe4 would still trade a bishop for
        // the queen, so the fork is not sound enough to show as a premove hint.
        let board = parse "8/1b6/8/8/r6r/8/8/4Q2B w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 4; Rank = 7 }, { File = 4; Rank = 4 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows rejects checking queen forks capturable by a bishop`` () =
        // Screenshot regression: Qh7-d7+ would attack the king and rook, but
        // the bishop on c8 can take the queen on d7 and win material.
        let board = parse "rnb1kr2/ppp4Q/8/4N3/4B3/2P1B1P1/P1P2P1P/R3K2R w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 7; Rank = 1 }, { File = 3; Rank = 1 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows allows queen checks when the forking queen is protected`` () =
        // Same geometry as above, but a bottom bishop protects e7. The checked
        // king can no longer safely capture the queen, so the queen fork remains
        // a valid premove hint.
        let board = parse "3K4/8/3R4/8/7b/8/8/4q3 w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.Contains(({ File = 4; Rank = 7 }, { File = 4; Rank = 1 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows rejects false fork when queen captures the forking knight on an undefended square`` () =
        // Caro-Kann after 7.Nxd5: Nc7+ attacks king and rook, but the black
        // queen on d8 can simply capture the undefended knight on c7.
        let board = parse "r1bqkbnr/pp3ppp/8/3Nn3/8/4B3/PPP2PPP/RN1QKBNR b - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 3; Rank = 3 }, { File = 2; Rank = 1 }), arrows)

    [<Fact>]
    let ``enemyForkMoveArrows returns empty for an empty board`` () =
        let board = parse "8/8/8/8/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.enemyForkMoveArrows board)

    [<Fact>]
    let ``enemyForkMoveArrows reports an enemy move that forks two friendly pieces`` () =
        // The top (enemy) white knight on d8 (file=3,rank=0) can move to e6
        // (file=4,rank=2), where it attacks the undefended black pawns on d5
        // (file=3,rank=4) and f5 (file=5,rank=4).
        let board = parse "3N4/8/8/8/3p1p2/8/8/8 w - - 0 1"
        let arrows = AttackCalculator.enemyForkMoveArrows board

        Assert.Contains(({ File = 3; Rank = 0 }, { File = 4; Rank = 2 }), arrows)

    [<Fact>]
    let ``enemyForkMoveArrows does not report fork when enemy piece is undefended and capturable`` () =
        // The enemy knight can reach a forking square but a friendly rook on the
        // same file can immediately capture it, so the fork attempt is unsound.
        // Enemy (white) knight on d8 (file=3,rank=0) moves to e6 (file=4,rank=2)
        // but a black rook on e5 (file=4,rank=3) can recapture it immediately.
        let board = parse "3N4/8/8/8/3p1p2/4r3/8/8 w - - 0 1"
        let arrows = AttackCalculator.enemyForkMoveArrows board

        Assert.DoesNotContain(({ File = 3; Rank = 0 }, { File = 4; Rank = 2 }), arrows)
