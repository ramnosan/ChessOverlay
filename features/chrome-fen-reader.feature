Feature: Chess.com Chrome CDP board reader

  # chrome-fen-reader-001
  Scenario Outline: Returns board state when chess.com tab shows a position
    Given Chrome is running with remote debugging enabled
    And a chess.com tab is displaying the FEN "<fen>"
    When the Chrome FEN reader reads the board
    Then the board state matches the FEN "<fen>"
    And the reading confidence is 1.0

    Examples:
      | fen |
      | rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1 |
      | r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4 |

  # chrome-fen-reader-002
  Scenario: Returns no reading when no chess.com tab is open
    Given Chrome is running with remote debugging enabled
    And no chess.com tab is open
    When the Chrome FEN reader reads the board
    Then no reading is returned

  # chrome-fen-reader-003
  Scenario: Returns no reading when Chrome remote debugging is not reachable
    Given Chrome remote debugging is not reachable
    When the Chrome FEN reader reads the board
    Then no reading is returned

  # chrome-fen-reader-004
  Scenario: Chrome FEN reader takes priority over template matching when it returns a reading
    Given the Chrome FEN reader is configured and returns a reading
    And the template board reader is also configured
    When the board controller chooses a reader
    Then the Chrome FEN reader result is used

  # chrome-fen-reader-005
  Scenario: Falls back to template matching when Chrome FEN reader returns no reading
    Given the Chrome FEN reader is configured and returns no reading
    And the template board reader is configured
    When the board controller chooses a reader
    Then the template board reader result is used
