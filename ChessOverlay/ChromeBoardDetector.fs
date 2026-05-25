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
        match el.TryGetProperty name with
        | true, v -> Some v
        | _ -> None

    let private tryGetString (el: JsonElement) (name: string) =
        match tryGet el name with
        | Some v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None

    let private tryGetInt (el: JsonElement) (name: string) =
        match tryGet el name with
        | Some v -> ValueSome(v.GetInt32())
        | _ -> ValueNone

    let private tryListTabs () =
        async {
            try
                let url = sprintf "http://localhost:%d/json/list" cdpPort
                let! json = http.Value.GetStringAsync url |> Async.AwaitTask
                use doc = JsonDocument.Parse json

                let tabs =
                    doc.RootElement.EnumerateArray()
                    |> Seq.choose (fun tab ->
                        match tryGetString tab "type" with
                        | Some "page" ->
                            match
                                tryGetString tab "id",
                                tryGetString tab "title",
                                tryGetString tab "url",
                                tryGetString tab "webSocketDebuggerUrl"
                            with
                            | Some id, Some title, Some url, Some wsUrl ->
                                Some { Id = id; Title = title; Url = url; WebSocketUrl = wsUrl }
                            | _ -> None
                        | _ -> None)
                    |> Seq.toList

                return Some tabs
            with _ ->
                return None
        }

    let private evaluate (wsUrl: string) (expression: string) =
        async {
            use ws = new ClientWebSocket()
            use cts = new CancellationTokenSource(TimeSpan.FromSeconds 5.0)

            try
                do! ws.ConnectAsync(Uri wsUrl, cts.Token) |> Async.AwaitTask

                let msg =
                    sprintf
                        """{"id":1,"method":"Runtime.evaluate","params":{"expression":%s,"returnByValue":true}}"""
                        (JsonSerializer.Serialize expression)

                let bytes = Encoding.UTF8.GetBytes msg
                do! ws.SendAsync(ArraySegment bytes, WebSocketMessageType.Text, true, cts.Token) |> Async.AwaitTask

                let buf = Array.zeroCreate<byte> 65536
                let sb = StringBuilder()
                let mutable isDone = false

                while not isDone do
                    let! res = ws.ReceiveAsync(ArraySegment buf, cts.Token) |> Async.AwaitTask
                    sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count)) |> ignore
                    isDone <- res.EndOfMessage

                return Some(sb.ToString())
            with _ ->
                return None
        }

    // BoardGeometry coordinates must be relative to the VirtualScreen bitmap origin,
    // matching what BoardSelectionWindow produces via form-client mouse coordinates.
    let private vsOrigin () =
        let vs = SystemInformation.VirtualScreen
        vs.Left, vs.Top

    let private parseGeometry (response: string) =
        try
            use doc = JsonDocument.Parse response

            match tryGet doc.RootElement "result" with
            | Some r1 ->
                match tryGet r1 "result" with
                | Some r2 ->
                    match tryGetString r2 "type" with
                    | Some "null" -> None
                    | _ ->
                        match tryGet r2 "value" with
                        | Some v ->
                            match
                                tryGetInt v "left",
                                tryGetInt v "top",
                                tryGetInt v "width",
                                tryGetInt v "height"
                            with
                            | ValueSome left, ValueSome top, ValueSome w, ValueSome h when w > 50 ->
                                let vsLeft, vsTop = vsOrigin ()
                                Some { Left = left - vsLeft; Top = top - vsTop; Size = min w h }
                            | _ -> None
                        | None -> None
                | None -> None
            | None -> None
        with _ ->
            None

    let tryDetectBoardInTab (tab: ChromeTab) =
        async {
            match! evaluate tab.WebSocketUrl detectionScript with
            | Some response -> return parseGeometry response
            | None -> return None
        }

    let detectBoards () =
        async {
            match! tryListTabs () with
            | None ->
                return
                    Error
                        "Chrome remote debugging is not available.\n\nStart Chrome with:\n  chrome.exe --remote-debugging-port=9222\n\nOr add --remote-debugging-port=9222 to a Chrome shortcut."
            | Some [] ->
                return Error "No Chrome tabs found. Make sure Chrome is open."
            | Some tabs ->
                let! results =
                    tabs
                    |> List.map (fun tab ->
                        async {
                            let! geo = tryDetectBoardInTab tab
                            return geo |> Option.map (fun g -> { Tab = tab; Geometry = g })
                        })
                    |> Async.Parallel

                return Ok(results |> Array.choose id |> Array.toList)
        }
