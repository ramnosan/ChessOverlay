namespace ChessOverlay.Tests

open System
open System.IO
open Xunit
open ChessOverlay.Quality

module ArchitectureViewTests =
    let private tempRoot () =
        let root = Path.Combine(Path.GetTempPath(), "ChessOverlayArchTests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        root

    let private writeFile root relativePath (lines: string list) =
        let path = Path.Combine(root, relativePath)
        Directory.CreateDirectory(Path.GetDirectoryName(path)) |> ignore
        File.WriteAllText(path, String.Join(Environment.NewLine, lines))
        path

    let private writeProject root relativePath includes =
        let compileItems =
            includes
            |> List.map (fun sourceFile -> sprintf "    <Compile Include=\"%s\" />" sourceFile)
            |> String.concat Environment.NewLine

        writeFile
            root
            relativePath
            [
                "<Project Sdk=\"Microsoft.NET.Sdk\">"
                "  <ItemGroup>"
                compileItems
                "  </ItemGroup>"
                "</Project>"
            ]
        |> ignore

    let private options root =
        {
            Root = root
            Inputs = []
            Format = "text"
            OutputPath = None
        }

    [<Fact>]
    let ``Architecture analyzer discovers app-specific layers and symbol dependencies`` () =
        let root = tempRoot ()

        writeProject root "ChessOverlay/ChessOverlay.fsproj" [ "Domain.fs"; "OverlayController.fs"; "Program.fs" ]

        writeFile
            root
            "ChessOverlay/Domain.fs"
            [
                "namespace ChessOverlay"
                "type BoardGeometry = { Left: int; Top: int; Size: int }"
            ]
        |> ignore

        writeFile
            root
            "ChessOverlay/OverlayController.fs"
            [
                "namespace ChessOverlay"
                "type OverlayController(geometry: BoardGeometry) ="
                "    member _.Geometry = geometry"
            ]
        |> ignore

        writeFile
            root
            "ChessOverlay/Program.fs"
            [
                "namespace ChessOverlay"
                "module Program ="
                "    let start geometry = OverlayController(geometry)"
            ]
        |> ignore

        let model = ArchitectureView.analyze (options root)

        let program = model.Modules |> List.find (fun moduleInfo -> moduleInfo.Name = "Program")
        let domain = model.Modules |> List.find (fun moduleInfo -> moduleInfo.Name = "Domain")

        Assert.Equal("Composition Root", program.Layer)
        Assert.Equal("Domain Model", domain.Layer)

        Assert.Contains(
            model.Dependencies,
            fun dependency -> dependency.From.EndsWith("Program.fs") && dependency.To.EndsWith("OverlayController.fs"))

        Assert.Contains(
            model.Dependencies,
            fun dependency -> dependency.From.EndsWith("OverlayController.fs") && dependency.To.EndsWith("Domain.fs"))

    [<Fact>]
    let ``Architecture analyzer marks cycle edges`` () =
        let root = tempRoot ()

        writeProject root "ChessOverlay/ChessOverlay.fsproj" [ "Alpha.fs"; "Beta.fs" ]

        writeFile
            root
            "ChessOverlay/Alpha.fs"
            [
                "namespace ChessOverlay"
                "type Alpha = { Beta: Beta option }"
            ]
        |> ignore

        writeFile
            root
            "ChessOverlay/Beta.fs"
            [
                "namespace ChessOverlay"
                "type Beta = { Alpha: Alpha option }"
            ]
        |> ignore

        let model = ArchitectureView.analyze (options root)

        Assert.NotEmpty model.Cycles
        Assert.All(model.Dependencies, fun dependency -> Assert.True(dependency.IsCycle))

    [<Fact>]
    let ``Architecture HTML includes modules and dependency metadata`` () =
        let root = tempRoot ()

        writeProject root "ChessOverlay/ChessOverlay.fsproj" [ "Domain.fs"; "Program.fs" ]

        writeFile
            root
            "ChessOverlay/Domain.fs"
            [
                "namespace ChessOverlay"
                "type BoardGeometry = { Size: int }"
            ]
        |> ignore

        writeFile
            root
            "ChessOverlay/Program.fs"
            [
                "namespace ChessOverlay"
                "module Program ="
                "    let geometry = { BoardGeometry.Size = 8 }"
            ]
        |> ignore

        let model = ArchitectureView.analyze (options root)
        let html = ArchitectureView.renderHtml model

        Assert.Contains("ChessOverlay Architecture", html)
        Assert.Contains("Composition Root", html)
        Assert.Contains("BoardGeometry", html)
