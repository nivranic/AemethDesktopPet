param([string]$EvidencePath)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required.' }
$pluginRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$modelPath=Join-Path $pluginRoot 'native\PetModel.cs'
$harnessPath=Join-Path $PSScriptRoot 'ModelHarness.cs'
$webAssembly=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll'
$sharedSource=[System.IO.File]::ReadAllText($modelPath)+[Environment]::NewLine+[System.IO.File]::ReadAllText($harnessPath)
$referenceAssemblies=@(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | Select-Object -ExpandProperty FullName)+$webAssembly
Add-Type -TypeDefinition $sharedSource -ReferencedAssemblies $referenceAssemblies
$checks=[ZCodePet.ModelHarness]::Run() -split "`n"
$record=[ordered]@{ok=$true;checkCount=$checks.Count;checks=$checks;productionSource='native/PetModel.cs';sourceSHA256=(Get-FileHash -LiteralPath $modelPath).Hash;runtime='PowerShell '+$PSVersionTable.PSVersion;windowsShown=$false;scope='Shared model and typed protocol validation only; Framework JSON parser, native IPC and window rendering not executed.'}
if([string]::IsNullOrWhiteSpace($EvidencePath)) { $EvidencePath=Join-Path $PSScriptRoot 'model-test.json' }
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
[pscustomobject]$record | Select-Object ok,checkCount,windowsShown,scope
