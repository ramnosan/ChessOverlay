# notify-agent.ps1  --  send a message to a SwarmForge agent
#
# Usage:
#   .\notify-agent.ps1 -Target <role> [-Sender <sender>] -File <msgfile>
#   .\notify-agent.ps1 -Target <role> [-Sender <sender>] -Message <text>
#
# Sessions file: .swarmforge\sessions.tsv  (tab-separated: role, session, worktree)
#
# If a terminal window titled "SwarmForge - <Role>" is found, the message is
# sent to it via WScript.Shell SendKeys. Otherwise the message is written to:
#   <worktree>\pending-messages\<priority>-<yyyyMMdd>-<HHmmss>-<sender>.txt
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Target,
    [string]$Sender  = "unknown",
    [string]$File    = "",
    [string]$Message = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$sessionsFile = ".swarmforge\sessions.tsv"
$priority     = "50"

# --- Resolve message content ---
if ($File -ne "") {
    if (-not (Test-Path $File)) {
        Write-Error "notify-agent: message file not found: $File"
        exit 1
    }
    $msg = Get-Content $File -Raw -Encoding UTF8
} elseif ($Message -ne "") {
    $msg = $Message
} else {
    Write-Error "notify-agent: no message provided (use -File or -Message)"
    exit 1
}

# --- Look up target in sessions file ---
if (-not (Test-Path $sessionsFile)) {
    Write-Error "notify-agent: sessions file not found: $sessionsFile"
    exit 1
}

$session  = $null
$worktree = $null

foreach ($line in (Get-Content $sessionsFile -Encoding UTF8)) {
    $line = $line.Trim()
    if ($line -eq "" -or $line.StartsWith("#")) { continue }
    $parts = $line -split "`t"
    if ($parts.Count -ge 3 -and $parts[0] -eq $Target) {
        $session  = $parts[1]
        $worktree = $parts[2]
        break
    }
}

if ($null -eq $session) {
    Write-Error "notify-agent: unknown target agent: $Target"
    exit 1
}

# --- Derive the expected window title from the role name ---
$culture     = [System.Globalization.CultureInfo]::InvariantCulture
$titleCased  = $culture.TextInfo.ToTitleCase($Target.ToLower())
$windowTitle = "SwarmForge - $titleCased"

# --- Check if the target window is reachable ---
$proc = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle -like "*$windowTitle*" } |
        Select-Object -First 1

if ($proc) {
    # Activate the window and type the message
    $shell = New-Object -ComObject WScript.Shell
    $shell.AppActivate($proc.Id) | Out-Null
    Start-Sleep -Milliseconds 300

    # Escape special SendKeys characters: +^%~{}[]()
    $escaped = $msg -replace '([+^%~\{\}\[\]\(\)])', '{$1}'
    $shell.SendKeys($escaped)
    $shell.SendKeys("{ENTER}")
} else {
    # File-based fallback
    $timestamp  = Get-Date -Format "yyyyMMdd-HHmmss"
    $pendingDir = Join-Path $worktree "pending-messages"
    New-Item -ItemType Directory -Force -Path $pendingDir | Out-Null
    $outFile    = Join-Path $pendingDir "${priority}-${timestamp}-${Sender}.txt"
    [System.IO.File]::WriteAllText($outFile, $msg, [System.Text.Encoding]::UTF8)
    Write-Host "notify-agent: window not reachable -- message queued at $outFile"
}

exit 0
