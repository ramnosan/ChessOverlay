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
        | _, "BoardReading"
        | _, "TemplatePieceDetection" -> "Screen And Piece Detection", 2
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
            && not (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            // Ignore git worktree copies (.worktrees/<name>/...) so duplicated
            // project trees are not counted as separate modules.
            && not (path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}")))
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

    let private dependencySourceText (text: string) =
        let builder = StringBuilder(text.Length)
        let mutable index = 0

        let appendSpace () =
            builder.Append(' ') |> ignore
            index <- index + 1

        let appendChar () =
            builder.Append(text[index]) |> ignore
            index <- index + 1

        let startsWith (value: string) =
            index + value.Length <= text.Length
            && String.CompareOrdinal(text, index, value, 0, value.Length) = 0

        let rec skipBlockComment depth =
            if index < text.Length then
                if startsWith "(*" then
                    appendSpace ()
                    appendSpace ()
                    skipBlockComment (depth + 1)
                elif startsWith "*)" then
                    appendSpace ()
                    appendSpace ()

                    if depth > 1 then
                        skipBlockComment (depth - 1)
                else
                    appendSpace ()
                    skipBlockComment depth

        let skipLineComment () =
            while index < text.Length && text[index] <> '\n' do
                appendSpace ()

        let skipTripleQuotedString () =
            while index < text.Length && not (startsWith "\"\"\"") do
                appendSpace ()

            if startsWith "\"\"\"" then
                appendSpace ()
                appendSpace ()
                appendSpace ()

        let skipVerbatimString () =
            let mutable closedString = false

            while index < text.Length && not closedString do
                if text[index] = '"' && index + 1 < text.Length && text[index + 1] = '"' then
                    appendSpace ()
                    appendSpace ()
                elif text[index] = '"' then
                    appendSpace ()
                    closedString <- true
                else
                    appendSpace ()

        let skipString () =
            let mutable escaped = false
            let mutable closedString = false

            while index < text.Length && not closedString do
                let current = text[index]
                appendSpace ()

                if escaped then
                    escaped <- false
                elif current = '\\' then
                    escaped <- true
                elif current = '"' then
                    closedString <- true

        while index < text.Length do
            if startsWith "(*" then
                appendSpace ()
                appendSpace ()
                skipBlockComment 1
            elif startsWith "//" then
                skipLineComment ()
            elif startsWith "\"\"\"" then
                appendSpace ()
                appendSpace ()
                appendSpace ()
                skipTripleQuotedString ()
            elif startsWith "@\"" then
                appendSpace ()
                appendSpace ()
                skipVerbatimString ()
            elif text[index] = '"' then
                appendSpace ()
                skipString ()
            else
                appendChar ()

        builder.ToString()

    let private tokens (text: string) =
        tokenPattern.Matches(dependencySourceText text)
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

    let private boxWidth = 150
    let private boxHeight = 56
    let private hGap = 30
    let private vGap = 70
    let private marginX = 30
    let private marginY = 40

    type private BoxLayout =
        {
            Module: ArchitectureModule
            X: int
            Y: int
        }

    let private layoutBoxes (model: ArchitectureModel) =
        let layers =
            model.Modules
            |> List.groupBy (fun moduleInfo -> moduleInfo.LayerRank, moduleInfo.Layer)
            |> List.sortBy fst
            |> List.map (fun (key, modules) -> key, modules |> List.sortBy _.Name)

        let layerWidths =
            layers
            |> List.map (fun (_, modules) ->
                let count = modules.Length
                count * boxWidth + max 0 (count - 1) * hGap)

        let canvasWidth =
            if List.isEmpty layerWidths then
                boxWidth
            else
                List.max layerWidths

        let lastLayerHasSiblings =
            match layers |> List.tryLast with
            | Some (_, modules) -> modules.Length > 1
            | None -> false

        let bottomArcPadding = if lastLayerHasSiblings then vGap else 0

        let canvasHeight =
            if List.isEmpty layers then
                boxHeight
            else
                layers.Length * boxHeight + max 0 (layers.Length - 1) * vGap + bottomArcPadding

        let boxes =
            layers
            |> List.mapi (fun layerIndex (_, modules) ->
                let totalWidth =
                    modules.Length * boxWidth + max 0 (modules.Length - 1) * hGap

                let xStart = marginX + (canvasWidth - totalWidth) / 2
                let y = marginY + layerIndex * (boxHeight + vGap)

                modules
                |> List.mapi (fun moduleIndex moduleInfo ->
                    {
                        Module = moduleInfo
                        X = xStart + moduleIndex * (boxWidth + hGap)
                        Y = y
                    }))
            |> List.concat

        let totalWidth = canvasWidth + marginX * 2
        let totalHeight = canvasHeight + marginY * 2
        boxes, totalWidth, totalHeight

    let private renderSvgDiagram (model: ArchitectureModel) =
        let boxes, width, height = layoutBoxes model
        let boxById = boxes |> List.map (fun box -> box.Module.Id, box) |> Map.ofList

        let cycleClassOf (moduleInfo: ArchitectureModule) =
            let touchesCycle =
                model.Dependencies
                |> List.exists (fun edge ->
                    edge.IsCycle && (edge.From = moduleInfo.Id || edge.To = moduleInfo.Id))

            if touchesCycle then " cycle" else ""

        let edgePaths =
            model.Dependencies
            |> List.choose (fun edge ->
                match Map.tryFind edge.From boxById, Map.tryFind edge.To boxById with
                | Some source, Some target ->
                    let sourceCenterX = source.X + boxWidth / 2
                    let targetCenterX = target.X + boxWidth / 2

                    let pathData =
                        if target.Y > source.Y then
                            sprintf "M %d %d L %d %d" sourceCenterX (source.Y + boxHeight) targetCenterX target.Y
                        elif target.Y < source.Y then
                            sprintf "M %d %d L %d %d" sourceCenterX source.Y targetCenterX (target.Y + boxHeight)
                        else
                            let sx = sourceCenterX
                            let sy = source.Y + boxHeight
                            let tx = targetCenterX
                            let ty = target.Y + boxHeight
                            let span = abs (tx - sx)
                            let arcDepth = max 24 (min (vGap - 10) (span / 3))
                            let cx = (sx + tx) / 2
                            let cy = sy + arcDepth
                            sprintf "M %d %d Q %d %d %d %d" sx sy cx cy tx ty

                    let cls = if edge.IsCycle then "edge cycle" else "edge"
                    let dataFrom = html edge.From
                    let dataTo = html edge.To
                    let path = sprintf "<path class=\"%s\" data-from=\"%s\" data-to=\"%s\" d=\"%s\" marker-end=\"url(#arrow)\" />" cls dataFrom dataTo pathData
                    Some path
                | _ -> None)
            |> String.concat Environment.NewLine

        let boxNodes =
            boxes
            |> List.map (fun box ->
                let cls = "node" + cycleClassOf box.Module
                let dataId = html box.Module.Id
                let label = html box.Module.Name
                let titleX = box.X + boxWidth / 2
                let titleY = box.Y + boxHeight / 2 + 5
                let rect = sprintf "<rect x=\"%d\" y=\"%d\" width=\"%d\" height=\"%d\" rx=\"6\" ry=\"6\" />" box.X box.Y boxWidth boxHeight
                let text = sprintf "<text x=\"%d\" y=\"%d\" text-anchor=\"middle\">%s</text>" titleX titleY label
                sprintf "<g class=\"%s\" data-module=\"%s\">%s%s</g>" cls dataId rect text)
            |> String.concat Environment.NewLine

        let defs = "<defs><marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\"><path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#65736b\" /></marker></defs>"
        sprintf "<svg id=\"diagram-svg\" viewBox=\"0 0 %d %d\" preserveAspectRatio=\"xMidYMin meet\" xmlns=\"http://www.w3.org/2000/svg\">%s<g class=\"edges\">%s</g><g class=\"nodes\">%s</g></svg>" width height defs edgePaths boxNodes

    let renderHtml (model: ArchitectureModel) =
        let layers =
            model.Modules
            |> List.groupBy (fun moduleInfo -> moduleInfo.LayerRank, moduleInfo.Layer)
            |> List.sortBy fst

        let svgDiagram = renderSvgDiagram model

        let layerLegend =
            layers
            |> List.map (fun ((_, layer), modules) ->
                $"""<li><strong>{html layer}</strong> <span>{modules.Length}</span></li>""")
            |> String.concat Environment.NewLine

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
.option {{ display: inline-flex; align-items: center; gap: 6px; color: var(--muted); font-size: 13px; }}
.option input {{ min-width: 0; }}
main {{ display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 18px; padding: 18px; }}
h2 {{ font-size: 16px; margin: 0 0 8px; }}
#diagram {{ background: var(--panel); border: 1px solid var(--line); border-radius: 8px; padding: 10px; overflow: auto; }}
#diagram-svg {{ width: 100%%; height: auto; display: block; }}
.node rect {{ fill: #eaf0ee; stroke: var(--line); stroke-width: 1.5; transition: fill .12s, stroke .12s; }}
.node text {{ font-family: Consolas, "Courier New", monospace; font-size: 12px; fill: var(--ink); pointer-events: none; }}
.node {{ cursor: pointer; }}
.node:hover rect, .node.selected rect {{ fill: #d6e6e0; stroke: var(--accent); stroke-width: 2; }}
.node.cycle rect {{ stroke: var(--warn); }}
.node.dim {{ opacity: 0.25; }}
.node.hidden, .edges path.hidden {{ display: none; }}
.edges path {{ fill: none; stroke: #65736b; stroke-width: 1.5; }}
.edges path.cycle {{ stroke: var(--warn); stroke-dasharray: 4 3; }}
.edges path.highlight {{ stroke: var(--accent); stroke-width: 2.5; }}
.legend {{ list-style: none; padding: 0; margin: 0 0 12px; display: flex; flex-wrap: wrap; gap: 6px; font-size: 12px; color: var(--muted); }}
.legend li {{ background: var(--panel); border: 1px solid var(--line); border-radius: 4px; padding: 2px 8px; }}
.legend span {{ color: var(--ink); margin-left: 4px; }}
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
  <label class="option"><input id="exclude-tests" type="checkbox" checked> Exclude tests</label>
  <span id="module-count">{model.Modules.Length} modules</span>
  <span id="dependency-count">{model.Dependencies.Length} dependencies</span>
  <span>{model.Cycles.Length} cycles</span>
</div>
<main>
  <div id="diagram">
    <ul class="legend">{layerLegend}</ul>
    {svgDiagram}
  </div>
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
const filterInput = document.querySelector("#filter");
const excludeTestsInput = document.querySelector("#exclude-tests");
const moduleCount = document.querySelector("#module-count");
const dependencyCount = document.querySelector("#dependency-count");
const nodes = () => document.querySelectorAll("#diagram-svg .node");
const edgePaths = () => document.querySelectorAll("#diagram-svg .edges path");
let selectedModuleId = null;
function describeEdge(edge, direction) {{
  const other = direction === "out" ? byId[edge.to] : byId[edge.from];
  const arrow = direction === "out" ? "→" : "←";
  const arrowClass = direction === "out" ? "dep-out" : "dep-in";
  const symbols = edge.symbols.length ? " via " + edge.symbols.join(", ") : "";
  return `<div class="edge ${{edge.cycle ? "cycle" : ""}}"><span class="arrow ${{arrowClass}}">${{arrow}}</span><strong>${{other.name}}</strong><br><span>${{other.file}}${{symbols}}</span></div>`;
}}
function selectModule(id) {{
  if (!isModuleVisible(id)) return;
  selectedModuleId = id;
  nodes().forEach(node => node.classList.toggle("selected", node.dataset.module === id));
  edgePaths().forEach(path => {{
    const related = path.dataset.from === id || path.dataset.to === id;
    path.classList.toggle("highlight", related);
  }});
  const module = byId[id];
  const outgoing = edges.filter(edge => edge.from === id);
  const incoming = edges.filter(edge => edge.to === id);
  title.textContent = module.name;
  details.className = "";
  details.innerHTML = `<p><strong>${{module.layer}}</strong><br>${{module.file}}</p>` +
    `<h3>Outgoing</h3>${{outgoing.length ? outgoing.map(edge => describeEdge(edge, "out")).join("") : "<p class='empty'>No outgoing dependencies.</p>"}}` +
    `<h3>Incoming</h3>${{incoming.length ? incoming.map(edge => describeEdge(edge, "in")).join("") : "<p class='empty'>No incoming dependencies.</p>"}}`;
}}
nodes().forEach(node => node.addEventListener("click", () => selectModule(node.dataset.module)));
function isModuleVisible(id) {{
  const module = byId[id];
  return module && (!excludeTestsInput.checked || module.layer !== "Test Suite");
}}
function clearSelection() {{
  selectedModuleId = null;
  title.textContent = "Select a module";
  details.className = "empty";
  details.textContent = "Click a module to inspect incoming and outgoing dependencies.";
  nodes().forEach(node => node.classList.remove("selected"));
  edgePaths().forEach(path => path.classList.remove("highlight"));
}}
function applyFilters() {{
  const value = filterInput.value.toLowerCase();
  const matches = new Set();
  modules.forEach(module => {{
    const haystack = [module.name, module.file, module.layer, ...module.symbols].join(" ").toLowerCase();
    if (!value || haystack.includes(value)) matches.add(module.id);
  }});
  nodes().forEach(node => {{
    const visible = isModuleVisible(node.dataset.module);
    node.classList.toggle("hidden", !visible);
    node.classList.toggle("dim", visible && !matches.has(node.dataset.module));
  }});
  let visibleEdges = 0;
  edgePaths().forEach(path => {{
    const visible = isModuleVisible(path.dataset.from) && isModuleVisible(path.dataset.to);
    path.classList.toggle("hidden", !visible);
    if (visible) visibleEdges++;
  }});
  const visibleModules = modules.filter(module => isModuleVisible(module.id)).length;
  moduleCount.textContent = `${{visibleModules}} modules`;
  dependencyCount.textContent = `${{visibleEdges}} dependencies`;

  if (selectedModuleId && !isModuleVisible(selectedModuleId)) clearSelection();
}}
filterInput.addEventListener("input", applyFilters);
excludeTestsInput.addEventListener("change", applyFilters);
applyFilters();
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
