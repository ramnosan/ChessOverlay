namespace ChessOverlay.Tests

open Xunit
open ChessOverlay

module ChromeBoardSelectionDialogTests =

    let private board title url size : ChromeBoardDetector.DetectedBoard =
        { Tab =
            { Id = title
              Title = title
              Url = url
              WebSocketUrl = "ws://localhost:9222/devtools/page/test" }
          Geometry = { Left = 10; Top = 20; Size = size } }

    [<Fact>]
    let ``viewForResult shows detection errors without selectable boards`` () =
        let view = ChromeBoardSelection.viewForResult (Error "Chrome unavailable")

        Assert.Equal("Chrome unavailable", view.Status)
        Assert.Empty(view.Boards)
        Assert.Empty(view.Items)
        Assert.False(view.SelectSingleBoard)

    [<Fact>]
    let ``viewForResult shows an empty-board message`` () =
        let view = ChromeBoardSelection.viewForResult (Ok [])

        Assert.Contains("No chess boards detected in Chrome", view.Status)
        Assert.Empty(view.Boards)
        Assert.Empty(view.Items)
        Assert.False(view.SelectSingleBoard)

    [<Fact>]
    let ``viewForResult formats and auto-selects one detected board`` () =
        let detected = board "Lichess game" "https://lichess.org/abc" 640

        let view = ChromeBoardSelection.viewForResult (Ok [ detected ])

        Assert.Equal("Found 1 chess board(s). Select one and click Use.", view.Status)
        Assert.Equal<ChromeBoardDetector.DetectedBoard list>([ detected ], view.Boards)
        Assert.Equal("Lichess game  —  lichess.org  (640 px)", Assert.Single(view.Items))
        Assert.True(view.SelectSingleBoard)

    [<Fact>]
    let ``viewForResult keeps invalid tab URLs readable`` () =
        let detected = board "Local game" "not a url" 512

        let view = ChromeBoardSelection.viewForResult (Ok [ detected ])

        Assert.Equal("Local game  —  not a url  (512 px)", Assert.Single(view.Items))

    [<Fact>]
    let ``viewForResult does not auto-select multiple boards`` () =
        let first = board "Game 1" "https://www.chess.com/game/live/1" 600
        let second = board "Game 2" "https://lichess.org/2" 640

        let view = ChromeBoardSelection.viewForResult (Ok [ first; second ])

        Assert.Equal("Found 2 chess board(s). Select one and click Use.", view.Status)
        Assert.Equal(2, view.Items.Length)
        Assert.False(view.SelectSingleBoard)

    [<Fact>]
    let ``viewForException shows the detection failure`` () =
        let view = ChromeBoardSelection.viewForException (System.InvalidOperationException "boom")

        Assert.Equal("Detection failed: boom", view.Status)
        Assert.Empty(view.Boards)
        Assert.Empty(view.Items)
        Assert.False(view.SelectSingleBoard)
