namespace ChessOverlay.Tests

open System
open System.IO

module TestHelpers =
    let tempRoot suiteName =
        let uniqueName = Guid.NewGuid().ToString("N")
        let root = Path.Combine(Path.GetTempPath(), suiteName, uniqueName)
        Directory.CreateDirectory(root) |> ignore
        root

/// FsCheck generators shared across the property-test suites.
module Generators =
    open FsCheck.FSharp.GenBuilder
    open ChessOverlay

    module G = FsCheck.FSharp.Gen

    let genPieceColor = G.elements [ White; Black ]
    let genPieceKind = G.elements [ Pawn; Knight; Bishop; Rook; Queen; King ]

    let genPiece =
        gen {
            let! color = genPieceColor
            let! kind = genPieceKind
            return { Color = color; Kind = kind }
        }

    let genSquare =
        gen {
            let! file = G.choose (0, 7)
            let! rank = G.choose (0, 7)
            return { File = file; Rank = rank }
        }
