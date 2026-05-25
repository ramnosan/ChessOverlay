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
