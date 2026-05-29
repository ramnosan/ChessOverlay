# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ChessOverlay is a Windows desktop overlay application that helps chess players see attacked squares on a visible chessboard. The application:
- Lets users select a visible chessboard on screen via drag-to-select
- Reads the board state from the selected area (using FEN notation or template matching)
- Calculates which squares are attacked by the enemy (top-side player)
- Displays a transparent red overlay highlighting those attacked squares
- Updates in real-time as pieces move on the board

The app does not interact with chess engines, make moves, or provide move suggestions—it only observes and displays visual information.

## Repository Structure

- **ChessOverlay/** - Main WinForms desktop application (F#, net10.0-windows)
- **ChessOverlay.Tests/** - XUnit test suite covering domain logic, attack calculation, board reading
- **ChessOverlay.Quality/** - Code quality analysis tool (DRY duplication, CRAP metric, architecture)
- **specification.md** - Product requirements and acceptance criteria

Key application files:
- **Domain.fs** - Core types: PieceColor, PieceKind, Square, Piece, BoardState, BoardGeometry, OverlayFrame
- **AttackCalculator.fs** - Calculates attacked squares, hanging pieces, fork opportunities, move rays
- **BoardReading.fs** - Board reader interface and implementations (FEN parser, template matching)
- **TemplatePieceDetection.fs** - Piece template loading, bitmap extraction, calibration
- **OverlayWindow.fs** - Transparent overlay window rendering (WinForms)
- **OverlayController.fs** - Updates overlay based on board state changes
- **BoardSelectionWindow.fs** - User interface for selecting board area on screen
- **Program.fs** - Application entry point, startup options, geometry storage

## Build and Run

**Build the application:**
\\\
dotnet build ChessOverlay.slnx
\\\

**Run the application:**
\\\
dotnet run --project ChessOverlay
\\\

**Run with demo mode** (automatically positions a board in the center of the screen):
\\\
dotnet run --project ChessOverlay -- --demo
\\\

**Run with specific board geometry** (left,top,size in pixels):
\\\
dotnet run --project ChessOverlay -- --board 100,150,640
\\\

**Run with FEN position** (sets board state via FEN string):
\\\
dotnet run --project ChessOverlay -- --fen "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w - - 0 1"
\\\

**Enable timing output** (logs performance metrics to debug output):
\\\
dotnet run --project ChessOverlay -- --timing
\\\

## Testing

**Run all tests:**
\\\
dotnet test ChessOverlay.Tests
\\\

**Run a single test file:**
\\\
dotnet test ChessOverlay.Tests --filter "ClassName"
\\\

**Run tests with coverage:**
\\\
dotnet run --project ChessOverlay.Quality -- crap
\\\

The CRAP quality command generates fresh Cobertura coverage before scoring, using isolated
`.build-check/quality-coverage/` build output and `artifacts/coverage/` results so stale
`ChessOverlay.Tests/TestResults` files are not reused accidentally.

Test files and their focus areas:
- **DomainTests.fs** - FEN parsing, square naming, board geometry, board state helpers
- **AttackCalculatorTests.fs** - Attack calculation, enemy color detection, piece attack rays
- **BoardReaderTests.fs** - Board reading implementations, confidence scoring
- **TemplatePieceDetectionTests.fs** - Template loading, bitmap extraction, correlation
- **BoardSelectionWindowTests.fs** - Board selection UI geometry calculations
- **ProgramTests.fs** - Startup option parsing, geometry storage/loading
- **ArchitectureViewTests.fs**, **CrapMetricTests.fs**, **DryDuplicationTests.fs** - Quality analysis

Test fixtures are in **ChessOverlay.Tests/Fixtures/** (sample board screenshots and piece templates).

## Code Quality Tools

The ChessOverlay.Quality project provides static analysis of the main codebase.

**Run all quality checks:**
\\\
dotnet run --project ChessOverlay.Quality
\\\

**DRY duplication analysis** (finds structurally similar code blocks):
\\\
dotnet run --project ChessOverlay.Quality -- dry --threshold 0.82 --min-lines 4
\\\

**CRAP metric** (Cyclomatic complexity and code coverage combined risk score):
\\\
dotnet run --project ChessOverlay.Quality -- crap
\\\

By default this regenerates test coverage before calculating scores. Use `--coverage <file>`
only when intentionally analyzing a specific Cobertura XML file.

**Architecture view** (detects circular dependencies and visualizes module layers):
\\\
dotnet run --project ChessOverlay.Quality -- arch --format html --out artifacts/architecture.html
\\\

**Mutation testing** (verifies the test suite actually catches regressions):
\\\
dotnet run --project ChessOverlay.Quality -- mutate
\\\

`mutate` is a custom source-level mutation tester living in ChessOverlay.Quality, modeled
on [unclebob/mutate4go](https://github.com/unclebob/mutate4go). It rewrites six operator
families in the pure core (Domain.fs, AttackCalculator.fs): arithmetic (`+ - * /`),
comparison (`> >= < <=`), equality (`= <>`), boolean (`true false`), logical (`&& || not`),
and the constant `0 <-> 1`. It then builds and runs the test suite per mutant. Each mutant
is built first: build failures are reported as compile errors and excluded from the score,
so the F# compiler acts as the safety net for malformed/equivalent mutants. Mutants on
lines with no test coverage are skipped (reported as no-coverage). A mutant whose tests run
past the timeout (see `--timeout-factor`) counts as killed. The mutation score is
(killed + timeout) / (killed + timeout + survived); the command exits non-zero below
`--threshold` (default 0.70).

Every mutation site is tagged with its enclosing function. `--update-manifest` embeds a
footer manifest of normalized per-function hashes into each core file, and `--since-last-run`
then mutates only functions whose body changed since that manifest (differential mutation).
`--scan` reports mutation-site counts (total and changed) without building or testing.
`--max-workers <n>` runs mutants in parallel using isolated full-project copies, so the live
working tree is never raced on.

\\\
dotnet run --project ChessOverlay.Quality -- mutate --changed --max-mutants 20 --threshold 0.6
\\\

Options: `--changed` (changed core files only), `--since-last-run` (only functions changed
since the embedded manifest), `--scan` (counts only, no builds), `--update-manifest` (rewrite
the embedded manifest without mutating), `--max-mutants <n>` (cap mutants run),
`--max-workers <n>` (parallel isolated workers), `--timeout-factor <n>` (bound each mutant's
test run at N x the baseline duration), `--threshold <n>` (minimum score), `--coverage <file>`
(reuse a Cobertura XML instead of regenerating). Like CRAP, it generates fresh coverage by
default to skip uncovered lines.

Available Claude Code skill: **chessoverlay-quality** runs all three analyses.

## Architecture and Design

### Layered Architecture

1. **Domain Layer** (Domain.fs)
   - Immutable types: Square, Piece, BoardState (Map<Square, Piece>), BoardGeometry
   - Fen module: Parses FEN notation into BoardState
   - Squares module: Helpers for square validation and chess notation

2. **Attack Calculation Layer** (AttackCalculator.fs)
   - Calculates attack rays per piece (pawn, knight, bishop, rook, queen, king)
   - Determines enemy color (top player's color) by mean rank
   - Computes attacked squares, hanging pieces, fork threats
   - Returns rays (direction-grouped attacks) for arrow rendering
   - Pawn attacks assume enemy is moving down the screen (toward increasing ranks)

3. **Board Reading Layer** (BoardReading.fs, TemplatePieceDetection.fs)
   - IBoardReader interface: Read(Bitmap, BoardGeometry) -> BoardReading option
   - FenBoardReader: Returns a fixed board state from FEN (for testing/demo)
   - TemplateBoardReader: Matches piece templates against screen capture to read board
   - UncertainBoardReader: Returns None (used when templates unavailable)
   - Returns confidence score and candidate piece matches for debugging

4. **UI/Overlay Layer** (OverlayWindow.fs, OverlayController.fs, BoardSelectionWindow.fs)
   - OverlayWindow: Transparent borderless WinForms window (layered, click-through)
   - OverlayController: Timer-driven polling (500ms screen / 30ms DOM) of board state, updates overlay
   - BoardSelectionWindow: Drag-to-select UI for defining board area
   - Program.fs: Startup orchestration, geometry persistence (AppData), CLI arguments

### Key Design Patterns

- **Functional domain model**: Immutable types, pure functions for calculations
- **Pluggable readers**: IBoardReader interface allows FEN, template, or mock implementations
- **Confidence-based filtering**: Board readings include confidence score; updates skipped below threshold
- **Ray-based attacks**: Attack directions grouped for arrow rendering, not just sets of squares
- **Enemy color inference**: Determined by mean rank of pieces (not assuming white/black)
- **Pawn direction abstraction**: Pawns attack toward increasing ranks (top player always enemy)

### Board State Representation

- 8x8 board with file (0-7, left to right) and rank (0-7, top to bottom)
- Chess notation: file a-h (left to right) maps to file 0-7; rank 8-1 (top to bottom) maps to rank 0-7
- BoardState is Map<Square, Piece>; empty squares omitted
- FEN format: ranks separated by "/", empty squares as digits, pieces as chars (uppercase white, lowercase black)

### Overlay Rendering

OverlayFrame contains:
- **AttackArrows**: One arrow per direction a piece can see (ending at farthest reachable square)
- **HangingSquares**: Friendly pieces attacked and undefended (transparent red)
- **EnemyHangingSquares**: Enemy pieces attacked by friendly player (transparent red with different tint)
- **ForkSquares**: Enemy pieces attacking 2+ undefended friendly pieces (yellow highlight)
- **FriendlyForkMoveArrows**: Friendly moves that would create a fork (blue arrows)
- **DetectedPieces**: The board state detected from screen (optional, for confidence visualization)

## Important Implementation Details

### Board Selection and Geometry

Users drag-select a square around the visible chessboard. The selection is converted to BoardGeometry (Left, Top, Size in pixels). Geometry is persisted to %AppData%/ChessOverlay/board_area.txt so it survives restarts.

ScreenCapture.captureVirtualScreen() captures the full virtual screen (handles multi-monitor). Board geometry references virtual screen coordinates.

### Template-Based Piece Detection

- Templates are PNG/BMP images named like white_king.png, lack_pawn.png (color_kind.png or wK, bP)
- Loaded from "templates" directory relative to working directory (or specified via --piece-templates)
- PieceTemplateCalibration can auto-generate templates from starting position on first run
- Similarity threshold defaults to 0.35 (scales correlation by piece contrast for robustness)
- FieldTemplates (move hints, premove dots) loaded from FieldTemplates/ beside the binary

### Piece Matching Scoring

Board reader returns Confidence (0-1) and Candidates (per-square list of matching pieces with scores). Overlay only displays if confidence >= 0.45. Timing and candidate debug output available with --timing flag.

### Enemy Detection

AttackCalculator.enemyColor uses mean rank: whichever color's pieces sit at lower average rank is on top. This works because the user is always bottom player, and top player is always the enemy.

## Keyboard Shortcuts

- **Ctrl+Shift+B**: Open board selection dialog (hotkey registered with OS)
- **Ctrl+Shift+O**: Toggle overlay on/off (pauses/resumes scanning and clears the frame)

## Testing Strategy

- Unit tests cover FEN parsing, attack calculation, square naming, board geometry
- Template matching and board reading tested with fixture images
- No UI tests (UI code marked with [<ExcludeFromCodeCoverage>])
- Quality analysis (DRY, CRAP) has dedicated test suite checking correctness of analyses
- Architecture view detects cycles in module dependencies

## Common Development Tasks

**Add a new piece type or attack pattern**: Update Domain.fs PieceKind enum, add attack rays in AttackCalculator.fs, add tests.

**Change overlay colors or appearance**: Edit OverlayWindow.fs color definitions and Paint event handler.

**Add a new board reader**: Implement IBoardReader interface in BoardReading.fs, update Program.fs reader creation logic.

**Adjust confidence threshold**: Edit OverlayController.fs confidenceThreshold value (default 0.45).

**Change board poll interval**: Pass scanIntervalMilliseconds to OverlayController constructor (default 500ms screen scan; DOM reader polls at a fixed 30ms).
