namespace ChessOverlay.Tests

open System
open System.IO

module TestHelpers =
    let tempRoot suiteName =
        let uniqueName = Guid.NewGuid().ToString("N")
        let root = Path.Combine(Path.GetTempPath(), suiteName, uniqueName)
        Directory.CreateDirectory(root) |> ignore
        root
