namespace ChessOverlay

open System
open System.Drawing
open System.Windows.Forms

type OverlayWindow() as this =
    inherit Form()

    let highlightColor = Color.FromArgb(96, 220, 30, 30)
    let outlineColor = Color.FromArgb(220, 255, 64, 64)
    let statusBackColor = Color.FromArgb(235, 24, 24, 24)
    let statusTextColor = Color.White
    let mutable frame: OverlayFrame option = None
    let mutable statusText = "Searching for chessboard..."
    let virtualBounds = SystemInformation.VirtualScreen

    do
        this.FormBorderStyle <- FormBorderStyle.None
        this.StartPosition <- FormStartPosition.Manual
        this.Bounds <- virtualBounds
        this.TopMost <- true
        this.ShowInTaskbar <- false
        this.BackColor <- Color.Magenta
        this.TransparencyKey <- Color.Magenta
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

    member _.ShowFrame(nextFrame: OverlayFrame) =
        frame <- Some nextFrame
        statusText <- sprintf "Board detected - %i attacked squares" nextFrame.HighlightedSquares.Count
        this.Invalidate()

    member _.ShowSearching() =
        frame <- None
        statusText <- "Searching for chessboard..."
        this.Invalidate()

    member _.ShowUncertainBoard(geometry: BoardGeometry) =
        frame <-
            Some
                {
                    Geometry = geometry
                    HighlightedSquares = Set.empty
                }

        statusText <- "Board found - reading pieces..."
        this.Invalidate()

    override _.OnPaint(args) =
        base.OnPaint args
        args.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        args.Graphics.TextRenderingHint <- Text.TextRenderingHint.ClearTypeGridFit

        match frame with
        | None -> this.PaintStatus args.Graphics
        | Some current ->
            use brush = new SolidBrush(highlightColor)
            use outlinePen = new Pen(outlineColor, 3.0f)

            for square in current.HighlightedSquares do
                let rect: RectangleF = this.ToClientRectangle(current.Geometry.GetSquareRectangle square)
                args.Graphics.FillRectangle(brush, rect)

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

            this.PaintStatus args.Graphics

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

and private NativeMethods =
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

    static member GWL_EXSTYLE = GWL_EXSTYLE_VALUE
    static member WS_EX_LAYERED = WS_EX_LAYERED_VALUE
    static member WS_EX_TRANSPARENT = WS_EX_TRANSPARENT_VALUE
    static member WS_EX_TOOLWINDOW = WS_EX_TOOLWINDOW_VALUE
    static member getWindowLong handle index = GetWindowLong(handle, index)
    static member setWindowLong handle index value = SetWindowLong(handle, index, value)
    static member tryExcludeFromCapture handle =
        try
            SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE_VALUE) |> ignore
        with _ ->
            ()
