param([Parameter(Mandatory=$true)][string]$BackupDirectory)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required.' }
$pluginRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe=Join-Path $pluginRoot 'bin\ZCodePet.exe'
if(-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Build the companion before migrating startup.' }
$backupPath=[IO.Path]::GetFullPath($BackupDirectory)
[IO.Directory]::CreateDirectory($backupPath) | Out-Null
$recordPath=Join-Path $backupPath 'startup-before.json'
if(Test-Path -LiteralPath $recordPath) { throw 'Startup backup already exists; do not overwrite it.' }
$runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValue=(Get-ItemProperty -LiteralPath $runKey -ErrorAction Stop).ZCodePixelPet
$task=Get-ScheduledTask | Where-Object { $_.TaskName -eq 'ZCodePetKeepAlive' -and $_.TaskPath -eq '\' }
$expectedAction=Join-Path $PSScriptRoot 'keepalive.cmd'
if($task -and ($task.Actions.Count -ne 1 -or $task.Actions[0].Execute.Trim('"') -ine $expectedAction -or $task.Actions[0].Arguments)) {
    throw 'Old task action differs from this plugin. Inspect it before changing anything.'
}
if($runValue -and -not $runValue.StartsWith(('"'+$exe+'"'),[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Existing login entry belongs to a different path. Inspect it before changing anything.'
}
if($task) { Export-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath | Set-Content -LiteralPath (Join-Path $backupPath 'ZCodePetKeepAlive.xml') -Encoding utf8 }
[ordered]@{date=(Get-Date).ToString('o');pluginRoot=$pluginRoot;runValue=$runValue;taskPresent=[bool]$task;taskEnabled=if($task){$task.Settings.Enabled}else{$null}} | ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding utf8
if($task) {
    Disable-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath | Out-Null
    Stop-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath
    if((Get-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath).Settings.Enabled) { throw 'Old keepalive task is still enabled.' }
}
# Preserve the user's existing login-start choice; never create a new entry implicitly.
if($runValue) {
    $newValue='"'+$exe+'" --wait-host'
    Set-ItemProperty -LiteralPath $runKey -Name ZCodePixelPet -Value $newValue
    if((Get-ItemPropertyValue -LiteralPath $runKey -Name ZCodePixelPet) -cne $newValue) { throw 'Login startup verification failed.' }
}
[pscustomobject]@{keepaliveDisabled=[bool]$task;existingLoginEntryUpdated=[bool]$runValue;backup=$recordPath}
