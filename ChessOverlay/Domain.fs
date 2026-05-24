namespace ChessOverlay

open System
open System.Drawing

type PieceColor =
    | Top
    | Bottom

type PieceKind =
    | Pawn
    | Knight
    | Bishop
    | Rook
    | Queen
    | King

type Square =
    {
        File: int
        Rank: int
    }

type Piece =
    {
        Color: PieceColor
        Kind: PieceKind
    }

type BoardState = Map<Square, Piece>

type BoardGeometry =
    {
        Left: int
        Top: int
        Size: int
    }

    member this.SquareSize = float this.Size / 8.0

    member this.GetSquareRectangle(square: Square) =
        let squareSize = this.SquareSize
        let x = float this.Left + float square.File * squareSize
        let y = float this.Top + float square.Rank * squareSize

        RectangleF(
            single x,
            single y,
            single squareSize,
            single squareSize)

type BoardReading =
    {
        Board: BoardState
        Confidence: float
    }

type DetectionResult =
    | BoardDetected of BoardGeometry
    | BoardNotFound

type OverlayFrame =
    {
        Geometry: BoardGeometry
        HighlightedSquares: Set<Square>
    }

module Squares =
    let all =
        [ for rank in 0 .. 7 do
              for file in 0 .. 7 do
                  { File = file; Rank = rank } ]

    let isValid square =
        square.File >= 0
        && square.File < 8
        && square.Rank >= 0
        && square.Rank < 8

    let tryCreate file rank =
        let square = { File = file; Rank = rank }

        if isValid square then
            Some square
        else
            None

    let name square =
        let file = char (int 'a' + square.File)
        let rank = 8 - square.Rank
        sprintf "%c%i" file rank

module BoardState =
    let empty: BoardState = Map.empty

    let tryPieceAt square (board: BoardState) =
        Map.tryFind square board

    let occupied square board =
        tryPieceAt square board |> Option.isSome

module Fen =
    let private pieceFromChar value =
        let color =
            if Char.IsUpper value then
                Bottom
            else
                Top

        let kind =
            match Char.ToLowerInvariant value with
            | 'p' -> Some Pawn
            | 'n' -> Some Knight
            | 'b' -> Some Bishop
            | 'r' -> Some Rook
            | 'q' -> Some Queen
            | 'k' -> Some King
            | _ -> None

        kind |> Option.map (fun pieceKind -> { Color = color; Kind = pieceKind })

    let parseBoard (fen: string) =
        if String.IsNullOrWhiteSpace fen then
            Error "FEN cannot be empty."
        else
            let placement = fen.Trim().Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)[0]
            let ranks = placement.Split('/')

            if ranks.Length <> 8 then
                Error "FEN board placement must contain 8 ranks."
            else
                let mutable board = Map.empty
                let mutable error: string option = None

                for rankIndex in 0 .. 7 do
                    let mutable fileIndex = 0

                    for value in ranks[rankIndex] do
                        if Char.IsDigit value then
                            fileIndex <- fileIndex + int (Char.GetNumericValue value)
                        else
                            match pieceFromChar value with
                            | Some piece when fileIndex < 8 ->
                                board <- Map.add { File = fileIndex; Rank = rankIndex } piece board
                                fileIndex <- fileIndex + 1
                            | Some _ -> error <- Some "FEN rank contains too many files."
                            | None -> error <- Some (sprintf "Unsupported FEN piece '%c'." value)

                    if fileIndex <> 8 then
                        error <- Some "Each FEN rank must describe exactly 8 files."

                match error with
                | Some message -> Error message
                | None -> Ok board
