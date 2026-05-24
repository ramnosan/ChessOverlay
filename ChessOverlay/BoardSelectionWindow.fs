namespace ChessOverlay

open System
open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.Windows.Forms

module BoardSelectionGeometry =
    let clientToCaptureGeometry (rectangle: Rectangle) =
        {
            Left = rectangle.Left
            Top = rectangle.Top
            Size = rectangle.Width
        }

    let selectionFromPoints (startPoint: Point) (endPoint: Point) =
        let deltaX = endPoint.X - startPoint.X
        let deltaY = endPoint.Y - startPoint.Y
        let size = min (abs deltaX) (abs deltaY)
        let left =
            if deltaX < 0 then
                startPoint.X - size
            else
                startPoint.X

        let top =
            if deltaY < 0 then
                startPoint.Y - size
            else
                startPoint.Y

        Rectangle(left, top, size, size)

    let currentSelection dragStart currentPoint =
        match dragStart, currentPoint with
        | Some startPoint, Some endPoint -> Some(selectionFromPoints startPoint endPoint)
        | _ -> None

[<ExcludeFromCodeCoverage>]
type BoardSelectionWindow() as this =
    inherit Form()

    let virtualBounds = SystemInformation.VirtualScreen
    let minimumSelectionSize = 80
    let mutable dragStart: Point option = None
    let mutable currentPoint: Point option = None
    let mutable selectedGeometry: BoardGeometry option = None

    let currentSelection () =
        BoardSelectionGeometry.currentSelection dragStart currentPoint

    do
        this.FormBorderStyle <- FormBorderStyle.None
        this.StartPosition <- FormStartPosition.Manual
        this.Bounds <- virtualBounds
        this.TopMost <- true
        this.ShowInTaskbar <- false
        this.BackColor <- Color.Black
        this.Opacity <- 0.42
        this.DoubleBuffered <- true
        this.Cursor <- Cursors.Cross
        this.KeyPreview <- true
        this.Text <- "Select chessboard"

    member _.SelectedGeometry = selectedGeometry

    override _.OnMouseDown(args) =
        base.OnMouseDown args

        if args.Button = MouseButtons.Left then
            dragStart <- Some args.Location
            currentPoint <- Some args.Location
            this.Invalidate()

    override _.OnMouseMove(args) =
        base.OnMouseMove args

        if dragStart.IsSome then
            currentPoint <- Some args.Location
            this.Invalidate()

    override _.OnMouseUp(args) =
        base.OnMouseUp args

        if args.Button = MouseButtons.Left then
            currentPoint <- Some args.Location

            match currentSelection () with
            | Some rectangle when rectangle.Width >= minimumSelectionSize ->
                selectedGeometry <- Some(BoardSelectionGeometry.clientToCaptureGeometry rectangle)
                this.DialogResult <- DialogResult.OK
                this.Close()
            | _ ->
                dragStart <- None
                currentPoint <- None
                this.Invalidate()

    override _.OnKeyDown(args) =
        base.OnKeyDown args

        if args.KeyCode = Keys.Escape then
            selectedGeometry <- None
            this.DialogResult <- DialogResult.Cancel
            this.Close()

    override _.OnPaint(args) =
        base.OnPaint args
        args.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        args.Graphics.TextRenderingHint <- Text.TextRenderingHint.ClearTypeGridFit

        use font = new Font("Segoe UI", 13.0f, FontStyle.Bold, GraphicsUnit.Point)
        use subFont = new Font("Segoe UI", 10.0f, FontStyle.Regular, GraphicsUnit.Point)
        use textBrush = new SolidBrush(Color.White)
        use backBrush = new SolidBrush(Color.FromArgb(210, 20, 20, 20))

        let instruction = "Drag a square around the chessboard"
        let hint = "Release to start. Press Esc to cancel."
        let padding = 14.0f
        let instructionSize = args.Graphics.MeasureString(instruction, font)
        let hintSize = args.Graphics.MeasureString(hint, subFont)
        let width = max instructionSize.Width hintSize.Width + padding * 2.0f
        let height = instructionSize.Height + hintSize.Height + padding * 2.0f + 4.0f
        let x = 18.0f
        let y = 18.0f
        let panel = RectangleF(x, y, width, height)

        args.Graphics.FillRectangle(backBrush, panel)
        args.Graphics.DrawString(instruction, font, textBrush, x + padding, y + padding)
        args.Graphics.DrawString(hint, subFont, textBrush, x + padding, y + padding + instructionSize.Height + 4.0f)

        match currentSelection () with
        | Some rectangle when rectangle.Width > 0 ->
            use fillBrush = new SolidBrush(Color.FromArgb(70, 255, 64, 64))
            use outlinePen = new Pen(Color.FromArgb(255, 255, 64, 64), 3.0f)
            use gridPen = new Pen(Color.FromArgb(190, 255, 255, 255), 1.0f)

            args.Graphics.FillRectangle(fillBrush, rectangle)
            args.Graphics.DrawRectangle(outlinePen, rectangle)

            let squareSize = single rectangle.Width / 8.0f

            for index in 1 .. 7 do
                let offset = single index * squareSize
                args.Graphics.DrawLine(
                    gridPen,
                    single rectangle.Left + offset,
                    single rectangle.Top,
                    single rectangle.Left + offset,
                    single rectangle.Bottom)

                args.Graphics.DrawLine(
                    gridPen,
                    single rectangle.Left,
                    single rectangle.Top + offset,
                    single rectangle.Right,
                    single rectangle.Top + offset)
        | _ -> ()
