# Chess Overlay MVP Specification

## Product Summary

Chess Overlay is a desktop helper application that lets the user select a visible chessboard, reads the current board state from that selected area, and draws an overlay on top of the board.

The MVP helps the bottom-side player understand danger on the board by highlighting every square attacked or "seen" by the enemy. The enemy is always the top player. Enemy-attacked squares are marked with transparent red so the board remains visible and playable.

The program does not make moves, suggest best moves, evaluate positions, or interact with the chess app. It only observes the screen and displays visual information.

## Assumptions

- The user is playing as the bottom-side player.
- The enemy is always the top-side player.
- The chessboard may appear in any screen application, including a browser or desktop chess app.
- The app uses screen capture to read the selected board area.
- "Attacked" or "seen" squares means all squares controlled by enemy pieces, not only legal enemy moves.
- If the app cannot confidently read the board or pieces, it avoids showing misleading highlights.

## Glossary

- **Board selection**: The user dragging a square around a visible 8 by 8 chessboard so the app can map its 64 squares.
- **Enemy player**: The player whose pieces start at the top of the selected board.
- **Attacked square**: A square controlled by at least one enemy piece according to chess movement rules.
- **Seen square**: Another name for an attacked square.
- **Overlay highlight**: A transparent visual layer drawn over a board square.
- **Board state**: The recognized pieces and their positions on the 64 board squares.

## User Stories

### User Story 1: Select a Chessboard

As a chess player, I want to select the chessboard area, so that the overlay can align with the game I am playing.

#### Acceptance Criteria

```gherkin
Given the program is running
And a chessboard is visible on the screen
When I drag a square around the chessboard
Then it uses the selected chessboard area
And it maps the board into 64 individual squares
```

```gherkin
Given the program is running
When I cancel board selection
Then it exits without showing attack highlights
```

### User Story 2: Highlight Enemy-Attacked Squares

As a chess player, I want squares attacked by the enemy to be marked in transparent red, so that I can quickly see dangerous squares.

#### Acceptance Criteria

```gherkin
Given a chessboard is selected
And the board state is read successfully
When the program calculates attacked squares
Then it treats the top-side player as the enemy
And it calculates every square attacked by enemy pieces
```

```gherkin
Given enemy-attacked squares have been calculated
When the overlay is displayed
Then every enemy-attacked square is marked with transparent red
And non-attacked squares are not marked red
And the chessboard remains visible through the overlay
```

### User Story 3: Update Highlights After Board Changes

As a chess player, I want the overlay to update when the position changes, so that the marked danger squares match the current board.

#### Acceptance Criteria

```gherkin
Given a chessboard is selected
And attack highlights are visible
When a move changes the board position
Then the program reads the new board state
And it clears outdated highlights
And it displays highlights for the new enemy-attacked squares
```

### User Story 4: Avoid Misleading Highlights

As a chess player, I want the program to avoid drawing highlights when it is unsure, so that I do not rely on incorrect danger information.

#### Acceptance Criteria

```gherkin
Given the selected chessboard is partially hidden or unclear
When the program cannot confidently read the board or pieces
Then it does not display attack highlights for that uncertain state
And it continues scanning the selected board area
```

## Given/When/Then Acceptance Criteria Summary

```gherkin
Feature: Chessboard selection

Scenario: Visible chessboard is selected
  Given the program is running
  And a chessboard is visible on the screen
  When I drag a square around the chessboard
  Then it uses the selected chessboard area
  And it maps the board into 64 individual squares

Scenario: Board selection is cancelled
  Given the program is running
  When I cancel board selection
  Then it exits without showing attack highlights
```

```gherkin
Feature: Enemy attack overlay

Scenario: Enemy attacked squares are calculated
  Given a chessboard is selected
  And the board state is read successfully
  When the program calculates attacked squares
  Then it treats the top-side player as the enemy
  And it calculates every square attacked by enemy pieces

Scenario: Enemy attacked squares are highlighted
  Given enemy-attacked squares have been calculated
  When the overlay is displayed
  Then every enemy-attacked square is marked with transparent red
  And non-attacked squares are not marked red
  And the chessboard remains visible through the overlay

Scenario: Board changes after a move
  Given a chessboard is selected
  And attack highlights are visible
  When a move changes the board position
  Then the program reads the new board state
  And it clears outdated highlights
  And it displays highlights for the new enemy-attacked squares
```

```gherkin
Feature: Safe failure behavior

Scenario: Board state cannot be read confidently
  Given the selected chessboard is partially hidden or unclear
  When the program cannot confidently read the board or pieces
  Then it does not display attack highlights for that uncertain state
  And it continues scanning the selected board area
```

## Non-Functional Requirements

- The transparent red overlay must allow the user to see the original board and pieces.
- Highlighted squares must align closely with the selected board squares.
- The overlay should update without distracting flicker.
- The program should not click, type, drag, or otherwise interact with the chess app.
- The program should prefer showing no highlights over showing inaccurate highlights.
- The app should be usable while the user continues playing normally.
- Automated tests should be written for core board-state, attack-calculation, board-selection, and update behavior where practical.

## Out of Scope

- Best-move suggestions.
- Chess engine evaluation.
- Opening, middlegame, or endgame advice.
- Automatically making moves.
- Detecting the enemy as the bottom-side player.
- Move history and game replay.
- Online account integration.
- Support for non-standard chess variants.
- User coaching or verbal explanations.

## Test Plan

- Tests should be added alongside implementation work and run as part of normal development.
- Show a normal chessboard on screen and verify the user can select it.
- Cancel board selection and verify the program exits without showing attack highlights.
- Confirm the selected board is mapped into 64 aligned squares.
- Use known chess positions and verify enemy-attacked squares are calculated correctly.
- Confirm the enemy is always interpreted as the top-side player.
- Confirm attacked squares are highlighted with transparent red.
- Confirm non-attacked squares are not highlighted red.
- Make a move and verify old highlights are cleared and new highlights are drawn.
- Partially cover or blur the board and verify the app avoids showing uncertain highlights.
- Confirm the app does not click, move pieces, or interact with the chess app.
