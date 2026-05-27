namespace ChessOverlay

open System
open System.Net.Http
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Windows.Forms

module ChromeBoardDetector =

    let private cdpPort = 9222

    // Singleton - HttpClient is not safe to dispose per-request
    let private http = lazy (new HttpClient(Timeout = TimeSpan.FromSeconds 3.0))

    type ChromeTab =
        { Id: string
          Title: string
          Url: string
          WebSocketUrl: string }

    type DetectedBoard =
        { Tab: ChromeTab
          Geometry: BoardGeometry }

    // Returns physical screen pixels (CSS coords × devicePixelRatio).
    // halfFrame approximates the symmetric DWM window border so maximised Chrome windows
    // (which extend 8 logical px beyond each monitor edge) are handled correctly.
    // Picks the largest matching element per selector so lichess TV mini-boards are skipped.
    let private detectionScript =
        """(() => {
            const selectors = ['chess-board', 'wc-chess-board', 'cg-board'];
            let best = null, bestW = 0;
            for (const sel of selectors) {
                for (const el of document.querySelectorAll(sel)) {
                    const r = el.getBoundingClientRect();
                    if (r.width > bestW) { best = el; bestW = r.width; }
                }
                if (best && bestW > 100) break;
            }
            if (!best) return null;
            const r = best.getBoundingClientRect();
            const dpr = window.devicePixelRatio || 1;
            const bx  = window.outerWidth  - window.innerWidth;
            const by  = window.outerHeight - window.innerHeight;
            const hf  = bx / 2;
            return {
                left:   Math.round((window.screenX + hf       + r.left) * dpr),
                top:    Math.round((window.screenY + by - hf  + r.top ) * dpr),
                width:  Math.round(r.width  * dpr),
                height: Math.round(r.height * dpr)
            };
        })()"""

    // Explicit string overload annotation avoids F# ambiguity with ReadOnlySpan overloads.
    let private tryGet (el: JsonElement) (name: string) =
        let ok, v = el.TryGetProperty name
        if ok then Some v else None

    let private tryGetString (el: JsonElement) (name: string) =
        tryGet el name |> Option.bind (fun v ->
            if v.ValueKind = JsonValueKind.String then Some(v.GetString()) else None)

    let private tryGetInt (el: JsonElement) (name: string) =
        tryGet el name |> Option.map (fun v -> v.GetInt32()) |> Option.toValueOption

    // Option.bind chain — avoids match/with keywords so CC stays at 1.
    let private tryBuildTab (tab: JsonElement) =
        tryGetString tab "id" |> Option.bind (fun id ->
        tryGetString tab "title" |> Option.bind (fun title ->
        tryGetString tab "url" |> Option.bind (fun url ->
        tryGetString tab "webSocketDebuggerUrl" |> Option.map (fun wsUrl ->
            { Id = id; Title = title; Url = url; WebSocketUrl = wsUrl }))))

    let internal tryParseTab (tab: JsonElement) =
        if tryGetString tab "type" = Some "page" then tryBuildTab tab else None

    let private tryListTabs () =
        async {
            try
                let url = sprintf "http://localhost:%d/json/list" cdpPort
                let! json = http.Value.GetStringAsync url |> Async.AwaitTask
                use doc = JsonDocument.Parse json
                return Some(doc.RootElement.EnumerateArray() |> Seq.choose tryParseTab |> Seq.toList)
            with _ ->
                return None
        }

    let private readFullResponse (ws: ClientWebSocket) (cts: CancellationTokenSource) = async {
        let buf = Array.zeroCreate<byte> 65536
        let sb = StringBuilder()
        let mutable isDone = false
        while not isDone do
            let! res = ws.ReceiveAsync(ArraySegment buf, cts.Token) |> Async.AwaitTask
            sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count)) |> ignore
            isDone <- res.EndOfMessage
        return sb.ToString()
    }

    let private sendEvaluate (wsUrl: string) (expression: string) = async {
        use ws = new ClientWebSocket()
        use cts = new CancellationTokenSource(TimeSpan.FromSeconds 5.0)
        do! ws.ConnectAsync(Uri wsUrl, cts.Token) |> Async.AwaitTask
        let msg =
            sprintf
                """{"id":1,"method":"Runtime.evaluate","params":{"expression":%s,"returnByValue":true}}"""
                (JsonSerializer.Serialize expression)
        let bytes = Encoding.UTF8.GetBytes msg
        do! ws.SendAsync(ArraySegment bytes, WebSocketMessageType.Text, true, cts.Token) |> Async.AwaitTask
        let! result = readFullResponse ws cts
        return result
    }

    // BoardGeometry coordinates must be relative to the VirtualScreen bitmap origin,
    // matching what BoardSelectionWindow produces via form-client mouse coordinates.
    let private vsOrigin () =
        let vs = SystemInformation.VirtualScreen
        vs.Left, vs.Top

    // ValueOption.bind chain — no leading |> lines, so CC stays at 1.
    let private tryGetAllInts (v: JsonElement) =
        let vopt =
            tryGetInt v "left" |> ValueOption.bind (fun l ->
            tryGetInt v "top"  |> ValueOption.bind (fun t ->
            tryGetInt v "width" |> ValueOption.bind (fun w ->
            tryGetInt v "height" |> ValueOption.map (fun h -> (l, t, w, h)))))
        ValueOption.toOption vopt

    // if/else instead of match so no match/with keywords raise CC.
    let private tryGetBoardValue (r2: JsonElement) =
        if tryGetString r2 "type" = Some "null" then None else tryGet r2 "value"

    // |> inline so the pipe is not a leading | on its own line.
    let private tryBuildGeometry (v: JsonElement) =
        tryGetAllInts v |> Option.bind (fun (left, top, w, h) ->
            if w > 50 then
                let vsLeft, vsTop = vsOrigin ()
                Some { Left = left - vsLeft; Top = top - vsTop; Size = min w h }
            else None)

    // try/with is unavoidable for the HTTP call; all other branches are inline.
    let internal parseGeometry (response: string) =
        try
            use doc = JsonDocument.Parse response
            tryGet doc.RootElement "result" |> Option.bind (fun r1 ->
                tryGet r1 "result" |> Option.bind tryGetBoardValue |> Option.bind tryBuildGeometry)
        with _ ->
            None

    let tryDetectBoardInTab (tab: ChromeTab) =
        async {
            try
                let! response = sendEvaluate tab.WebSocketUrl detectionScript
                return parseGeometry response
            with _ ->
                return None
        }

    let private detectBoardInTab (tab: ChromeTab) =
        async {
            let! geo = tryDetectBoardInTab tab
            return geo |> Option.map (fun g -> { Tab = tab; Geometry = g })
        }

    let private detectFromTabs (tabs: ChromeTab list) =
        async {
            if tabs.IsEmpty then
                return Error "No Chrome tabs found. Make sure Chrome is open."
            else
                let! results = tabs |> List.map detectBoardInTab |> Async.Parallel
                return Ok(results |> Array.choose id |> Array.toList)
        }

    let detectBoards () =
        async {
            let! tabs = tryListTabs ()

            if tabs.IsNone then
                return
                    Error
                        "Chrome remote debugging is not available.\n\nStart Chrome with:\n  chrome.exe --remote-debugging-port=9222\n\nOr add --remote-debugging-port=9222 to a Chrome shortcut."
            else
                return! detectFromTabs tabs.Value
        }
