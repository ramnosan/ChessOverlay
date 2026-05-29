# ChessOverlay

ChessOverlay is a Windows desktop overlay for chess players. It watches a visible chessboard, reads the current position, and draws transparent visual hints over the board so you can quickly see tactical danger without leaving the chess app.

The program only observes the screen. It does not use a chess engine, suggest best moves, make moves, click the board, or interact with your chess account.

## Features

- **Transparent attack overlay**: draws enemy attack arrows over the selected board.
- **Top-side enemy detection**: treats the player at the top of the board as the opponent.
- **Live updates**: rescans the board as the position changes and refreshes the overlay.
- **Chrome board detection**: tries to detect an open Chrome chessboard automatically on startup.
- **Manual board selection**: lets you drag-select any visible 8x8 board when automatic detection is not enough.
- **Template-based piece reading**: can read pieces from screen captures using calibrated piece templates.
- **FEN/demo mode**: can run from a fixed FEN position for testing or demonstrations.
- **Hanging-piece and fork markers**: highlights hanging pieces and fork threats in addition to attack arrows.
- **Safe uncertainty behavior**: avoids showing misleading attack data when the board cannot be read confidently.
- **Click-through overlay**: stays on top while allowing normal interaction with the chess app underneath.

## Requirements

- Windows
- .NET SDK 10.0 or newer

## Build

```powershell
dotnet build ChessOverlay.slnx
```

## Run

Start the app normally:

```powershell
dotnet run --project ChessOverlay
```

On startup, ChessOverlay first tries to find a chessboard in Chrome. If no board is detected, press `Ctrl+Shift+B` and select the board manually.

## How To Use

1. Open a chessboard in Chrome or another desktop chess app.
2. Start ChessOverlay with `dotnet run --project ChessOverlay`.
3. If prompted, press `Ctrl+Shift+B` and drag a square around the visible board.
4. Play normally. The overlay updates as the board position changes.
5. Press `Ctrl+Shift+O` to toggle the overlay on or off.

## Keyboard Shortcuts

- `Ctrl+Shift+B`: choose or reselect the board area.
- `Ctrl+Shift+O`: toggle the overlay display and scanning.

## Run Modes

Run in demo mode with the normal starting position centered on the primary screen:

```powershell
dotnet run --project ChessOverlay -- --demo
```

Run with a fixed board area, using `left,top,size` in pixels:

```powershell
dotnet run --project ChessOverlay -- --board 100,150,640
```

Run with a fixed FEN position:

```powershell
dotnet run --project ChessOverlay -- --fen "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w - - 0 1"
```

Enable timing and candidate debug output:

```powershell
dotnet run --project ChessOverlay -- --timing
```

Use template-based piece reading explicitly:

```powershell
dotnet run --project ChessOverlay -- --piece-reader template --piece-templates templates
```

Calibrate templates from a normal starting position:

```powershell
dotnet run --project ChessOverlay -- --calibrate-templates
```

You can also provide a FEN through the `CHESS_OVERLAY_FEN` environment variable.

## Template Calibration

Template reading needs piece images that match the board style you are using. To calibrate:

1. Open a normal starting chess position.
2. Start ChessOverlay with `--calibrate-templates`.
3. Select the board if needed.
4. The app saves piece templates under `templates/`.

If templates are missing or the board cannot be read confidently, the app shows a status message instead of drawing unreliable attack data.

## Testing

Run the automated tests:

```powershell
dotnet test ChessOverlay.Tests
```

Run the quality analysis suite:

```powershell
dotnet run --project ChessOverlay.Quality
```

## Notes

- ChessOverlay assumes you are the bottom-side player.
- It highlights squares controlled by the top-side player, not engine evaluations or best moves.
- It stores board geometry and the last known board state in `%AppData%\ChessOverlay`.
- When in doubt, it prefers showing no tactical overlay over showing incorrect information.
