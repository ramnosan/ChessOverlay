namespace ChessOverlay.Tests

open System.Windows.Forms
open Xunit
open ChessOverlay

module ChromeBoardDetectorTests =

    let private vsLeft = SystemInformation.VirtualScreen.Left
    let private vsTop = SystemInformation.VirtualScreen.Top

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
