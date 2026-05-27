Feature: Fork validity filter

  # fork-validity-filter-001
  Scenario: Enemy fork square not highlighted when the forking piece can be captured
    Given the board has an enemy piece that attacks two undefended friendly pieces
    And a friendly piece can capture the enemy forking piece
    When the overlay is rendered
    Then the fork square is not highlighted

  # fork-validity-filter-002
  Scenario: Enemy fork square highlighted when the forking piece cannot be captured
    Given the board has an enemy piece that attacks two undefended friendly pieces
    And no friendly piece can capture the enemy forking piece
    When the overlay is rendered
    Then the fork square is highlighted

  # fork-validity-filter-003
  Scenario: Friendly fork move not shown when the forking piece would be capturable at the destination
    Given moving a friendly piece to a square would attack two enemy pieces
    And an enemy piece can capture the friendly piece at that square
    When the overlay is rendered
    Then no fork move arrow is shown for that square

  # fork-validity-filter-004
  Scenario: Friendly fork move shown when the forking piece would not be capturable at the destination
    Given moving a friendly piece to a square would attack two enemy pieces
    And no enemy piece can capture the friendly piece at that square
    When the overlay is rendered
    Then a fork move arrow is shown for that square
