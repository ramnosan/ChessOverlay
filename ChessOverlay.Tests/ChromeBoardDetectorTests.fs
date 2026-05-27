namespace ChessOverlay.Tests

open System.Text.Json
open System.Windows.Forms
open Xunit
open ChessOverlay

module ChromeBoardDetectorTests =

    let private vsLeft = SystemInformation.VirtualScreen.Left
    let private vsTop = SystemInformation.VirtualScreen.Top

    let private parseTab (json: string) =
        use doc = JsonDocument.Parse json
        ChromeBoardDetector.tryParseTab doc.RootElement

    [<Fact>]
    let ``tryParseTab builds a tab from a complete page entry`` () =
        let tab =
            parseTab
                """{"type":"page","id":"AB12","title":"lichess","url":"https://lichess.org/x","webSocketDebuggerUrl":"ws://localhost:9222/devtools/page/AB12"}"""

        let expected: ChromeBoardDetector.ChromeTab =
            { Id = "AB12"
              Title = "lichess"
              Url = "https://lichess.org/x"
              WebSocketUrl = "ws://localhost:9222/devtools/page/AB12" }

        Assert.Equal(Some expected, tab)

    [<Fact>]
    let ``tryParseTab ignores entries whose type is not page`` () =
        let json =
            """{"type":"background_page","id":"BG","title":"bg","url":"chrome://x","webSocketDebuggerUrl":"ws://localhost:9222/devtools/page/BG"}"""

        Assert.Equal(None, parseTab json)

    [<Fact>]
    let ``tryParseTab returns None when the debugger URL is missing`` () =
        let json =
            """{"type":"page","id":"NoWs","title":"t","url":"https://chess.com"}"""

        Assert.Equal(None, parseTab json)

    [<Fact>]
    let ``parseFen extracts the FEN string from a CDP evaluate response`` () =
        let fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
        let json = sprintf """{"id":1,"result":{"result":{"type":"string","value":"%s"}}}""" fen
        Assert.Equal(Some fen, ChromeBoardDetector.parseFen json)

    [<Fact>]
    let ``parseFen returns None when CDP result is null (no chess board)`` () =
        Assert.Equal(None, ChromeBoardDetector.parseFen """{"id":1,"result":{"result":{"type":"null"}}}""")

    [<Fact>]
    let ``parseFen returns None when the value is not a string`` () =
        Assert.Equal(None, ChromeBoardDetector.parseFen """{"id":1,"result":{"result":{"value":12345}}}""")

    [<Fact>]
    let ``parseFen returns None when the result field is missing`` () =
        Assert.Equal(None, ChromeBoardDetector.parseFen """{"id":1}""")

    [<Fact>]
    let ``parseFen returns None for malformed JSON`` () =
        Assert.Equal(None, ChromeBoardDetector.parseFen "not json")
        Assert.Equal(None, ChromeBoardDetector.parseFen "")

    [<Fact>]
    let ``parseBoardReading extracts chess com piece classes for white orientation`` () =
        let json =
            """{"id":1,"result":{"result":{"value":{"orientation":"white","pieces":[{"piece":"wk","square":"51"},{"piece":"bp","square":"48"}]}}}}"""

        match ChromeBoardDetector.parseBoardReading json with
        | Some reading ->
            Assert.Equal("Chrome DOM", reading.Strategy)
            Assert.Equal(1.0, reading.Confidence)
            Assert.Equal(Some { Color = White; Kind = King }, BoardState.tryPieceAt { File = 4; Rank = 7 } reading.Board)
            Assert.Equal(Some { Color = Black; Kind = Pawn }, BoardState.tryPieceAt { File = 3; Rank = 0 } reading.Board)
        | None -> failwith "Expected Chrome DOM reading."

    [<Fact>]
    let ``parseBoardReading flips chess com squares for black orientation`` () =
        let json =
            """{"id":1,"result":{"result":{"value":{"orientation":"black","pieces":[{"piece":"wk","square":"51"},{"piece":"bp","square":"48"}]}}}}"""

        match ChromeBoardDetector.parseBoardReading json with
        | Some reading ->
            Assert.Equal(Some { Color = White; Kind = King }, BoardState.tryPieceAt { File = 3; Rank = 0 } reading.Board)
            Assert.Equal(Some { Color = Black; Kind = Pawn }, BoardState.tryPieceAt { File = 4; Rank = 7 } reading.Board)
        | None -> failwith "Expected Chrome DOM reading."

    [<Fact>]
    let ``parseBoardReading returns None for null or empty results`` () =
        Assert.Equal(None, ChromeBoardDetector.parseBoardReading """{"id":1,"result":{"result":{"type":"null"}}}""")
        Assert.Equal(None, ChromeBoardDetector.parseBoardReading """{"id":1,"result":{"result":{"value":{"orientation":"white","pieces":[]}}}}""")

    [<Fact>]
    let ``parseGeometry returns None for malformed JSON`` () =
        Assert.Equal(None, ChromeBoardDetector.parseGeometry "not json")
        Assert.Equal(None, ChromeBoardDetector.parseGeometry "")
        Assert.Equal(None, ChromeBoardDetector.parseGeometry "{")

    [<Fact>]
    let ``parseGeometry returns None when CDP result is null (no board)`` () =
        let json = """{"id":1,"result":{"result":{"type":"null"}}}"""
        Assert.Equal(None, ChromeBoardDetector.parseGeometry json)

    [<Fact>]
    let ``parseGeometry returns None when board is too small`` () =
        let json = """{"id":1,"result":{"result":{"value":{"left":100,"top":200,"width":40,"height":40}}}}"""
        Assert.Equal(None, ChromeBoardDetector.parseGeometry json)

    [<Fact>]
    let ``parseGeometry returns None when width is zero`` () =
        let json = """{"id":1,"result":{"result":{"value":{"left":100,"top":200,"width":0,"height":640}}}}"""
        Assert.Equal(None, ChromeBoardDetector.parseGeometry json)

    [<Fact>]
    let ``parseGeometry returns None when result field is missing`` () =
        Assert.Equal(None, ChromeBoardDetector.parseGeometry """{"id":1}""")

    [<Fact>]
    let ``parseGeometry returns None when value field is missing`` () =
        let json = """{"id":1,"result":{"result":{"type":"object"}}}"""
        Assert.Equal(None, ChromeBoardDetector.parseGeometry json)

    [<Fact>]
    let ``parseGeometry returns valid geometry from CDP evaluate response`` () =
        let json =
            """{"id":1,"result":{"result":{"value":{"left":100,"top":200,"width":640,"height":640}}}}"""

        let expected =
            Some { Left = 100 - vsLeft; Top = 200 - vsTop; Size = 640 }

        Assert.Equal(expected, ChromeBoardDetector.parseGeometry json)

    [<Fact>]
    let ``parseGeometry adjusts coordinates by virtual screen origin`` () =
        let json =
            sprintf
                """{"id":1,"result":{"result":{"value":{"left":%d,"top":%d,"width":800,"height":800}}}}"""
                (300 + vsLeft) (150 + vsTop)

        let expected =
            Some { Left = 300; Top = 150; Size = 800 }

        Assert.Equal(expected, ChromeBoardDetector.parseGeometry json)

    [<Fact>]
    let ``parseGeometry uses min of width and height as size`` () =
        let json =
            """{"id":1,"result":{"result":{"value":{"left":100,"top":200,"width":700,"height":400}}}}"""

        let expected =
            Some { Left = 100 - vsLeft; Top = 200 - vsTop; Size = 400 }

        Assert.Equal(expected, ChromeBoardDetector.parseGeometry json)

    [<Fact>]
    let ``parseGeometry returns None when height field is missing`` () =
        let json =
            """{"id":1,"result":{"result":{"value":{"left":100,"top":200,"width":640}}}}"""

        Assert.Equal(None, ChromeBoardDetector.parseGeometry json)

    [<Fact>]
    let ``detectBoards returns error when Chrome debugging is not available`` () =
        let result = ChromeBoardDetector.detectBoards () |> Async.RunSynchronously
        Assert.True(
            match result with
            | Error msg -> msg.StartsWith("Chrome remote debugging is not available")
            | Ok _ -> true)

    [<Fact>]
    let ``tryDetectBoardInTab returns None when WebSocket connection fails`` () =
        let tab : ChromeBoardDetector.ChromeTab =
            { Id = "test-tab"
              Title = "test"
              Url = "about:blank"
              WebSocketUrl = "ws://localhost:19222/devtools/page/FAKE" }

        let result =
            ChromeBoardDetector.tryDetectBoardInTab tab
            |> Async.RunSynchronously

        Assert.Equal(None, result)
