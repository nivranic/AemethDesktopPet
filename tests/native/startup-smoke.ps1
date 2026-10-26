param([Parameter(Mandatory=$true)][string]$EvidencePath)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required.' }
$pluginRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$exe=Join-Path $pluginRoot 'bin\ZCodePet.exe'
$bridge=Join-Path $pluginRoot 'scripts\bridge.cjs'
$checks=[Collections.Generic.List[string]]::new()
function Invoke-Pet([string]$operation) {
    $raw=& node -e 'const b=require(process.argv[1]); const op=process.argv[2]; const p=op.startsWith("pet_")?b.callTool(op):b.request({op}); p.then(r=>console.log(JSON.stringify(r))).catch(e=>{console.error(e.message);process.exitCode=1})' $bridge $operation
    if($LASTEXITCODE -ne 0) { throw "Pet operation failed: $operation" }
    $raw | ConvertFrom-Json
}
function Assert-Pet([bool]$condition,[string]$description) {
    if(-not $condition) { throw $description }
    $checks.Add($description)
}
$initial=Invoke-Pet 'status'
Assert-Pet ($initial.ok -and $initial.visible) 'No-argument startup from an unrelated working directory is visible and responds over the default pipe'
$originalPid=$initial.pid
$hidden=Invoke-Pet 'pet_hide'
Assert-Pet (-not $hidden.visible) 'Explicit hide is respected'
Assert-Pet (-not $hidden.animationTimerRunning -and $hidden.animationIntervalMs -eq 0) 'Hidden pet stops the animation timer'
$duplicate=Start-Process -FilePath $exe -WorkingDirectory $env:TEMP -PassThru
if(-not $duplicate.WaitForExit(10000)) { throw 'Duplicate launch did not return' }
Assert-Pet ($duplicate.ExitCode -eq 0) 'Duplicate no-argument launch returns success'
$shown=Invoke-Pet 'status'
Assert-Pet ($shown.visible -and $shown.pid -eq $originalPid) 'Duplicate launch reveals the original instance without creating a second resident process'
Assert-Pet ($shown.animationTimerRunning -and $shown.animationIntervalMs -ge 16) 'Revealed pet resumes adaptive animation scheduling'
$flag=Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'ZCodePixelPet\autostart.json') -Raw | ConvertFrom-Json
Assert-Pet ($flag.autoStart -eq $true) 'Manual show restores automatic-display permission'
$null=Invoke-Pet 'quit'
Start-Sleep -Milliseconds 700
Assert-Pet (-not (Get-Process -Id $originalPid -ErrorAction SilentlyContinue)) 'Quit terminates the original instance'
$flag=Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'ZCodePixelPet\autostart.json') -Raw | ConvertFrom-Json
Assert-Pet ($flag.autoStart -eq $false) 'Native quit persists the user opt-out without depending on the Node bridge'
# A separate manually started instance is terminated to model external cleanup accurately.
$external=Start-Process -FilePath $exe -WorkingDirectory $env:TEMP -PassThru
Start-Sleep -Seconds 2
$externalStatus=Invoke-Pet 'status'
Assert-Pet ($externalStatus.pid -eq $external.Id) 'External-stop fixture owns the exact process being terminated'
Stop-Process -Id $external.Id
Start-Sleep -Milliseconds 700
& node -e 'const b=require(process.argv[1]); (async()=>{if(!b.autoStartEnabled()) throw new Error("Expected autoStart=true"); for(const event of ["SessionStart","UserPromptSubmit"]){try{await b.notifyHook({hookEventName:event,sessionId:"startup-smoke"});throw new Error("Unexpected live pipe")}catch(e){if(!["ENOENT","ECONNREFUSED"].includes(e.code))throw e}}})().catch(e=>{console.error(e.message);process.exitCode=1})' $bridge
Assert-Pet ($LASTEXITCODE -eq 0) 'Task hooks tolerate a missing companion without launching it even when automatic display is allowed'
Start-Sleep -Milliseconds 700
$unexpected=Get-Process -Name ZCodePet -ErrorAction SilentlyContinue | Where-Object {$_.Path -ieq $exe}
Assert-Pet (-not $unexpected) 'No companion process reappeared after task hooks'
$restarted=Start-Process -FilePath $exe -WorkingDirectory $env:TEMP -PassThru
Start-Sleep -Seconds 2
$final=Invoke-Pet 'status'
Assert-Pet ($final.ok -and $final.visible -and $final.pid -eq $restarted.Id) 'Manual restart returns a visible, responsive pet'
$task=Get-ScheduledTask -TaskName ZCodePetKeepAlive -TaskPath '\' -ErrorAction SilentlyContinue
Assert-Pet (-not $task -or -not $task.Settings.Enabled) 'Old keepalive task is absent or disabled'
$runValue=(Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run').ZCodePixelPet
Assert-Pet (-not $runValue -or $runValue -ceq ('"'+$exe+'" --wait-host')) 'Existing login entry directly starts the EXE'
$report=[ordered]@{ok=$true;date=(Get-Date).ToString('o');checks=$checks.ToArray();finalPid=$final.pid;visible=$final.visible;binarySHA256=(Get-FileHash -LiteralPath $exe).Hash;securitySoftwareVerdictVerified=$false;loginCycleExecuted=$false}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
$report | ConvertTo-Json -Depth 5
