param([switch]$SelfTest, [string]$OutputDirectory, [string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required.' }
$pluginRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) { throw "System .NET Framework compiler not found: $compilerPath" }
$nativeSource = Join-Path $PSScriptRoot 'Companion.cs'
$modelSource = Join-Path $PSScriptRoot 'PetModel.cs'
$binaryDir = if($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $pluginRoot 'bin' }
[System.IO.Directory]::CreateDirectory($binaryDir) | Out-Null
$binaryPath = Join-Path $binaryDir 'ZCodePet.exe'
& $compilerPath /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu "/out:$binaryPath" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\pet.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll "$PSScriptRoot\AssemblyInfo.cs" $modelSource $nativeSource
if ($LASTEXITCODE -ne 0) { throw "Native compilation failed: $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) { throw 'Compiler returned success but the native executable is missing; inspect local execution restrictions.' }
if ($SelfTest) {
    $atlasPath = Join-Path $pluginRoot 'assets\aemeath-mini\spritesheet.png'
    $evidenceDir = if($EvidenceDirectory) { [System.IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path $pluginRoot 'tests\native\evidence' }
    [System.IO.Directory]::CreateDirectory($evidenceDir) | Out-Null
    $nativeProcess = Start-Process -FilePath $binaryPath -ArgumentList @('--self-test', '--atlas', ('"' + $atlasPath + '"'), '--render-dir', ('"' + $evidenceDir + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($nativeProcess.ExitCode -ne 0) { throw "Native render self-test failed: $($nativeProcess.ExitCode)" }
    $pipeName = 'ZCodePet-selftest-' + [guid]::NewGuid().ToString('N')
    $testDataDir = Join-Path $evidenceDir ('settings-' + [guid]::NewGuid().ToString('N'))
    $nativeProcess = Start-Process -FilePath $binaryPath -ArgumentList @('--pipe-self-test','--atlas', ('"' + $atlasPath + '"'),'--pipe',$pipeName,'--data-dir',('"' + $testDataDir + '"'),'--render-dir',('"' + $evidenceDir + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($nativeProcess.ExitCode -ne 0) { throw "Native pipe self-test failed: $($nativeProcess.ExitCode)" }
    Get-Content -LiteralPath (Join-Path $evidenceDir 'self-test.json'), (Join-Path $evidenceDir 'pipe-self-test.json') | ConvertFrom-Json | Select-Object ok, frameCount, windowsShown
}
Get-Item -LiteralPath $binaryPath | Select-Object FullName, Length
