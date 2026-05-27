namespace ChessOverlay

open System
open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.Windows.Forms

[<ExcludeFromCodeCoverage>]
type OverlayWindow() as this =
    inherit Form()

    let transparentColor = Color.Magenta
    let arrowColor = Color.FromArgb(255, 210, 30, 30)
    let friendlyForkMoveColor = Color.FromArgb(255, 45, 135, 255)
    let outlineColor = Color.FromArgb(220, 255, 64, 64)
    let hangingColor = Color.FromArgb(210, 255, 140, 0)
    let enemyHangingColor = Color.FromArgb(220, 60, 210, 255)
    let forkColor = Color.FromArgb(230, 255, 215, 0)
    let statusBackColor = Color.FromArgb(235, 24, 24, 24)
    let statusTextColor = Color.White
    let mutable frame: OverlayFrame option = None
    let mutable statusText = "Press Ctrl+Shift+B to select the board area"
    let mutable overlayUiVisible = true
    let virtualBounds = SystemInformation.VirtualScreen
    let hotkeyId = 1
    let toggleHotkeyId = 2
    let selectBoardRequested = Event<unit>()
    let toggleOverlayRequested = Event<unit>()

    do
        this.FormBorderStyle <- FormBorderStyle.None
        this.StartPosition <- FormStartPosition.Manual
        this.Bounds <- virtualBounds
        this.TopMost <- true
        this.ShowInTaskbar <- false
        this.BackColor <- transparentColor
        this.TransparencyKey <- transparentColor
        this.Opacity <- 0.6
        this.DoubleBuffered <- true
        this.Text <- "Chess Overlay"

        let initialStyle = NativeMethods.getWindowLong this.Handle NativeMethods.GWL_EXSTYLE

        NativeMethods.setWindowLong
            this.Handle
            NativeMethods.GWL_EXSTYLE
            (initialStyle
             ||| NativeMethods.WS_EX_LAYERED
             ||| NativeMethods.WS_EX_TRANSPARENT
             ||| NativeMethods.WS_EX_TOOLWINDOW)
        |> ignore

        NativeMethods.tryExcludeFromCapture this.Handle
        NativeMethods.registerHotKey this.Handle hotkeyId 0x0006u 0x42u |> ignore
        NativeMethods.registerHotKey this.Handle toggleHotkeyId 0x0006u 0x4Fu |> ignore

        this.FormClosed.Add(fun _ ->
            NativeMethods.unregisterHotKey this.Handle hotkeyId |> ignore
            NativeMethods.unregisterHotKey this.Handle toggleHotkeyId |> ignore)

    member _.SelectBoardRequested = selectBoardRequested.Publish
    member _.ToggleOverlayRequested = toggleOverlayRequested.Publish

    override _.WndProc(m: byref<Message>) =
        if m.Msg = 0x0312 then
            if m.WParam.ToInt32() = hotkeyId then
                selectBoardRequested.Trigger()
            elif m.WParam.ToInt32() = toggleHotkeyId then
                toggleOverlayRequested.Trigger()

        base.WndProc(&m)

    member _.ShowFrame(nextFrame: OverlayFrame) =
        frame <- Some nextFrame
        let attackedCount =
            match nextFrame.DetectedPieces with
            | Some board -> (AttackCalculator.enemyAttackedSquares board).Count
            | None -> 0

        let strategy =
            nextFrame.Strategy
            |> Option.map (sprintf "Reader: %s - ")
            |> Option.defaultValue ""

        statusText <-
            if nextFrame.ForkSquares.IsEmpty then
                sprintf "%s%i attacked squares" strategy attackedCount
            else
                sprintf "%s%i attacked squares, %i fork(s)" strategy attackedCount nextFrame.ForkSquares.Count
        this.Invalidate()

    member _.ShowStatus(message: string) =
        statusText <- message
        this.Invalidate()

    member _.ClearFrame() =
        frame <- None
        this.Invalidate()

    member _.HideOverlayUi() =
        overlayUiVisible <- false
        frame <- None
        this.Invalidate()

    member _.ShowOverlayUi() =
        overlayUiVisible <- true
        this.Invalidate()

    member _.ShowUncertainBoard(geometry: BoardGeometry, ?message: string) =
        frame <-
            Some
                {
                    Geometry = geometry
                    AttackArrows = []
                    FriendlyForkMoveArrows = []
                    EnemyForkMoveArrows = []
                    HangingSquares = Set.empty
                    EnemyHangingSquares = Set.empty
                    ForkSquares = Set.empty
                    DetectedPieces = None
                    Strategy = None
                }

        statusText <- defaultArg message "Board selected - reading pieces..."
        this.Invalidate()

    override _.OnPaint(args) =
        base.OnPaint args
        if overlayUiVisible then
            args.Graphics.CompositingQuality <- Drawing2D.CompositingQuality.HighQuality
            args.Graphics.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
            args.Graphics.PixelOffsetMode <- Drawing2D.PixelOffsetMode.HighQuality
            args.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            args.Graphics.TextRenderingHint <- Text.TextRenderingHint.ClearTypeGridFit

            match frame with
            | None -> this.PaintStatus args.Graphics
            | Some current ->
                let penWidth = single current.Geometry.SquareSize * 0.035f
                use arrowPen = new Pen(arrowColor, penWidth)
                use arrowCap = new Drawing2D.AdjustableArrowCap(3.5f, 3.5f, true)
                arrowPen.StartCap <- Drawing2D.LineCap.Round
                arrowPen.LineJoin <- Drawing2D.LineJoin.Round
                arrowPen.CustomEndCap <- arrowCap
                use outlinePen = new Pen(outlineColor, 3.0f)

                for (fromSq, toSq) in current.AttackArrows do
                    let fromRect = this.ToClientRectangle(current.Geometry.GetSquareRectangle fromSq)
                    let toRect = this.ToClientRectangle(current.Geometry.GetSquareRectangle toSq)
                    let fromCenter = PointF(fromRect.X + fromRect.Width / 2.0f, fromRect.Y + fromRect.Height / 2.0f)
                    let toCenter = PointF(toRect.X + toRect.Width / 2.0f, toRect.Y + toRect.Height / 2.0f)
                    args.Graphics.DrawLine(arrowPen, fromCenter, toCenter)

                use friendlyForkMovePen = new Pen(friendlyForkMoveColor, penWidth * 1.4f)
                use friendlyForkMoveCap = new Drawing2D.AdjustableArrowCap(3.8f, 3.8f, true)
                friendlyForkMovePen.StartCap <- Drawing2D.LineCap.Round
                friendlyForkMovePen.LineJoin <- Drawing2D.LineJoin.Round
                friendlyForkMovePen.CustomEndCap <- friendlyForkMoveCap

                for (fromSq, toSq) in current.FriendlyForkMoveArrows do
                    let fromRect = this.ToClientRectangle(current.Geometry.GetSquareRectangle fromSq)
                    let toRect = this.ToClientRectangle(current.Geometry.GetSquareRectangle toSq)
                    let fromCenter = PointF(fromRect.X + fromRect.Width / 2.0f, fromRect.Y + fromRect.Height / 2.0f)
                    let toCenter = PointF(toRect.X + toRect.Width / 2.0f, toRect.Y + toRect.Height / 2.0f)
                    args.Graphics.DrawLine(friendlyForkMovePen, fromCenter, toCenter)

                use enemyForkMovePen = new Pen(forkColor, penWidth * 1.4f)
                use enemyForkMoveCap = new Drawing2D.AdjustableArrowCap(3.8f, 3.8f, true)
                enemyForkMovePen.StartCap <- Drawing2D.LineCap.Round
                enemyForkMovePen.LineJoin <- Drawing2D.LineJoin.Round
                enemyForkMovePen.CustomEndCap <- enemyForkMoveCap

                for (fromSq, toSq) in current.EnemyForkMoveArrows do
                    let fromRect = this.ToClientRectangle(current.Geometry.GetSquareRectangle fromSq)
                    let toRect = this.ToClientRectangle(current.Geometry.GetSquareRectangle toSq)
                    let fromCenter = PointF(fromRect.X + fromRect.Width / 2.0f, fromRect.Y + fromRect.Height / 2.0f)
                    let toCenter = PointF(toRect.X + toRect.Width / 2.0f, toRect.Y + toRect.Height / 2.0f)
                    args.Graphics.DrawLine(enemyForkMovePen, fromCenter, toCenter)

                use hangingPen = new Pen(hangingColor, penWidth * 1.8f)

                for sq in current.HangingSquares do
                    let rect = this.ToClientRectangle(current.Geometry.GetSquareRectangle sq)
                    let inset = rect.Width * 0.1f
                    args.Graphics.DrawEllipse(
                        hangingPen,
                        rect.X + inset,
                        rect.Y + inset,
                        rect.Width - 2.0f * inset,
                        rect.Height - 2.0f * inset)

                use enemyHangingPen = new Pen(enemyHangingColor, penWidth * 1.8f)
                enemyHangingPen.DashStyle <- Drawing2D.DashStyle.Dash

                for sq in current.EnemyHangingSquares do
                    let rect = this.ToClientRectangle(current.Geometry.GetSquareRectangle sq)
                    let inset = rect.Width * 0.18f
                    args.Graphics.DrawEllipse(
                        enemyHangingPen,
                        rect.X + inset,
                        rect.Y + inset,
                        rect.Width - 2.0f * inset,
                        rect.Height - 2.0f * inset)

                // A fork is an enemy piece hitting two or more friendly pieces; mark
                // its square so the more dangerous threat stands out from plain attacks.
                use forkPen = new Pen(forkColor, penWidth * 1.8f)

                for sq in current.ForkSquares do
                    let rect = this.ToClientRectangle(current.Geometry.GetSquareRectangle sq)
                    let inset = rect.Width * 0.1f
                    args.Graphics.DrawRectangle(
                        forkPen,
                        rect.X + inset,
                        rect.Y + inset,
                        rect.Width - 2.0f * inset,
                        rect.Height - 2.0f * inset)

                let boardRect =
                    RectangleF(
                        single (current.Geometry.Left - virtualBounds.Left),
                        single (current.Geometry.Top - virtualBounds.Top),
                        single current.Geometry.Size,
                        single current.Geometry.Size)

                args.Graphics.DrawRectangle(
                    outlinePen,
                    boardRect.X,
                    boardRect.Y,
                    boardRect.Width,
                    boardRect.Height)

                match current.DetectedPieces with
                | None -> ()
                | Some pieces -> this.PaintPieceLabels(args.Graphics, current.Geometry, pieces)

                this.PaintStatus args.Graphics

    member private _.PieceNotation(piece: Piece) =
        let letter =
            match piece.Kind with
            | King -> "K"
            | Queen -> "Q"
            | Rook -> "R"
            | Bishop -> "B"
            | Knight -> "N"
            | Pawn -> "P"

        if piece.Color = Black then letter.ToLowerInvariant() else letter

    member private this.PaintPieceLabels(graphics: Graphics, geometry: BoardGeometry, pieces: BoardState) =
        let fontSize = single geometry.SquareSize * 0.38f
        use font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel)

        for KeyValue(square, piece) in pieces do
            let rect = this.ToClientRectangle(geometry.GetSquareRectangle square)
            let notation = this.PieceNotation piece
            let textSize = graphics.MeasureString(notation, font)
            let tx = rect.X + (rect.Width - textSize.Width) / 2.0f
            let ty = rect.Y + (rect.Height - textSize.Height) / 2.0f

            let textColor, shadowColor =
                if piece.Color = White then
                    Color.FromArgb(240, 255, 255, 255), Color.FromArgb(200, 20, 20, 20)
                else
                    Color.FromArgb(240, 24, 24, 24), Color.FromArgb(200, 240, 240, 240)

            use shadowBrush = new SolidBrush(shadowColor)

            for dx in [ -1; 0; 1 ] do
                for dy in [ -1; 0; 1 ] do
                    if dx <> 0 || dy <> 0 then
                        graphics.DrawString(notation, font, shadowBrush, tx + single dx, ty + single dy)

            use textBrush = new SolidBrush(textColor)
            graphics.DrawString(notation, font, textBrush, tx, ty)

    member private _.ToClientRectangle(rectangle: RectangleF) : RectangleF =
        RectangleF(
            rectangle.X - single virtualBounds.Left,
            rectangle.Y - single virtualBounds.Top,
            rectangle.Width,
            rectangle.Height)

    member private _.PaintStatus(graphics: Graphics) =
        use font = new Font("Segoe UI", 11.0f, FontStyle.Bold, GraphicsUnit.Point)
        let padding = 12.0f
        let stripeWidth = 6.0f
        let measured = graphics.MeasureString(statusText, font)
        let width = measured.Width + padding * 2.0f + stripeWidth
        let height = max 38.0f (measured.Height + padding)
        let primaryBounds = Screen.PrimaryScreen.Bounds
        let x = single (primaryBounds.Left - virtualBounds.Left) + 18.0f
        let y = single (primaryBounds.Top - virtualBounds.Top) + 18.0f
        let rect = RectangleF(x, y, width, height)

        use backBrush = new SolidBrush(statusBackColor)
        use stripeBrush = new SolidBrush(Color.FromArgb(255, 220, 30, 30))
        use textBrush = new SolidBrush(statusTextColor)

        graphics.FillRectangle(backBrush, rect)
        graphics.FillRectangle(stripeBrush, RectangleF(x, y, stripeWidth, height))
        graphics.DrawString(statusText, font, textBrush, x + stripeWidth + padding, y + 8.0f)

and [<ExcludeFromCodeCoverage>] private NativeMethods =
    [<Literal>]
    static let GWL_EXSTYLE_VALUE = -20

    [<Literal>]
    static let WS_EX_LAYERED_VALUE = 0x00080000

    [<Literal>]
    static let WS_EX_TRANSPARENT_VALUE = 0x00000020

    [<Literal>]
    static let WS_EX_TOOLWINDOW_VALUE = 0x00000080

    [<Literal>]
    static let WDA_EXCLUDEFROMCAPTURE_VALUE = 0x00000011u

    [<System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong")>]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex)

    [<System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong")>]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)

    [<System.Runtime.InteropServices.DllImport("user32.dll")>]
    static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint32 dwAffinity)

    [<System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "RegisterHotKey")>]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint32 fsModifiers, uint32 vk)

    [<System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "UnregisterHotKey")>]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id)

    static member GWL_EXSTYLE = GWL_EXSTYLE_VALUE
    static member WS_EX_LAYERED = WS_EX_LAYERED_VALUE
    static member WS_EX_TRANSPARENT = WS_EX_TRANSPARENT_VALUE
    static member WS_EX_TOOLWINDOW = WS_EX_TOOLWINDOW_VALUE
    static member getWindowLong handle index = GetWindowLong(handle, index)
    static member setWindowLong handle index value = SetWindowLong(handle, index, value)
    static member registerHotKey handle id modifiers vk = RegisterHotKey(handle, id, modifiers, vk)
    static member unregisterHotKey handle id = UnregisterHotKey(handle, id)
    static member tryExcludeFromCapture handle =
        try
            SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE_VALUE) |> ignore
        with _ ->
            ()
