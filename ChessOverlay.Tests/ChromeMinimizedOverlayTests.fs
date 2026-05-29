namespace ChessOverlay.Tests

open Xunit
open ChessOverlay

/// Scenario: the Chrome tab strategy is active and the watched Chrome tab is
/// minimized (or otherwise not visible on screen). The overlay must not draw the
/// board outline or the detected piece labels over whatever is now on screen.
///
/// Two seams cover this end to end:
///   * ChromeBoardDetector.parseBoardReading — a minimized tab reports
///     document.hidden = true, so the DOM read yields no board (no pieces shown).
///   * ChromeOverlayVisibility.shouldClearOverlay — with a DOM reader active and
///     no board read, the controller clears the overlay (no board outline shown).
module ChromeMinimizedOverlayTests =
    let private chromeReading =
        {
            Board = TestHelpers.boardFromFen "8/8/8/8/8/8/4K3/8 w - - 0 1"
            Confidence = 1.0
            Candidates = Map.empty
            Strategy = "Chrome DOM"
        }

    [<Fact>]
    let ``minimized Chrome tab yields no board reading so no pieces are shown`` () =
        // The board element and its pieces are still in the DOM, but a minimized
        // tab reports document.hidden = true.
        let json =
            """{"id":1,"result":{"result":{"value":{"orientation":"white","hidden":true,"pieces":[{"piece":"wk","square":"51"},{"piece":"bk","square":"58"}]}}}}"""

        Assert.Equal(None, ChromeBoardDetector.parseBoardReading json)

    [<Fact>]
    let ``overlay is cleared when the Chrome DOM reader returns no board`` () =
        // A minimized tab produces no DOM reading; with a DOM reader active the
        // overlay must be cleared rather than left showing a stale board outline.
        Assert.True(ChromeOverlayVisibility.shouldClearOverlay true None)

    [<Fact>]
    let ``overlay is kept while the Chrome tab still reports a board`` () =
        Assert.False(ChromeOverlayVisibility.shouldClearOverlay true (Some chromeReading))

    [<Fact>]
    let ``overlay is not force-cleared when no DOM reader is active`` () =
        // Without a DOM (Chrome) reader the screen-capture path owns the overlay,
        // so a None reading must not trigger the Chrome clear behavior.
        Assert.False(ChromeOverlayVisibility.shouldClearOverlay false None)
