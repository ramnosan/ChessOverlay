namespace ChessOverlay.Quality

open System
open System.IO
open System.Text
open System.Text.RegularExpressions

type DuplicateLocation =
    {
        File: string
        StartLine: int
        EndLine: int
    }

type DuplicateCandidate =
    {
        Score: float
        Left: DuplicateLocation
        Right: DuplicateLocation
        LeftTokens: int
        RightTokens: int
    }

type DryOptions =
    {
        Root: string
        Inputs: string list
        Threshold: float
        MinimumLines: int
        MinimumTokens: int
        Format: string
    }

module DryDuplication =
    let private inlineCommentPattern = Regex(@"//.*$", RegexOptions.Compiled)
    let private blockCommentPattern = Regex(@"\(\*.*?\*\)", RegexOptions.Compiled ||| RegexOptions.Singleline)
    let private tokenPattern =
        Regex(
            @"``[^`]+``|[A-Za-z_][A-Za-z0-9_']*|\d+(?:\.\d+)?|""(?:\\""|[^""])*""|[()\[\]{}.,:;|=+\-*/<>!&%]+",
            RegexOptions.Compiled)
    let private declarationPattern =
        Regex(
            @"^\s*(?:let\s+(?:(?:rec|inline|private|internal|public|mutable)\s+)*(?!(?:rec|inline|private|internal|public|mutable)\b)(?:``[^`]+``|[A-Za-z_][A-Za-z0-9_']*)\b|member\s+(?:(?:private|internal|public)\s+)*[^\s.]+\.(?:``[^`]+``|[A-Za-z_][A-Za-z0-9_']*)\b|type\s+(?:(?:private|internal|public)\s+)*(?:``[^`]+``|[A-Za-z_][A-Za-z0-9_']*)\b)",
            RegexOptions.Compiled)

    let private keywords =
        set [
            "abstract"; "and"; "as"; "assert"; "base"; "begin"; "class"; "default"; "delegate"; "do"; "done"
            "downcast"; "downto"; "elif"; "else"; "end"; "exception"; "extern"; "false"; "finally"; "fixed"
            "for"; "fun"; "function"; "global"; "if"; "in"; "inherit"; "inline"; "interface"; "internal"
            "lazy"; "let"; "match"; "member"; "module"; "mutable"; "namespace"; "new"; "not"; "null"; "of"
            "open"; "or"; "override"; "private"; "public"; "rec"; "return"; "sig"; "static"; "struct"; "then"
            "to"; "true"; "try"; "type"; "upcast"; "use"; "val"; "void"; "when"; "while"; "with"; "yield"
        ]

    let private normalizePath (path: string) =
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

    let private relativePath (root: string) (path: string) =
        Path.GetRelativePath(root, path).Replace('\\', '/')

    let private indentation (line: string) =
        line
        |> Seq.takeWhile (fun value -> value = ' ' || value = '\t')
        |> Seq.sumBy (fun value -> if value = '\t' then 4 else 1)

    let private stripComments (text: string) =
        let withoutBlockComments =
            blockCommentPattern.Replace(text, MatchEvaluator(fun m -> String(' ', m.Value.Length)))

        withoutBlockComments.Split([| "\r\n"; "\n" |], StringSplitOptions.None)
            |> Array.map (fun line -> inlineCommentPattern.Replace(line, ""))
            |> String.concat "\n"

    let private normalizeToken (value: string) =
        if value.StartsWith("\"", StringComparison.Ordinal) then
            "STR"
        elif Regex.IsMatch(value, @"^\d") then
            "NUM"
        elif value.StartsWith("``", StringComparison.Ordinal) then
            "ID"
        elif keywords.Contains value then
            value
        elif Regex.IsMatch(value, @"^[A-Za-z_]") then
            "ID"
        else
            value

    let private tokensWithLines (text: string) =
        let cleaned = stripComments text

        tokenPattern.Matches(cleaned)
        |> Seq.cast<Match>
        |> Seq.map (fun token ->
            let prefix = cleaned.Substring(0, token.Index)
            let line = 1 + (prefix |> Seq.filter ((=) '\n') |> Seq.length)
            line, normalizeToken token.Value)
        |> Seq.toList

    let private fingerprints (tokens: string list) =
        let windowSize = 5

        if tokens.Length < windowSize then
            Set.ofList tokens
        else
            tokens
            |> List.windowed windowSize
            |> List.map (String.concat " ")
            |> Set.ofList

    let private jaccard (left: Set<string>) (right: Set<string>) =
        let shared = Set.intersect left right |> Set.count
        let all = Set.union left right |> Set.count

        if all = 0 then
            0.0
        else
            float shared / float all

    let private chunksForFile (root: string) (file: string) =
        let fullPath = normalizePath file
        let text = File.ReadAllText fullPath
        let lines = text.Split([| "\r\n"; "\n" |], StringSplitOptions.None) |> Array.toList

        let starts =
            lines
            |> List.mapi (fun index line -> index + 1, line)
            |> List.choose (fun (lineNumber, line) ->
                if declarationPattern.IsMatch(line) && indentation line <= 4 then
                    Some(lineNumber, indentation line)
                else
                    None)

        starts
        |> List.mapi (fun index (startLine, startIndent) ->
            let endLine =
                starts
                |> List.skip (index + 1)
                |> List.tryFind (fun (_, indent) -> indent <= startIndent)
                |> Option.map (fun (lineNumber, _) -> lineNumber - 1)
                |> Option.defaultValue lines.Length

            let chunkText =
                lines
                |> List.skip (startLine - 1)
                |> List.take (endLine - startLine + 1)
                |> String.concat "\n"

            let normalizedTokens = tokensWithLines chunkText |> List.map snd

            {
                File = relativePath (normalizePath root) fullPath
                StartLine = startLine
                EndLine = endLine
            },
            normalizedTokens,
            fingerprints normalizedTokens)

    let discoverSourceFiles (root: string) (inputs: string list) =
        let root = normalizePath root

        let sourceFilesIn (directory: string) =
            Directory.EnumerateFiles(directory, "*.fs", SearchOption.AllDirectories)
            |> Seq.filter (fun path ->
                not (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                && not (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))

        let selected =
            if List.isEmpty inputs then
                [
                    Path.Combine(root, "ChessOverlay")
                    Path.Combine(root, "ChessOverlay.Tests")
                ]
            else
                inputs
                |> List.map (fun input ->
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
        |> Seq.distinctBy normalizePath
        |> Seq.sort
        |> Seq.toList

    let findDuplicates (options: DryOptions) =
        let chunks =
            discoverSourceFiles options.Root options.Inputs
            |> List.collect (chunksForFile options.Root)
            |> List.filter (fun (location, tokens, _) ->
                location.EndLine - location.StartLine + 1 >= options.MinimumLines
                && tokens.Length >= options.MinimumTokens)

        [
            for leftIndex in 0 .. chunks.Length - 1 do
                for rightIndex in leftIndex + 1 .. chunks.Length - 1 do
                    let leftLocation, leftTokens, leftFingerprints = chunks[leftIndex]
                    let rightLocation, rightTokens, rightFingerprints = chunks[rightIndex]
                    let sameFile = leftLocation.File = rightLocation.File
                    let overlaps =
                        sameFile
                        && leftLocation.StartLine <= rightLocation.EndLine
                        && rightLocation.StartLine <= leftLocation.EndLine

                    if not overlaps then
                        let score = jaccard leftFingerprints rightFingerprints

                        if score >= options.Threshold then
                            {
                                Score = score
                                Left = leftLocation
                                Right = rightLocation
                                LeftTokens = leftTokens.Length
                                RightTokens = rightTokens.Length
                            }
        ]
        |> List.sortByDescending _.Score

    let escapeEdn (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")
