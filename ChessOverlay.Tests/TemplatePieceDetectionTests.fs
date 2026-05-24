namespace ChessOverlay.Tests

open System
open System.Drawing
open System.IO
open Xunit
open ChessOverlay
open ChessOverlay.Tests

module TemplatePieceDetectionTests =
    let private tempRoot () =
        TestHelpers.tempRoot "ChessOverlayTemplateTests"

    let private saveBitmap path color =
        use bitmap = new Bitmap(6, 6)

        use graphics = Graphics.FromImage(bitmap)
        graphics.Clear color
        bitmap.Save(path)

    let private patternedSquare size =
        let bitmap = new Bitmap(size, size)

        for x in 0 .. size - 1 do
            for y in 0 .. size - 1 do
                let color =
                    if x = y || x + y = size - 1 then
                        Color.White
                    else
                        Color.Black

                bitmap.SetPixel(x, y, color)

        bitmap

    let private disposeTemplates (templates: Map<Piece, Bitmap>) =
        templates
        |> Map.iter (fun _ bitmap -> bitmap.Dispose())

    [<Fact>]
    let ``Template loader parses short and long piece names`` () =
        let root = tempRoot ()

        saveBitmap (Path.Combine(root, "wk.png")) Color.White
        saveBitmap (Path.Combine(root, "black_queen.bmp")) Color.Black
        saveBitmap (Path.Combine(root, "unknown.png")) Color.Red

        let templates = PieceTemplates.loadFromDirectory root

        try
            Assert.Equal(2, templates.Count)
            Assert.True(templates.ContainsKey { Color = Bottom; Kind = King })
            Assert.True(templates.ContainsKey { Color = Top; Kind = Queen })
        finally
            disposeTemplates templates

    [<Fact>]
    let ``Template reader matches a patterned piece square`` () =
        use templateBitmap = patternedSquare 16
        use boardBitmap = new Bitmap(128, 128)

        use graphics = Graphics.FromImage(boardBitmap)
        graphics.Clear Color.Black
        graphics.DrawImage(templateBitmap, Rectangle(0, 0, 16, 16))

        let piece = { Color = Bottom; Kind = King }
        let templates = Map.ofList [ piece, templateBitmap ]
        let reader = TemplateBoardReader(templates, 0.9) :> IBoardReader
        let geometry = { Left = 0; Top = 0; Size = 128 }

        match reader.Read(boardBitmap, geometry) with
        | Some reading ->
            Assert.Equal(0.5, reading.Confidence)
            Assert.Equal(Some piece, BoardState.tryPieceAt { File = 0; Rank = 0 } reading.Board)
        | None -> failwith "Expected template reader output."

    [<Fact>]
    let ``Template reader reports no board when no templates are configured`` () =
        use bitmap = new Bitmap(20, 20)
        let reader = TemplateBoardReader(Map.empty, 0.9) :> IBoardReader

        let result =
            reader.Read(bitmap, { Left = 0; Top = 0; Size = 20 })

        Assert.True(result.IsNone)
