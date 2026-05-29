namespace ChessOverlay.Quality

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq

type FunctionSpan =
    {
        File: string
        Name: string
        StartLine: int
        EndLine: int
        CyclomaticComplexity: int
    }

type FunctionScore =
    {
        Span: FunctionSpan
        Coverage: float option
        Crap: float option
    }

type QualityOptions =
    {
        Root: string
        Inputs: string list
        ChangedOnly: bool
        CoveragePath: string option
        Threshold: float
    }

type CoverageGenerationResult =
    {
        CoveragePath: string
        Output: string
        Error: string
    }

module CrapMetric =
    let private functionPattern =
        Regex(@"^\s*(?:let\s+(?:(?:rec|inline|private|internal|public|mutable)\s+)*(?!(?:rec|inline|private|internal|public|mutable)\b)((?:``[^`]+``)|[A-Za-z_][A-Za-z0-9_']*)(?![A-Za-z0-9_'])\s+(?!=)|member\s+(?:(?:private|internal|public)\s+)*[^\s.]+\.((?:``[^`]+``)|[A-Za-z_][A-Za-z0-9_']*)(?![A-Za-z0-9_'])(?:\s*\(|\s+(?!=)))", RegexOptions.Compiled)

    let private structuralBoundaryPattern =
        Regex(@"^\s*(do|interface|override|member|type|module)\b", RegexOptions.Compiled)

    let private inlineCommentPattern = Regex(@"//.*$", RegexOptions.Compiled)
    let private quotedNamePattern = Regex("^``(.+)``$", RegexOptions.Compiled)
    let private excludeCoverageAttribute = "ExcludeFromCodeCoverage"

    let private normalizePath (path: string) =
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

    let private relativePath (root: string) (path: string) =
        Path.GetRelativePath(root, path).Replace('\\', '/')

    let private indentation (line: string) =
        line
        |> Seq.takeWhile (fun value -> value = ' ' || value = '\t')
        |> Seq.sumBy (fun value -> if value = '\t' then 4 else 1)

    let stripComments (line: string) =
        inlineCommentPattern.Replace(line, "")

    let stripStrings (line: string) =
        let mutable inString = false
        let mutable escaped = false
        let chars = line.ToCharArray()

        for index in 0 .. chars.Length - 1 do
            let value = chars[index]

            if inString then
                chars[index] <- ' '

                if escaped then
                    escaped <- false
                elif value = '\\' then
                    escaped <- true
                elif value = '"' then
                    inString <- false
            elif value = '"' then
                chars[index] <- ' '
                inString <- true

        String(chars)

    let codeOnly line =
        line |> stripComments |> stripStrings

    let private hasExcludeAttribute (line: string) =
        line.Contains(excludeCoverageAttribute, StringComparison.Ordinal)

    let private previousCodeLine (indexedLines: (int * string) list) lineNumber =
        indexedLines
        |> List.rev
        |> List.tryFind (fun (currentLineNumber, line) ->
            currentLineNumber < lineNumber && not (String.IsNullOrWhiteSpace line))

    let private isExcludedByAttribute indexedLines startLine startIndent =
        let directlyExcluded =
            previousCodeLine indexedLines startLine
            |> Option.exists (snd >> hasExcludeAttribute)

        let enclosingExcluded =
            indexedLines
            |> List.rev
            |> List.tryPick (fun (lineNumber, line) ->
                if lineNumber >= startLine || String.IsNullOrWhiteSpace line then
                    None
                elif indentation line < startIndent && structuralBoundaryPattern.IsMatch line then
                    Some(
                        previousCodeLine indexedLines lineNumber
                        |> Option.exists (snd >> hasExcludeAttribute))
                else
                    None)
            |> Option.defaultValue false

        directlyExcluded || enclosingExcluded

    let private decisionCount (lines: string list) =
        let countRegex pattern (line: string) =
            Regex.Matches(line, pattern).Count

        lines
        |> List.sumBy (fun line ->
            let code = codeOnly line
            countRegex @"\bif\b" code
            + countRegex @"\belif\b" code
            + countRegex @"\bfor\b" code
            + countRegex @"\bwhile\b" code
            + countRegex @"\bmatch\b" code
            + countRegex @"\btry\b" code
            + countRegex @"\bwith\b" code
            + countRegex @"\|\|" code
            + countRegex @"&&" code
            + if Regex.IsMatch(code, @"^\s*\|(?![>|])") then 1 else 0)

    let private displayName (name: string) =
        let quoted = quotedNamePattern.Match(name)

        if quoted.Success then
            quoted.Groups[1].Value
        else
            name

    let findFunctionSpans (root: string) (file: string) =
        let fullPath = normalizePath file
        let lines = File.ReadAllLines fullPath |> Array.toList

        let indexedLines =
            lines
            |> List.mapi (fun index line -> index + 1, line)

        let starts =
            indexedLines
            |> List.choose (fun (lineNumber, line) ->
                let matchResult = functionPattern.Match(line)

                if matchResult.Success && indentation line <= 4 then
                    let name =
                        if matchResult.Groups[1].Success then
                            matchResult.Groups[1].Value
                        else
                            matchResult.Groups[2].Value

                    Some(lineNumber, indentation line, displayName name)
                else
                    None)

        let includedStarts =
            starts
            |> List.filter (fun (startLine, startIndent, _) ->
                not (isExcludedByAttribute indexedLines startLine startIndent))

        includedStarts
        |> List.mapi (fun index (startLine, startIndent, name) ->
            let endLine =
                let nextFunction =
                    starts
                    |> List.filter (fun (lineNumber, _, _) -> lineNumber > startLine)
                    |> List.tryFind (fun (_, indent, _) -> indent <= startIndent)
                    |> Option.map (fun (lineNumber, _, _) -> lineNumber)

                let nextStructuralBoundary =
                    indexedLines
                    |> List.skip startLine
                    |> List.tryFind (fun (_, line) ->
                        not (String.IsNullOrWhiteSpace line)
                        && indentation line <= startIndent
                        && structuralBoundaryPattern.IsMatch line)
                    |> Option.map fst

                [ nextFunction; nextStructuralBoundary ]
                |> List.choose id
                |> List.sort
                |> List.tryHead
                |> Option.map (fun lineNumber -> lineNumber - 1)
                |> Option.defaultValue lines.Length

            let body =
                lines
                |> List.skip (startLine - 1)
                |> List.take (endLine - startLine + 1)

            {
                File = relativePath (normalizePath root) fullPath
                Name = name
                StartLine = startLine
                EndLine = endLine
                CyclomaticComplexity = max 1 (1 + decisionCount body)
            })

    let private coverageKey (root: string) (path: string) =
        let fullPath =
            if Path.IsPathRooted path then
                path
            else
                Path.Combine(root, path)

        relativePath (normalizePath root) (normalizePath fullPath)

    let readCoberturaLineCoverage (root: string) (coveragePath: string) =
        if String.IsNullOrWhiteSpace coveragePath || not (File.Exists coveragePath) then
            Map.empty
        else
            let document = XDocument.Load coveragePath

            let lineMaps =
                document.Descendants(XName.Get "class")
                |> Seq.choose (fun classElement ->
                let fileName = classElement.Attribute(XName.Get "filename")

                if isNull fileName then
                    None
                else
                    let lines =
                        classElement.Descendants(XName.Get "line")
                        |> Seq.choose (fun lineElement ->
                            let number = lineElement.Attribute(XName.Get "number")
                            let hits = lineElement.Attribute(XName.Get "hits")

                            if isNull number || isNull hits then
                                None
                            else
                                match Int32.TryParse(number.Value), Int32.TryParse(hits.Value) with
                                | (true, lineNumber), (true, hitCount) -> Some(lineNumber, hitCount > 0)
                                | _ -> None)
                        |> Map.ofSeq

                    Some(coverageKey root fileName.Value, lines))

            lineMaps
            |> Seq.groupBy fst
            |> Seq.map (fun (file, maps) ->
                let merged =
                    maps
                    |> Seq.collect (snd >> Map.toSeq)
                    |> Seq.groupBy fst
                    |> Seq.map (fun (lineNumber, hits) -> lineNumber, hits |> Seq.exists snd)
                    |> Map.ofSeq

                file, merged)
            |> Map.ofSeq

    let coverageForSpan (coverage: Map<string, Map<int, bool>>) (span: FunctionSpan) =
        coverage
        |> Map.tryFind span.File
        |> Option.bind (fun lineCoverage ->
            let covered, total =
                [ span.StartLine .. span.EndLine ]
                |> List.fold
                    (fun (covered, total) lineNumber ->
                        match Map.tryFind lineNumber lineCoverage with
                        | Some true -> covered + 1, total + 1
                        | Some false -> covered, total + 1
                        | None -> covered, total)
                    (0, 0)

            if total = 0 then
                None
            else
                Some(float covered / float total))

    let crapScore (complexity: int) (coverage: float) =
        let cc = float complexity
        cc ** 2.0 * ((1.0 - coverage) ** 3.0) + cc

    let scoreSpans (coverage: Map<string, Map<int, bool>>) (spans: FunctionSpan list) =
        spans
        |> List.map (fun span ->
            let coverage = coverageForSpan coverage span

            {
                Span = span
                Coverage = coverage
                Crap = coverage |> Option.map (crapScore span.CyclomaticComplexity)
            })
        |> List.sortByDescending (fun score -> score.Crap |> Option.defaultValue -1.0)

    let discoverSourceFiles (root: string) (inputs: string list) =
        let root = normalizePath root

        let underRoot (path: string) =
            let fullPath = normalizePath path
            fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)

        let sourceFilesIn (directory: string) =
            Directory.EnumerateFiles(directory, "*.fs", SearchOption.AllDirectories)
            |> Seq.filter (fun path ->
                not (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                && not (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))

        let selected =
            if List.isEmpty inputs then
                [ Path.Combine(root, "ChessOverlay") ]
            else
                inputs
                |> List.map (fun (input: string) ->
                    if Path.IsPathRooted input then
                        input
                    else
                        Path.Combine(root, input))

        selected
        |> Seq.collect (fun input ->
            if File.Exists input && Path.GetExtension(input).Equals(".fs", StringComparison.OrdinalIgnoreCase) then
                Seq.singleton input
            elif Directory.Exists input then
                sourceFilesIn input
            else
                Seq.empty)
        |> Seq.filter underRoot
        |> Seq.distinctBy (fun path -> normalizePath path)
        |> Seq.sort
        |> Seq.toList

    let private runGit (root: string) (arguments: string) =
        let startInfo = ProcessStartInfo("git", arguments)
        startInfo.WorkingDirectory <- root
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false

        use gitProcess = Process.Start(startInfo)
        let output = gitProcess.StandardOutput.ReadToEnd()
        let _ = gitProcess.StandardError.ReadToEnd()
        gitProcess.WaitForExit()

        if gitProcess.ExitCode = 0 then
            output.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList
        else
            []

    let changedSourceFiles (root: string) =
        runGit root "diff --name-only --diff-filter=ACMRT HEAD"
        |> List.filter (fun path -> path.EndsWith(".fs", StringComparison.OrdinalIgnoreCase))
        |> List.filter (fun path -> path.StartsWith("ChessOverlay/", StringComparison.OrdinalIgnoreCase))
        |> List.map (fun path -> Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))

    let private uniqueRunName () =
        $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"

    let private msbuildPathWithTrailingSlash ([<ParamArray>] parts: string array) =
        String.Join("/", parts) + "/"

    let generateCoverage (root: string) =
        let root = normalizePath root
        let runName = uniqueRunName ()
        let testProject = Path.Combine(root, "ChessOverlay.Tests")
        let resultsDirectory = Path.Combine(root, "artifacts", "coverage", runName)
        let outputPath = msbuildPathWithTrailingSlash [| ".build-check"; "quality-coverage"; runName; "bin" |]
        let intermediatePath = msbuildPathWithTrailingSlash [| ".build-check"; "quality-coverage"; runName; "obj" |]

        Directory.CreateDirectory(resultsDirectory) |> ignore

        let startInfo = ProcessStartInfo("dotnet")
        startInfo.WorkingDirectory <- root
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        startInfo.ArgumentList.Add("test")
        startInfo.ArgumentList.Add(testProject)
        startInfo.ArgumentList.Add("--collect")
        startInfo.ArgumentList.Add("XPlat Code Coverage")
        // Skip the slow FsCheck property-test modules during coverage generation.
        // The fast example-based tests still run, so coverage stays meaningful.
        startInfo.ArgumentList.Add("--filter")
        startInfo.ArgumentList.Add("FullyQualifiedName!~PropertyTests")
        startInfo.ArgumentList.Add("--results-directory")
        startInfo.ArgumentList.Add(resultsDirectory)
        startInfo.ArgumentList.Add($"-p:BaseOutputPath={outputPath}")
        startInfo.ArgumentList.Add($"-p:BaseIntermediateOutputPath={intermediatePath}")

        use testProcess = Process.Start(startInfo)
        let output = testProcess.StandardOutput.ReadToEnd()
        let error = testProcess.StandardError.ReadToEnd()
        testProcess.WaitForExit()

        if testProcess.ExitCode <> 0 then
            invalidOp $"Coverage test run failed.{Environment.NewLine}{output}{Environment.NewLine}{error}"

        let coveragePath =
            Directory.EnumerateFiles(resultsDirectory, "coverage.cobertura.xml", SearchOption.AllDirectories)
            |> Seq.map FileInfo
            |> Seq.sortByDescending _.LastWriteTimeUtc
            |> Seq.tryHead
            |> Option.map _.FullName
            |> Option.defaultWith (fun () ->
                invalidOp $"Coverage test run completed but no coverage.cobertura.xml was written under {resultsDirectory}.")

        {
            CoveragePath = coveragePath
            Output = output
            Error = error
        }

    let analyze (options: QualityOptions) =
        let sourceFiles =
            if options.ChangedOnly then
                changedSourceFiles options.Root
            else
                discoverSourceFiles options.Root options.Inputs

        let coverage =
            options.CoveragePath
            |> Option.map (readCoberturaLineCoverage options.Root)
            |> Option.defaultValue Map.empty

        sourceFiles
        |> List.collect (findFunctionSpans options.Root)
        |> scoreSpans coverage
