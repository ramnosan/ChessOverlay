# ChessOverlay

A Windows desktop overlay that highlights squares attacked by your opponent in real time, directly on top of any visible chessboard on screen.

![ChessOverlay in action](overlay-screenshot.png)

## What it does

ChessOverlay sits as a transparent window over your screen. You drag-select the area where your chessboard is displayed, and the app:

- Reads the current board position by matching piece images against the screen
- Determines which side is on top (the opponent)
- Draws colored overlays showing:
  - **Red squares** — squares attacked by the enemy
  - **Red-tinted squares** — your pieces that are hanging (attacked and undefended)
  - **Yellow squares** — enemy pieces that are forking two or more of your undefended pieces
  - **Arrows** — attack rays per piece, showing how far each piece sees
  - **Blue arrows** — friendly moves that would create a fork

The app does not interact with chess engines, suggest moves, or make decisions — it only observes and visualizes what is already on screen.

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A chessboard visible somewhere on screen (browser, desktop client, etc.)

## Getting started

**Build:**
```
dotnet build ChessOverlay.slnx
```

**Run:**
```
dotnet run --project ChessOverlay
```

On first launch, press **Ctrl+Shift+B** to open the board selection dialog and drag a rectangle around your chessboard. The selection is saved automatically for future sessions.

## Piece templates

The app uses template matching to identify pieces on screen. Templates are PNG images in a `templates/` folder next to the binary, named like `white_king.png` / `black_pawn.png` (or short form `wK.png` / `bP.png`).

If no templates are found, the overlay will not display piece data. You can either supply your own templates or use `--calibrate` to auto-generate them from the starting position on first run.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+Shift+B | Open board selection dialog |
| Ctrl+Shift+O | Toggle overlay on/off |

## CLI options

```
dotnet run --project ChessOverlay -- [options]

--demo                     Auto-position board in screen center (for testing)
--board <left,top,size>    Set board geometry in pixels (e.g. 100,150,640)
--fen <fen-string>         Load a fixed board state from a FEN string
--piece-templates <path>   Path to folder containing piece template images
--calibrate                Auto-generate templates from starting position
--timing                   Print performance metrics to debug output
```

## How it works

1. A timer fires every 500 ms and captures the screen region defined by the board geometry.
2. Each square is compared against piece templates using normalized cross-correlation.
3. The reading is accepted only if the overall confidence score exceeds 0.45.
4. The top-side player is identified as the enemy by computing the mean rank of all pieces — whoever sits higher on screen is on top.
5. Attack rays are calculated for every enemy piece and the overlay is repainted.

## Development

**Run tests:**
```
dotnet test ChessOverlay.Tests
```

**Run code quality analysis** (DRY duplication, CRAP complexity, architecture view):
```
dotnet run --project ChessOverlay.Quality
```

## Project structure

```
ChessOverlay/           Main WinForms application (F#, net10.0-windows)
ChessOverlay.Tests/     XUnit test suite
ChessOverlay.Quality/   Static analysis tools (DRY, CRAP, architecture)
specification.md        Product requirements and acceptance criteria
```

## License

MIT
