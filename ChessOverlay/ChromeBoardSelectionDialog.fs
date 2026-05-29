namespace ChessOverlay

open System
open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.Windows.Forms

module internal ChromeBoardSelection =

    type ScanView =
        { Status: string
          Boards: ChromeBoardDetector.DetectedBoard list
          Items: string list
          SelectSingleBoard: bool }

    let private emptyView status =
        { Status = status
          Boards = []
          Items = []
          SelectSingleBoard = false }

    let private boardHost (url: string) =
        try
            Uri(url).Host
        with _ ->
            url

    let private boardItemText (board: ChromeBoardDetector.DetectedBoard) =
        sprintf "%s  —  %s  (%d px)" board.Tab.Title (boardHost board.Tab.Url) board.Geometry.Size

    let private boardsView boards =
        { Status = sprintf "Found %d chess board(s). Select one and click Use." (List.length boards)
          Boards = boards
          Items = boards |> List.map boardItemText
          SelectSingleBoard = List.length boards = 1 }

    let private noBoardsView () =
        emptyView
            "No chess boards detected in Chrome.\n\nOpen a game on chess.com or lichess.org and click Refresh."

    let private viewForBoards boards =
        if List.isEmpty boards then noBoardsView () else boardsView boards

    let viewForResult result =
        result |> Result.map viewForBoards |> Result.defaultWith emptyView

    let viewForException (ex: exn) =
        emptyView (sprintf "Detection failed: %s" ex.Message)

[<ExcludeFromCodeCoverage>]
type ChromeBoardSelectionDialog() as this =
    inherit Form()

    let mutable selectedGeometry: BoardGeometry option = None
    let mutable wantsManualSelection = false
    let mutable detectedBoards: ChromeBoardDetector.DetectedBoard list = []

    let statusLabel =
        new Label(
            Dock = DockStyle.Fill,
            Padding = Padding(10, 10, 10, 6),
            Font = new Font("Segoe UI", 9.0f),
            ForeColor = Color.FromArgb(210, 210, 210),
            BackColor = Color.FromArgb(45, 45, 48),
            TextAlign = ContentAlignment.MiddleLeft)

    let listBox =
        new ListBox(
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10.5f),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            ItemHeight = 28)

    let useButton = new Button(Text = "Use Selected Board", Enabled = false, Width = 150, Height = 30)
    let refreshButton = new Button(Text = "Refresh", Width = 80, Height = 30)
    let manualButton = new Button(Text = "Manual Selection", Width = 130, Height = 30)
    let cancelButton = new Button(Text = "Cancel", Width = 75, Height = 30)

    let updateStatus msg = statusLabel.Text <- msg

    let beginOnUiThread action =
        if this.IsHandleCreated && not this.IsDisposed then
            try
                this.BeginInvoke(
                    Action(fun () ->
                        try
                            if not this.IsDisposed then
                                action ()
                        with
                        | :? ObjectDisposedException
                        | :? InvalidOperationException -> ()))
                |> ignore
            with
            | :? ObjectDisposedException
            | :? InvalidOperationException -> ()

    let prepareForScan () =
        updateStatus "Scanning Chrome tabs for chess boards..."
        listBox.Items.Clear()
        detectedBoards <- []
        useButton.Enabled <- false
        refreshButton.Enabled <- false

    let applyScanView (view: ChromeBoardSelection.ScanView) =
        refreshButton.Enabled <- true
        detectedBoards <- view.Boards
        listBox.Items.Clear()

        for item in view.Items do
            listBox.Items.Add item |> ignore

        if view.SelectSingleBoard then
            listBox.SelectedIndex <- 0

        useButton.Enabled <- listBox.SelectedIndex >= 0
        updateStatus view.Status

    let scan () =
        prepareForScan ()

        Async.StartWithContinuations(
            ChromeBoardDetector.detectBoards (),
            (fun result ->
                beginOnUiThread (fun () -> result |> ChromeBoardSelection.viewForResult |> applyScanView)),
            (fun ex ->
                beginOnUiThread (fun () -> ex |> ChromeBoardSelection.viewForException |> applyScanView)),
            (fun _ -> ()))

    do
        this.Text <- "Detect Chess Board in Chrome"
        this.Size <- Size(520, 360)
        this.FormBorderStyle <- FormBorderStyle.FixedDialog
        this.StartPosition <- FormStartPosition.CenterScreen
        this.MaximizeBox <- false
        this.MinimizeBox <- false
        this.TopMost <- true
        this.BackColor <- Color.FromArgb(25, 25, 28)
        this.ForeColor <- Color.White

        listBox.SelectedIndexChanged.Add(fun _ ->
            useButton.Enabled <- listBox.SelectedIndex >= 0)

        listBox.DoubleClick.Add(fun _ ->
            if listBox.SelectedIndex >= 0 then
                useButton.PerformClick())

        useButton.Click.Add(fun _ ->
            let idx = listBox.SelectedIndex

            if idx >= 0 && idx < detectedBoards.Length then
                selectedGeometry <- Some detectedBoards[idx].Geometry
                this.DialogResult <- DialogResult.OK
                this.Close())

        refreshButton.Click.Add(fun _ -> scan ())

        manualButton.Click.Add(fun _ ->
            wantsManualSelection <- true
            this.DialogResult <- DialogResult.Cancel
            this.Close())

        cancelButton.Click.Add(fun _ ->
            this.DialogResult <- DialogResult.Cancel
            this.Close())

        // Layout: status row (fixed) | list (fill) | button row (fixed)
        let layout = new TableLayoutPanel(Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = Padding 0)
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70.0f)) |> ignore
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f)) |> ignore
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44.0f)) |> ignore

        layout.Controls.Add(statusLabel, 0, 0)
        layout.Controls.Add(listBox, 0, 1)

        let btnPanel = new Panel(Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 35, 38))

        useButton.Location <- Point(8, 7)
        refreshButton.Location <- Point(166, 7)

        // Right-anchored buttons
        manualButton.Anchor <- AnchorStyles.Top ||| AnchorStyles.Right
        cancelButton.Anchor <- AnchorStyles.Top ||| AnchorStyles.Right
        manualButton.Location <- Point(btnPanel.Width - 218, 7)
        cancelButton.Location <- Point(btnPanel.Width - 82, 7)

        btnPanel.Controls.AddRange(
            [| useButton :> Control
               refreshButton :> Control
               manualButton :> Control
               cancelButton :> Control |])

        layout.Controls.Add(btnPanel, 0, 2)
        this.Controls.Add layout

        // Esc cancels; the dialog is launched from a global hotkey so it is not
        // automatically the foreground window - force focus once shown.
        this.CancelButton <- cancelButton
        this.Shown.Add(fun _ -> SelectionNative.forceForeground this)
        this.Load.Add(fun _ -> scan ())

    member _.SelectedGeometry = selectedGeometry
    member _.WantsManualSelection = wantsManualSelection
