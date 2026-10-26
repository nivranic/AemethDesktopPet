#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$BackupDirectory,
    [switch]$KeepLoginEntry
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This script only supports Windows.' }
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$exe = Join-Path $pluginRoot 'bin/ZCodePet.exe'
$oldAction = Join-Path $PSScriptRoot 'keepalive.cmd'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$valueName = 'ZCodePixelPet'
$taskName = 'ZCodePetKeepAlive'

if (-not [IO.Path]::IsPathFullyQualified($BackupDirectory)) { throw 'BackupDirectory must be an absolute path to a NEW directory.' }
$backupRoot = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (Test-Path -LiteralPath $backupRoot) { throw 'Backup directory already exists; choose a new directory to preserve previous backups.' }
if ($backupRoot.Equals($pluginRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $backupRoot.StartsWith($pluginRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Backup directory must be outside the plugin.'
}
$cursor = $backupRoot
while ($cursor) {
    if ((Test-Path -LiteralPath $cursor) -and (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw "Backup path must not pass through a symbolic link or junction: $cursor"
    }
    $cursor = [IO.Path]::GetDirectoryName($cursor)
}

function Read-LoginEntry {
    if (-not (Test-Path -LiteralPath $runKey)) { return $null }
    $key = Get-Item -LiteralPath $runKey
    try {
        if ($key.GetValueNames() -notcontains $valueName) { return $null }
        return [pscustomobject]@{
            value = $key.GetValue($valueName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            kind = [string]$key.GetValueKind($valueName)
        }
    } finally { $key.Dispose() }
}
function Assert-LoginEntry($Entry) {
    if ($null -eq $Entry) { return }
    if ($Entry.kind -notin @('String', 'ExpandString') -or $Entry.value -isnot [string]) { throw 'Login entry is not a supported string. No changes made.' }
    # Accept only the known executable, with no arguments or the documented --wait-host option.
    $unquotedAlternative = if ($exe -notmatch '\s') { '|' + [regex]::Escape($exe) } else { '' }
    $pattern = '^\s*(?:"' + [regex]::Escape($exe) + '"' + $unquotedAlternative + ')(?:\s+--wait-host)?\s*$'
    if ($Entry.value -notmatch $pattern) { throw 'ZCodePixelPet login entry points elsewhere or has unknown arguments. Refusing to remove it.' }
}
function Read-OldTask {
    # Query failure is not equivalent to an absent task: allow it to stop the operation.
    $matches = @(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object { $_.TaskName -ceq $taskName })
    if ($matches.Count -gt 1) { throw 'Unexpected duplicate scheduled tasks; inspect them manually.' }
    if ($matches.Count -eq 1) { return $matches[0] }
    return $null
}
function Assert-OldTask($Task) {
    if ($null -eq $Task) { return }
    $actions = @($Task.Actions)
    if ($actions.Count -ne 1) { throw 'Keepalive task has unexpected actions; refusing to change it.' }
    $command = [string]$actions[0].Execute
    if ($command.StartsWith('"') -and $command.EndsWith('"') -and $command.Length -ge 2) { $command = $command.Substring(1, $command.Length - 2) }
    if ($command -ine $oldAction -or -not [string]::IsNullOrWhiteSpace([string]$actions[0].Arguments)) {
        throw 'Keepalive task does not point exactly to this plugin keepalive.cmd without arguments. Refusing to remove it.'
    }
}

$login = if ($KeepLoginEntry) { $null } else { Read-LoginEntry }
$task = Read-OldTask
Assert-LoginEntry $login
Assert-OldTask $task
if ($null -eq $login -and $null -eq $task) {
    [pscustomobject]@{ changed = $false; reason = 'No matching OS integration found.' }
    return
}
$taskXml = if ($task) { Export-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction Stop } else { $null }
$operationTarget = if ($KeepLoginEntry) { 'Matching ZCodePetKeepAlive task; preserve the current login-start choice' } else { 'Current user ZCodePixelPet login entry and matching ZCodePetKeepAlive task' }
if (-not $PSCmdlet.ShouldProcess($operationTarget, 'Back up and remove only verified plugin OS integration')) { return }

New-Item -Path $backupRoot -ItemType Directory -ErrorAction Stop | Out-Null
if ($taskXml) { $taskXml | Set-Content -LiteralPath (Join-Path $backupRoot 'ZCodePetKeepAlive.xml') -Encoding utf8NoBOM }
[ordered]@{
    createdUtc = [DateTime]::UtcNow.ToString('o'); pluginRoot = $pluginRoot
    registryKey = $runKey; registryValueName = $valueName; loginEntry = $login; keepLoginEntry = [bool]$KeepLoginEntry
    taskName = $taskName; taskPath = '\'; taskPresent = ($null -ne $task)
    scope = 'OS integration only. User data, plugin files, ZCode registration, running processes and antivirus settings are unchanged.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $backupRoot 'integration-before.json') -Encoding utf8NoBOM

if ($task) {
    $currentTask = Read-OldTask
    Assert-OldTask $currentTask
    if ($null -eq $currentTask -or (Export-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction Stop) -cne $taskXml) {
        throw 'Task changed after backup; removal stopped. Backup is retained.'
    }
    Unregister-ScheduledTask -TaskName $taskName -TaskPath '\' -Confirm:$false -ErrorAction Stop
    if ($null -ne (Read-OldTask)) { throw 'Scheduled task still exists after removal; inspect permissions and the retained backup.' }
}
if ($login) {
    $currentLogin = Read-LoginEntry
    Assert-LoginEntry $currentLogin
    if ($null -eq $currentLogin -or $currentLogin.value -cne $login.value -or $currentLogin.kind -cne $login.kind) {
        throw 'Login entry changed after backup; removal stopped. Backup is retained.'
    }
    Remove-ItemProperty -LiteralPath $runKey -Name $valueName -ErrorAction Stop
    if ($null -ne (Read-LoginEntry)) { throw 'Login entry still exists after removal; inspect permissions and the retained backup.' }
}
[pscustomobject]@{ changed = $true; loginEntryRemoved = ($null -ne $login); keepaliveTaskRemoved = ($null -ne $task); backup = $backupRoot }
