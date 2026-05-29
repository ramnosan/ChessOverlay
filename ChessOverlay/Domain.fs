namespace ChessOverlay

open System
open System.Drawing
open System.Text

type PieceColor =
    | White
    | Black

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

    [<System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage>]
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
        Candidates: Map<Square, PieceMatchCandidate list>
        Strategy: string
    }

and PieceMatchCandidate =
    {
        Piece: Piece
        Score: float
    }

type OverlayFrame =
    {
        Geometry: BoardGeometry
        AttackArrows: (Square * Square) list
        FriendlyForkMoveArrows: (Square * Square) list
        EnemyForkMoveArrows: (Square * Square) list
        HangingSquares: Set<Square>
        EnemyHangingSquares: Set<Square>
        ForkSquares: Set<Square>
        DetectedPieces: BoardState option
        Strategy: string option
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
    let private pieceKinds =
        Map.ofList [
            'p', Pawn
            'n', Knight
            'b', Bishop
            'r', Rook
            'q', Queen
            'k', King
        ]

    let private pieceFromChar value =
        let color =
            if Char.IsUpper value then
                White
            else
                Black

        let kind = Map.tryFind (Char.ToLowerInvariant value) pieceKinds

        kind |> Option.map (fun pieceKind -> { Color = color; Kind = pieceKind })

    let private addPiece rankIndex fileIndex value board =
        pieceFromChar value
        |> Option.map (fun piece -> Ok(Map.add { File = fileIndex; Rank = rankIndex } piece board, fileIndex + 1))
        |> Option.defaultValue (Error(sprintf "Unsupported FEN piece '%c'." value))

    let private parsePiece rankIndex fileIndex value board =
        if fileIndex < 8 then
            addPiece rankIndex fileIndex value board
        else
            Error "FEN rank contains too many files."

    let private parseFenValue rankIndex value state =
        state
        |> Result.bind (fun (board, fileIndex) ->
            if Char.IsDigit value then
                Ok(board, fileIndex + int (Char.GetNumericValue value))
            else
                parsePiece rankIndex fileIndex value board)

    let private completeRank state =
        state
        |> Result.bind (fun (board, fileIndex) ->
            if fileIndex = 8 then
                Ok board
            else
                Error "Each FEN rank must describe exactly 8 files.")

    let private parseRank rankIndex rank board =
        rank
        |> Seq.fold (fun state value -> parseFenValue rankIndex value state) (Ok(board, 0))
        |> completeRank

    let parseBoard (fen: string) =
        if String.IsNullOrWhiteSpace fen then
            Error "FEN cannot be empty."
        else
            let placement = fen.Trim().Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)[0]
            let ranks = placement.Split('/')

            if ranks.Length <> 8 then
                Error "FEN board placement must contain 8 ranks."
            else
                let mutable state = Ok Map.empty

                for rankIndex in 0 .. ranks.Length - 1 do
                    state <- state |> Result.bind (parseRank rankIndex ranks[rankIndex])

                state

    let private kindChars =
        pieceKinds
        |> Seq.map (fun entry -> entry.Value, entry.Key)
        |> Map.ofSeq

    let private kindToChar kind = Map.find kind kindChars

    let private pieceToChar piece =
        let value = kindToChar piece.Kind
        if piece.Color = White then Char.ToUpperInvariant value else value

    let private appendEmptyRun (builder: StringBuilder) emptyCount =
        if emptyCount > 0 then
            builder.Append(emptyCount) |> ignore

    let private appendSquareInRank (board: BoardState) rank (builder: StringBuilder) file emptyCount =
        match Map.tryFind { File = file; Rank = rank } board with
        | Some piece ->
            appendEmptyRun builder emptyCount
            builder.Append(pieceToChar piece) |> ignore
            0
        | None -> emptyCount + 1

    let private appendRank (board: BoardState) rank (builder: StringBuilder) =
        let emptyCount =
            [ 0 .. 7 ]
            |> List.fold (fun emptyCount file -> appendSquareInRank board rank builder file emptyCount) 0

        appendEmptyRun builder emptyCount

    let boardPlacement (board: BoardState) =
        let builder = StringBuilder()

        for rank in 0 .. 7 do
            if rank > 0 then
                builder.Append('/') |> ignore

            appendRank board rank builder

        builder.ToString()
