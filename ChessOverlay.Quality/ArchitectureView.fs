namespace ChessOverlay.Quality

open System
open System.IO
open System.Net
open System.Text
open System.Text.RegularExpressions
open System.Xml.Linq

type ArchitectureModule =
    {
        Id: string
        Name: string
        File: string
        Project: string
        Layer: string
        LayerRank: int
        Symbols: string list
        Lines: int
    }

type ArchitectureDependency =
    {
        From: string
        To: string
        Symbols: string list
        IsCycle: bool
    }

type ArchitectureCycle =
    {
        Path: string list
    }

type ArchitectureModel =
    {
        Root: string
        Modules: ArchitectureModule list
        Dependencies: ArchitectureDependency list
        Cycles: ArchitectureCycle list
    }

type ArchitectureOptions =
    {
        Root: string
        Inputs: string list
        Format: string
        OutputPath: string option
    }

module ArchitectureView =
    let private namespacePattern = Regex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.']*)", RegexOptions.Compiled)
    let private modulePattern = Regex(@"^\s*module\s+([A-Za-z_][A-Za-z0-9_']*)\b", RegexOptions.Compiled)
    let private typePattern = Regex(@"^\s*type\s+(?:(?:private|internal|public)\s+)*([A-Za-z_][A-Za-z0-9_']*)\b", RegexOptions.Compiled)
    let private tokenPattern = Regex(@"\b[A-Za-z_][A-Za-z0-9_']*\b", RegexOptions.Compiled)

    let private normalizePath (path: string) =
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

    let private relativePath (root: string) (path: string) =
        Path.GetRelativePath(root, path).Replace('\\', '/')

    let private html (value: string) =
        WebUtility.HtmlEncode(value)

    let private fileStem (file: string) =
        Path.GetFileNameWithoutExtension(file)

    let private projectName (root: string) (file: string) =
        let relative = relativePath (normalizePath root) (normalizePath file)
        let parts = relative.Split('/')

        if parts.Length > 1 then
            parts[0]
        else
            "ChessOverlay"

    let private layerFor (project: string) (name: string) =
        match project, name with
        | project, _ when project.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) -> "Test Suite", 0
        | project, _ when project.EndsWith(".Quality", StringComparison.OrdinalIgnoreCase) -> "Quality Tooling", 1
        | _, "Program" -> "Composition Root", 0
        | _, "OverlayController"
        | _, "OverlayWindow"
        | _, "BoardSelectionWindow" -> "Overlay UI", 1
        | _, "BoardDetection"
        | _, "TemplatePieceDetection"
        | _, "YoloPieceDetection" -> "Screen And Piece Detection", 2
        | _, "AttackCalculator" -> "Chess Rules", 3
        | _, "Domain" -> "Domain Model", 4
        | _ -> "Application Core", 2

    let private readCompileIncludes (projectFile: string) =
        let projectDirectory = Path.GetDirectoryName(projectFile)
        let document = XDocument.Load(projectFile)

        document.Descendants(XName.Get "Compile")
        |> Seq.choose (fun element ->
            let includeAttribute = element.Attribute(XName.Get "Include")

            if isNull includeAttribute then
                None
            else
                Some(Path.Combine(projectDirectory, includeAttribute.Value)))
        |> Seq.filter File.Exists
        |> Seq.toList

    let private discoverProjectFiles (root: string) (inputs: string list) =
        let root = normalizePath root

        let candidateProjects =
            if List.isEmpty inputs then
                Directory.EnumerateFiles(root, "*.fsproj", SearchOption.AllDirectories)
            else
                inputs
                |> Seq.map (fun input ->
                    if Path.IsPathRooted input then
                        input
                    else
                        Path.Combine(root, input))
                |> Seq.collect (fun input ->
                    if File.Exists input && input.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) then
                        Seq.singleton input
                    elif Directory.Exists input then
                        Directory.EnumerateFiles(input, "*.fsproj", SearchOption.AllDirectories)
                    else
                        Seq.empty)

        candidateProjects
        |> Seq.filter (fun path ->
            not (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            && not (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        |> Seq.sort
        |> Seq.collect readCompileIncludes
        |> Seq.distinctBy normalizePath
        |> Seq.toList

    let private symbolsInFile (text: string) (fallbackName: string) =
        let declared =
            text.Split([| "\r\n"; "\n" |], StringSplitOptions.None)
            |> Array.toList
            |> List.collect (fun line ->
                [
                    let moduleMatch = modulePattern.Match(line)
                    if moduleMatch.Success then
                        yield moduleMatch.Groups[1].Value

                    let typeMatch = typePattern.Match(line)
                    if typeMatch.Success then
                        yield typeMatch.Groups[1].Value
                ])
            |> List.distinct

        if List.isEmpty declared then
            [ fallbackName ]
        else
            declared

    let private parseModule (root: string) (file: string) =
        let text = File.ReadAllText(file)
        let lines = text.Split([| "\r\n"; "\n" |], StringSplitOptions.None)
        let project = projectName root file
        let name = fileStem file
        let layer, layerRank = layerFor project name

        {
            Id = relativePath (normalizePath root) (normalizePath file)
            Name = name
            File = relativePath (normalizePath root) (normalizePath file)
            Project = project
            Layer = layer
            LayerRank = layerRank
            Symbols = symbolsInFile text name
            Lines = lines.Length
        },
        text

    let private tokens (text: string) =
        tokenPattern.Matches(text)
        |> Seq.cast<Match>
        |> Seq.map _.Value
        |> Set.ofSeq

    let private dependencyEdges (modulesWithText: (ArchitectureModule * string) list) =
        let symbolOwners =
            modulesWithText
            |> List.collect (fun (moduleInfo, _) ->
                moduleInfo.Symbols
                |> List.map (fun symbol -> symbol, moduleInfo.Id))
            |> List.groupBy fst
            |> List.collect (fun (symbol, owners) ->
                match owners |> List.map snd |> List.distinct with
                | [ owner ] -> [ symbol, owner ]
                | _ -> [])

        modulesWithText
        |> List.collect (fun (moduleInfo, text) ->
            let fileTokens = tokens text

            symbolOwners
            |> List.choose (fun (symbol, owner) ->
                if owner <> moduleInfo.Id && Set.contains symbol fileTokens then
                    Some(owner, symbol)
                else
                    None)
            |> List.groupBy fst
            |> List.map (fun (target, symbols) ->
                {
                    From = moduleInfo.Id
                    To = target
                    Symbols = symbols |> List.map snd |> List.distinct |> List.sort
                    IsCycle = false
                }))
        |> List.sortBy (fun edge -> edge.From, edge.To)

    let private findCycles (edges: ArchitectureDependency list) =
        let adjacency =
            edges
            |> List.groupBy _.From
            |> List.map (fun (from, edges) -> from, edges |> List.map _.To |> List.distinct)
            |> Map.ofList

        let canonical (path: string list) =
            let cycle = path |> List.rev
            let withoutClosingNode =
                if cycle.Head = (cycle |> List.last) then
                    cycle |> List.take (cycle.Length - 1)
                else
                    cycle

            let rotations =
                [
                    for index in 0 .. withoutClosingNode.Length - 1 do
                        yield (withoutClosingNode |> List.skip index) @ (withoutClosingNode |> List.take index)
                ]

            let best = rotations |> List.min
            best @ [ best.Head ]

        let rec walk start current visited stack =
            adjacency
            |> Map.tryFind current
            |> Option.defaultValue []
            |> List.collect (fun next ->
                if next = start then
                    [ canonical (next :: stack) ]
                elif Set.contains next visited then
                    []
                else
                    walk start next (Set.add next visited) (next :: stack))

        edges
        |> List.collect (fun edge -> walk edge.From edge.From (Set.singleton edge.From) [ edge.From ])
        |> List.distinct
        |> List.map (fun path -> { Path = path })

    let private cycleEdgeSet (cycles: ArchitectureCycle list) =
        cycles
        |> List.collect (fun cycle ->
            cycle.Path
            |> List.pairwise
            |> List.map (fun edge -> edge))
        |> Set.ofList

    let analyze (options: ArchitectureOptions) =
        let modulesWithText =
            discoverProjectFiles options.Root options.Inputs
            |> List.map (parseModule options.Root)

        let cycles =
            modulesWithText
            |> dependencyEdges
            |> findCycles

        let cycleEdges = cycleEdgeSet cycles

        let dependencies =
            modulesWithText
            |> dependencyEdges
            |> List.map (fun edge ->
                {
                    edge with
                        IsCycle = Set.contains (edge.From, edge.To) cycleEdges
                })

        {
            Root = normalizePath options.Root
            Modules = modulesWithText |> List.map fst |> List.sortBy (fun moduleInfo -> moduleInfo.LayerRank, moduleInfo.Name)
            Dependencies = dependencies
            Cycles = cycles
        }

    let renderText (model: ArchitectureModel) =
        let builder = StringBuilder()
        builder.AppendLine("ChessOverlay architecture") |> ignore
        builder.AppendLine("=========================") |> ignore
        builder.AppendLine(sprintf "Modules: %i" model.Modules.Length) |> ignore
        builder.AppendLine(sprintf "Dependencies: %i" model.Dependencies.Length) |> ignore
        builder.AppendLine(sprintf "Cycles: %i" model.Cycles.Length) |> ignore
        builder.AppendLine() |> ignore

        for layer, modules in model.Modules |> List.groupBy _.Layer do
            builder.AppendLine(layer) |> ignore

            for moduleInfo in modules |> List.sortBy _.Name do
                builder.AppendLine(sprintf "  %s (%s)" moduleInfo.Name moduleInfo.File) |> ignore

        if model.Cycles.Length > 0 then
            builder.AppendLine() |> ignore
            builder.AppendLine("Cycles") |> ignore

            for cycle in model.Cycles do
                builder.AppendLine("  " + String.Join(" -> ", cycle.Path)) |> ignore

        builder.ToString()

    let private renderModuleCard (model: ArchitectureModel) (moduleInfo: ArchitectureModule) =
        let outgoing =
            model.Dependencies
            |> List.filter (fun edge -> edge.From = moduleInfo.Id)

        let incoming =
            model.Dependencies
            |> List.filter (fun edge -> edge.To = moduleInfo.Id)

        let cycleClass =
            if outgoing |> List.exists _.IsCycle || incoming |> List.exists _.IsCycle then
                " cycle"
            else
                ""

        let symbolSummary =
            moduleInfo.Symbols
            |> List.truncate 5
            |> String.concat ", "
            |> html

        $"""<article class="module{cycleClass}" data-module="{html moduleInfo.Id}">
  <button type="button" class="module-title">{html moduleInfo.Name}</button>
  <div class="module-file">{html moduleInfo.File}</div>
  <div class="module-meta"><span>{moduleInfo.Lines} lines</span><span class="dep-in">&#8592; {incoming.Length}</span><span class="dep-out">&#8594; {outgoing.Length}</span></div>
  <div class="symbols">{symbolSummary}</div>
</article>"""

    let renderHtml (model: ArchitectureModel) =
        let layers =
            model.Modules
            |> List.groupBy (fun moduleInfo -> moduleInfo.LayerRank, moduleInfo.Layer)
            |> List.sortBy fst

        let modulesJson =
            model.Modules
            |> List.map (fun moduleInfo ->
                let symbols =
                    moduleInfo.Symbols
                    |> List.map (fun symbol -> "\"" + symbol.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
                    |> String.concat ","

                $"""{{"id":"{moduleInfo.Id}","name":"{moduleInfo.Name}","file":"{moduleInfo.File}","layer":"{moduleInfo.Layer}","symbols":[{symbols}]}}""")
            |> String.concat ","

        let edgesJson =
            model.Dependencies
            |> List.map (fun edge ->
                let symbols =
                    edge.Symbols
                    |> List.map (fun symbol -> "\"" + symbol.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
                    |> String.concat ","

                $"""{{"from":"{edge.From}","to":"{edge.To}","symbols":[{symbols}],"cycle":{edge.IsCycle.ToString().ToLowerInvariant()}}}""")
            |> String.concat ","

        let cyclesHtml =
            if List.isEmpty model.Cycles then
                "<p>No cycles detected.</p>"
            else
                model.Cycles
                |> List.map (fun cycle ->
                    let path = html (String.Join(" → ", cycle.Path))
                    $"<li>{path}</li>")
                |> String.concat Environment.NewLine
                |> sprintf "<ul>%s</ul>"

        let layerHtml =
            layers
            |> List.map (fun ((_, layer), modules) ->
                let cards =
                    modules
                    |> List.sortBy _.Name
                    |> List.map (renderModuleCard model)
                    |> String.concat Environment.NewLine

                $"""<section class="layer">
  <header><h2>{html layer}</h2><span>{modules.Length} modules</span></header>
  <div class="module-grid">{cards}</div>
</section>""")
            |> String.concat Environment.NewLine

        $"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ChessOverlay Architecture</title>
<style>
:root {{ color-scheme: light; --ink: #1c2420; --muted: #65736b; --line: #c9d6ce; --panel: #f7faf8; --accent: #116a5c; --warn: #b42318; --blue: #e7f0ff; --green: #e7f6ec; --gold: #fff3c4; }}
body {{ margin: 0; font-family: Segoe UI, system-ui, sans-serif; color: var(--ink); background: #fbfcfb; }}
.toolbar {{ position: sticky; top: 0; z-index: 5; display: flex; gap: 12px; align-items: center; padding: 12px 18px; border-bottom: 1px solid var(--line); background: rgba(251,252,251,.96); }}
h1 {{ font-size: 20px; margin: 0 18px 0 0; }}
input {{ min-width: 260px; padding: 8px 10px; border: 1px solid var(--line); border-radius: 6px; font: inherit; }}
main {{ display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 18px; padding: 18px; }}
.layer {{ border-top: 3px solid var(--accent); padding-top: 10px; margin-bottom: 18px; }}
.layer header {{ display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 10px; }}
h2 {{ font-size: 16px; margin: 0; }}
.layer header span, .module-file, .symbols, .module-meta {{ color: var(--muted); font-size: 12px; }}
.module-grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px; }}
.module {{ background: var(--panel); border: 1px solid var(--line); border-radius: 8px; padding: 10px; min-height: 112px; box-shadow: 0 1px 2px rgba(0,0,0,.04); }}
.module:hover, .module.selected {{ border-color: var(--accent); outline: 2px solid rgba(17,106,92,.12); }}
.module.cycle {{ border-color: var(--warn); }}
.module-title {{ appearance: none; border: 0; background: transparent; padding: 0; color: var(--ink); font-weight: 700; font-size: 15px; cursor: pointer; }}
.module-meta {{ display: flex; gap: 10px; margin: 8px 0; }}
aside {{ position: sticky; top: 68px; align-self: start; border-left: 1px solid var(--line); padding-left: 18px; max-height: calc(100vh - 90px); overflow: auto; }}
.edge {{ padding: 8px 0; border-bottom: 1px solid var(--line); font-size: 13px; }}
.edge.cycle {{ color: var(--warn); font-weight: 700; }}
.empty {{ color: var(--muted); }}
.dep-in {{ color: #0066aa; font-weight: 600; }}
.dep-out {{ color: #885500; font-weight: 600; }}
.arrow {{ font-size: 15px; margin-right: 4px; }}
@media (max-width: 900px) {{ main {{ grid-template-columns: 1fr; }} aside {{ position: static; border-left: 0; padding-left: 0; }} .toolbar {{ flex-wrap: wrap; }} }}
</style>
</head>
<body>
<div class="toolbar">
  <h1>ChessOverlay Architecture</h1>
  <input id="filter" placeholder="Filter modules, layers, symbols">
  <span>{model.Modules.Length} modules</span>
  <span>{model.Dependencies.Length} dependencies</span>
  <span>{model.Cycles.Length} cycles</span>
</div>
<main>
  <div id="diagram">{layerHtml}</div>
  <aside>
    <h2 id="details-title">Select a module</h2>
    <div id="details" class="empty">Click a module to inspect incoming and outgoing dependencies.</div>
    <h2>Cycles</h2>
    {cyclesHtml}
  </aside>
</main>
<script>
const modules = [{modulesJson}];
const edges = [{edgesJson}];
const byId = Object.fromEntries(modules.map(module => [module.id, module]));
const details = document.querySelector("#details");
const title = document.querySelector("#details-title");
function describeEdge(edge, direction) {{
  const other = direction === "out" ? byId[edge.to] : byId[edge.from];
  const arrow = direction === "out" ? "→" : "←";
  const arrowClass = direction === "out" ? "dep-out" : "dep-in";
  const symbols = edge.symbols.length ? " via " + edge.symbols.join(", ") : "";
  return `<div class="edge ${{edge.cycle ? "cycle" : ""}}"><span class="arrow ${{arrowClass}}">${{arrow}}</span><strong>${{other.name}}</strong><br><span>${{other.file}}${{symbols}}</span></div>`;
}}
function selectModule(id) {{
  document.querySelectorAll(".module").forEach(card => card.classList.toggle("selected", card.dataset.module === id));
  const module = byId[id];
  const outgoing = edges.filter(edge => edge.from === id);
  const incoming = edges.filter(edge => edge.to === id);
  title.textContent = module.name;
  details.className = "";
  details.innerHTML = `<p><strong>${{module.layer}}</strong><br>${{module.file}}</p>` +
    `<h3>Outgoing</h3>${{outgoing.length ? outgoing.map(edge => describeEdge(edge, "out")).join("") : "<p class='empty'>No outgoing dependencies.</p>"}}` +
    `<h3>Incoming</h3>${{incoming.length ? incoming.map(edge => describeEdge(edge, "in")).join("") : "<p class='empty'>No incoming dependencies.</p>"}}`;
}}
document.querySelectorAll(".module-title").forEach(button => {{
  button.addEventListener("click", event => selectModule(event.target.closest(".module").dataset.module));
}});
document.querySelector("#filter").addEventListener("input", event => {{
  const value = event.target.value.toLowerCase();
  document.querySelectorAll(".module").forEach(card => {{
    const module = byId[card.dataset.module];
    const haystack = [module.name, module.file, module.layer, ...module.symbols].join(" ").toLowerCase();
    card.style.display = haystack.includes(value) ? "" : "none";
  }});
}});
</script>
</body>
</html>"""

    let writeOutput (options: ArchitectureOptions) (content: string) =
        match options.OutputPath with
        | Some outputPath ->
            let path =
                if Path.IsPathRooted outputPath then
                    outputPath
                else
                    Path.Combine(options.Root, outputPath)

            let directory = Path.GetDirectoryName(path)

            if not (String.IsNullOrWhiteSpace directory) then
                Directory.CreateDirectory(directory) |> ignore

            File.WriteAllText(path, content)
            Some path
        | None -> None
