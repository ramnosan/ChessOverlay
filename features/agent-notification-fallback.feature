Feature: Agent notification with file-based fallback

  # agent-notification-fallback-001
  Scenario: Message written to target pending-messages with correct filename when target window is not reachable
    Given a valid target agent "coder" whose worktree is ".worktrees/coder"
    And the window "SwarmForge - Coder" is not open
    When notify-agent is run with target "coder", sender "specifier", and message file "msg.txt"
    Then the content of "msg.txt" is written under ".worktrees/coder/pending-messages/"
    And the filename matches the pattern "50-YYYYMMDD-HHMMSS-specifier.txt"
    And notify-agent exits with code 0

  # agent-notification-fallback-002
  Scenario: Message sent via SendKeys when target window is reachable
    Given a valid target agent "coder" whose window title is "SwarmForge - Coder"
    And the window "SwarmForge - Coder" is open
    When notify-agent is run with target "coder" and message file "msg.txt"
    Then the message is typed into the window "SwarmForge - Coder"
    And notify-agent exits with code 0

  # agent-notification-fallback-003
  Scenario: Error on unknown target
    Given no agent with role "unknown-role" exists in the sessions file
    When notify-agent is run with target "unknown-role" and message file "msg.txt"
    Then notify-agent exits with a non-zero code
