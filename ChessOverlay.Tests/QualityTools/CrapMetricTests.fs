namespace ChessOverlay.Tests

open System
open System.IO
open Xunit
open ChessOverlay.Quality

module CrapMetricTests =
    let private tempRoot () =
        let root = Path.Combine(Path.GetTempPath(), "ChessOverlayQualityTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        root

    [<Fact>]
    let ``CRAP formula matches the reference metric`` () =
        let score = CrapMetric.crapScore 4 0.5

        Assert.Equal(6.0, score, 3)

    [<Fact>]
    let ``FSharp spans include branch complexity`` () =
        let root = tempRoot ()
        let app = Path.Combine(root, "ChessOverlay")
        Directory.CreateDirectory(app) |> ignore
        let file = Path.Combine(app, "Sample.fs")

        let source =
            String.Join(
                Environment.NewLine,
                [
                    "namespace Sample"
                    ""
                    "module Demo ="
                    "    let choose value ="
                    "        if value > 0 then"
                    "            \"positive\""
                    "        elif value = 0 then"
                    "            \"zero\""
                    "        else"
                    "            \"negative\""
                    ""
                    "    let simple value ="
                    "        value + 1"
                ])

        File.WriteAllText(file, source)

        let spans = CrapMetric.findFunctionSpans root file

        let choose = spans |> List.find (fun span -> span.Name = "choose")
        let simple = spans |> List.find (fun span -> span.Name = "simple")

        Assert.True(choose.CyclomaticComplexity > simple.CyclomaticComplexity)
        Assert.Equal(1, simple.CyclomaticComplexity)

    [<Fact>]
    let ``Coverage is calculated inside the detected function span`` () =
        let coverage =
            Map.ofList [
                "ChessOverlay/Sample.fs",
                Map.ofList [
                    3, true
                    4, true
                    5, false
                    6, false
                ]
            ]

        let span =
            {
                File = "ChessOverlay/Sample.fs"
                Name = "sample"
                StartLine = 3
                EndLine = 6
                CyclomaticComplexity = 2
            }

        Assert.Equal(Some 0.5, CrapMetric.coverageForSpan coverage span)

    [<Fact>]
    let ``Pipe operators at line start are not counted as match arms`` () =
        let root = tempRoot ()
        let app = Path.Combine(root, "ChessOverlay")
        Directory.CreateDirectory(app) |> ignore
        let file = Path.Combine(app, "Sample.fs")

        let source =
            String.Join(
                Environment.NewLine,
                [
                    "namespace Sample"
                    ""
                    "module Demo ="
                    "    let pipeline xs ="
                    "        xs"
                    "        |> List.map id"
                    "        |> List.filter (fun _ -> true)"
                ])

        File.WriteAllText(file, source)

        let spans = CrapMetric.findFunctionSpans root file
        let pipeline = spans |> List.find (fun span -> span.Name = "pipeline")

        Assert.Equal(1, pipeline.CyclomaticComplexity)
