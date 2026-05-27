namespace ChessOverlay

open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.IO

module BoardReadingConfidence =
    let minimumUsable = 0.45

module LastBoardStateStorage =
    let private storageDir =
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "ChessOverlay")

    let private storagePath = Path.Combine(storageDir, "last_board_state.fen")

    let tryLoadFrom (path: string) =
        try
            if File.Exists(path) then
                match File.ReadAllText(path).Trim() |> Fen.parseBoard with
                | Ok board -> Some board
                | Error _ -> None
            else
                None
        with _ ->
            None

    let saveTo (path: string) (board: BoardState) =
        try
            let directory = Path.GetDirectoryName(path)

            if not (System.String.IsNullOrWhiteSpace directory) then
                Directory.CreateDirectory(directory) |> ignore

            File.WriteAllText(path, Fen.boardPlacement board)
        with _ ->
            ()

    let tryLoad () = tryLoadFrom storagePath

    let save board = saveTo storagePath board

module BoardReaderHelpers =
    /// Builds a fully-confident reading from a FEN string, or None when the FEN is invalid.
    let readingFromFenWithStrategy strategy (fen: string) : BoardReading option =
        match Fen.parseBoard fen with
        | Ok board -> Some { Board = board; Confidence = 1.0; Candidates = Map.empty; Strategy = strategy }
        | Error _ -> None

    let readingFromFen (fen: string) : BoardReading option =
        readingFromFenWithStrategy "FEN" fen

type IBoardReader =
    abstract Read: Bitmap * BoardGeometry -> BoardReading option

type FenBoardReader(fen: string) =
    interface IBoardReader with
        member _.Read(_, _) = BoardReaderHelpers.readingFromFen fen

type UncertainBoardReader() =
    interface IBoardReader with
        member _.Read(_, _) = None

type LastBoardStateReader(
    primary: IBoardReader,
    loadLastBoard: unit -> BoardState option,
    saveLastBoard: BoardState -> unit,
    ?minimumConfidence: float) =
    let minimumConfidence = defaultArg minimumConfidence BoardReadingConfidence.minimumUsable

    let savedReading () =
        loadLastBoard ()
        |> Option.map (fun board ->
            {
                Board = board
                Confidence = 1.0
                Candidates = Map.empty
                Strategy = "Last board state"
            })

    interface IBoardReader with
        member _.Read(bitmap, geometry) =
            match primary.Read(bitmap, geometry) with
            | Some reading when reading.Confidence >= minimumConfidence ->
                saveLastBoard reading.Board
                Some reading
            | Some reading ->
                savedReading ()
                |> Option.orElseWith (fun () -> Some reading)
            | None ->
                savedReading ()

type FallbackBoardReader(primary: IBoardReader, fallback: IBoardReader) =
    interface IBoardReader with
        member _.Read(bitmap, geometry) =
            match primary.Read(bitmap, geometry) with
            | Some r -> Some r
            | None -> fallback.Read(bitmap, geometry)

[<ExcludeFromCodeCoverage>]
module ScreenCapture =
    let captureVirtualScreen () =
        let bounds = System.Windows.Forms.SystemInformation.VirtualScreen
        let bitmap = new Bitmap(bounds.Width, bounds.Height)

        use graphics = Graphics.FromImage bitmap
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size)
        bitmap, bounds.Location
