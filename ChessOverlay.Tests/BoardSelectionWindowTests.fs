namespace ChessOverlay.Tests

open System.Drawing
open Xunit
open ChessOverlay

module BoardSelectionWindowTests =
    [<Fact>]
    let ``Selection from points creates a square in drag direction`` () =
        let selection =
            BoardSelectionGeometry.selectionFromPoints (Point(100, 100)) (Point(40, 30))

        Assert.Equal(Rectangle(40, 40, 60, 60), selection)

    [<Fact>]
    let ``Current selection requires drag start and current point`` () =
        let selection =
            BoardSelectionGeometry.currentSelection (Some(Point(10, 20))) (Some(Point(50, 80)))

        Assert.Equal(Some(Rectangle(10, 20, 40, 40)), selection)
        Assert.True(BoardSelectionGeometry.currentSelection None (Some(Point(50, 80))) |> Option.isNone)

    [<Fact>]
    let ``Capture geometry keeps selected square coordinates`` () =
        let geometry =
            BoardSelectionGeometry.clientToCaptureGeometry (Rectangle(12, 34, 120, 120))

        Assert.Equal({ Left = 12; Top = 34; Size = 120 }, geometry)
