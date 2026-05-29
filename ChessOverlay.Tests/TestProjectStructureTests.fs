namespace ChessOverlay.Tests

open System
open System.IO
open System.Xml.Linq
open Xunit

module TestProjectStructureTests =
    let private testsRoot () =
        Path.Combine(TestHelpers.repositoryRoot (), "ChessOverlay.Tests")

    let private compileIncludes () =
        let projectFile = Path.Combine(testsRoot (), "ChessOverlay.Tests.fsproj")

        XDocument.Load(projectFile).Descendants(XName.Get "Compile")
        |> Seq.choose (fun element ->
            match element.Attribute(XName.Get "Include") with
            | null -> None
            | attribute -> Some attribute.Value)
        |> Seq.toList

    let private normalizePath (path: string) =
        path.Replace('\\', '/')

    let private isInPropertyTestsFolder (path: string) =
        normalizePath path
        |> fun normalized -> normalized.StartsWith("PropertyTests/", StringComparison.OrdinalIgnoreCase)

    let private sourceText (relativePath: string) =
        File.ReadAllText(Path.Combine(testsRoot (), relativePath))

    let private isPropertyTestFile (relativePath: string) =
        let lines =
            sourceText relativePath
            |> fun text -> text.Split([| "\r\n"; "\n" |], StringSplitOptions.None)

        let hasFsCheckOpen =
            lines |> Array.exists (fun line -> line.Trim() = "open FsCheck")

        let hasPropertyCheck =
            lines |> Array.exists (fun line -> line.TrimStart().StartsWith("Check.One", StringComparison.Ordinal))

        hasFsCheckOpen && hasPropertyCheck

    [<Fact>]
    let ``PropertyTests folder contains only property test files with Property in the name`` () =
        let propertyFolderFiles =
            compileIncludes ()
            |> List.filter isInPropertyTestsFolder

        Assert.NotEmpty propertyFolderFiles

        Assert.All(
            propertyFolderFiles,
            fun relativePath ->
                Assert.Contains("Property", Path.GetFileNameWithoutExtension(relativePath), StringComparison.Ordinal)
                Assert.True(isPropertyTestFile relativePath, $"{relativePath} is in PropertyTests but is not a property test."))

    [<Fact>]
    let ``Property test files live in PropertyTests folder and contain Property in the name`` () =
        let propertyTestFiles =
            compileIncludes ()
            |> List.filter isPropertyTestFile

        Assert.NotEmpty propertyTestFiles

        Assert.All(
            propertyTestFiles,
            fun relativePath ->
                Assert.True(isInPropertyTestsFolder relativePath, $"{relativePath} is a property test outside PropertyTests.")
                Assert.Contains("Property", Path.GetFileNameWithoutExtension(relativePath), StringComparison.Ordinal))
