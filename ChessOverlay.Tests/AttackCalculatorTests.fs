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
        // knights on c5/e5 (rank=3) are both attacked and do not defend each
        // other, so both are hanging.
        let board = parse "8/3n4/8/2N1N3/8/8/8/8 w - - 0 1"
        let forks = AttackCalculator.enemyForks board

        Assert.Equal(1, forks.Length)
        let forker, forked = List.head forks
        Assert.Equal({ File = 3; Rank = 1 }, forker)
        Assert.Equal<Set<Square>>(set [ { File = 2; Rank = 3 }; { File = 4; Rank = 3 } ], forked)

    [<Fact>]
    let ``enemyForks reports a sliding piece forking along two rays`` () =
        // Black rook on d7 (file=3, rank=1): white rooks on its file (d3) and
        // rank (g7) are the farthest pieces it attacks along each ray.
        let board = parse "8/3r2R1/8/8/8/3R4/8/8 w - - 0 1"
        let forks = AttackCalculator.enemyForks board

        Assert.Equal(1, forks.Length)
        let forker, forked = List.head forks
        Assert.Equal({ File = 3; Rank = 1 }, forker)
        Assert.Equal<Set<Square>>(set [ { File = 3; Rank = 5 }; { File = 6; Rank = 1 } ], forked)

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
    let ``enemyForks ignores defended pieces`` () =
        // Black knight on d7 attacks both white rooks, but the rooks defend each
        // other along the 5th rank, so neither is hanging — not a real fork.
        let board = parse "8/3n4/8/2R1R3/8/8/8/8 w - - 0 1"
        Assert.Empty(AttackCalculator.enemyForks board)

    [<Fact>]
    let ``enemyForks counts only the undefended pieces in a mixed fork`` () =
        // Black knight on d7 attacks white knights on c5/e5 (undefended) plus a
        // white pawn on f6 that a white rook on f3 defends. The defended pawn is
        // excluded, leaving the two knights as the fork.
        let board = parse "8/3n4/5P2/2N1N3/8/5R2/8/8 w - - 0 1"
        let forks = AttackCalculator.enemyForks board

        Assert.Equal(1, forks.Length)
        let forker, forked = List.head forks
        Assert.Equal({ File = 3; Rank = 1 }, forker)
        Assert.Equal<Set<Square>>(set [ { File = 2; Rank = 3 }; { File = 4; Rank = 3 } ], forked)

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
        let board = parse "2r2r2/1n3kb1/p2p2p1/1p1p1nBp/1PqPN2P/2P3P1/Q4PB1/R1R3K1 w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.Contains(({ File = 4; Rank = 4 }, { File = 3; Rank = 2 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows reports Bonin Alburt royal fork from published puzzle FEN`` () =
        // W.T. Harvey's Bonin vs Alburt puzzle gives this FEN with solution
        // Nf5+, a fork of the defended black king on g7 and queen on e7.
        let board = parse "5b2/2r1q1k1/p2pQ1p1/P1pPp2p/4P3/2P1NR1P/6PK/8 w - - 1 0"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.Contains(({ File = 4; Rank = 5 }, { File = 5; Rank = 3 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows reports Capablanca Graham rook fork from published puzzle FEN`` () =
        // ChessWorld, Capablanca vs Graham, Puzzle ID 714: 1.Rxc6+ forks the
        // black king on c8 and queen on d6 from the published FEN.
        let board = parse "r1k5/p6p/2nqrp2/p2N4/3p4/5QP1/1P3P1P/R1R3K1 w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.Contains(({ File = 2; Rank = 7 }, { File = 2; Rank = 2 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows does not report Korchnoi Smirin setup retreat as an immediate fork`` () =
        // ChessWorld, Korchnoi vs Smirin, Puzzle ID 1563: 1.Nc1 is a real-game
        // setup move, but the premove overlay should only show immediate forks.
        let board = parse "r3r1k1/ppp3b1/3p3p/3P3Q/1qP2P2/3b1B1P/P3NPK1/4R2R w - - 0 1"
        let arrows = AttackCalculator.friendlyForkMoveArrows board

        Assert.DoesNotContain(({ File = 4; Rank = 6 }, { File = 2; Rank = 7 }), arrows)

    [<Fact>]
    let ``friendlyForkMoveArrows ignores defended equal-value targets`` () =
        // From f3 the knight would attack both black knights, but the bishop on
        // f6 defends both and the exchange is not favorable.
        let board = parse "8/8/5b2/8/3n3n/8/3N4/8 w - - 0 1"

        Assert.Empty(AttackCalculator.friendlyForkMoveArrows board)
