namespace ChessOverlay

open System.Diagnostics.CodeAnalysis
open System.Drawing

type IBoardDetector =
    abstract Detect: Bitmap -> DetectionResult

type IBoardReader =
    abstract Read: Bitmap * BoardGeometry -> BoardReading option

type FenBoardReader(fen: string) =
    interface IBoardReader with
        member _.Read(_, _) =
            match Fen.parseBoard fen with
            | Ok board -> Some { Board = board; Confidence = 1.0 }
            | Error _ -> None

type UncertainBoardReader() =
    interface IBoardReader with
        member _.Read(_, _) = None

type FixedBoardDetector(geometry: BoardGeometry) =
    member _.Geometry = geometry

    interface IBoardDetector with
        member _.Detect(_) = BoardDetected geometry

[<ExcludeFromCodeCoverage>]
module ScreenCapture =
    let captureVirtualScreen () =
        let bounds = System.Windows.Forms.SystemInformation.VirtualScreen
        let bitmap = new Bitmap(bounds.Width, bounds.Height)

        use graphics = Graphics.FromImage bitmap
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size)
        bitmap, bounds.Location
