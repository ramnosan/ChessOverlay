namespace ChessOverlay

open System
open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.Windows.Forms

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

    let scan () =
        updateStatus "Scanning Chrome tabs for chess boards..."
        listBox.Items.Clear()
        detectedBoards <- []
        useButton.Enabled <- false
        refreshButton.Enabled <- false

        Async.StartWithContinuations(
            ChromeBoardDetector.detectBoards (),
            (fun result ->
                if not this.IsDisposed then
                    this.BeginInvoke(
                        Action(fun () ->
                            refreshButton.Enabled <- true

                            match result with
                            | Error msg -> updateStatus msg
                            | Ok [] ->
                                updateStatus
                                    "No chess boards detected in Chrome.\n\nOpen a game on chess.com or lichess.org and click Refresh."
                            | Ok boards ->
                                detectedBoards <- boards

                                for board in boards do
                                    let host =
                                        try
                                            Uri(board.Tab.Url).Host
                                        with _ ->
                                            board.Tab.Url

                                    listBox.Items.Add(
                                        sprintf "%s  —  %s  (%d px)" board.Tab.Title host board.Geometry.Size)
                                    |> ignore

                                if boards.Length = 1 then
                                    listBox.SelectedIndex <- 0

                                updateStatus (
                                    sprintf "Found %d chess board(s). Select one and click Use." boards.Length)))
                    |> ignore),
            (fun ex ->
                if not this.IsDisposed then
                    this.BeginInvoke(
                        Action(fun () ->
                            refreshButton.Enabled <- true
                            updateStatus (sprintf "Detection failed: %s" ex.Message)))
                    |> ignore),
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

        this.Load.Add(fun _ -> scan ())

    member _.SelectedGeometry = selectedGeometry
    member _.WantsManualSelection = wantsManualSelection
