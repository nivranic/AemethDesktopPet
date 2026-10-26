param([Parameter(Mandatory)][string]$BinaryPath,[Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7) {throw 'PowerShell 7 required'}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$atlasPath=Join-Path $root 'assets\aemeath-mini\spritesheet.png'
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$dataDir=Join-Path $evidence ('settings-'+[Guid]::NewGuid().ToString('N'))
$pipeName='ZCodePet-hover-'+[Guid]::NewGuid().ToString('N')
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HoverNative {
  public struct POINT { public int X,Y; }
  public struct RECT { public int Left,Top,Right,Bottom; }
  public delegate bool EnumProc(IntPtr h,IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb,IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int z,uint flags);
  public static IntPtr Find(int pid) {
    IntPtr found=IntPtr.Zero;
    EnumWindows(delegate(IntPtr h,IntPtr p){uint owner;GetWindowThreadProcessId(h,out owner);if(owner==pid && IsWindowVisible(h)){found=h;return false;}return true;},IntPtr.Zero);
    return found;
  }
}
'@
function Request-Pet($message) {
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect(2000)
        $writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),1024,$true)
        $reader=[IO.StreamReader]::new($pipe,[Text.UTF8Encoding]::new($false),$false,1024,$true)
        try {
            $writer.AutoFlush=$true;$writer.WriteLine(($message | ConvertTo-Json -Compress))
            $pending=$reader.ReadLineAsync();if(-not $pending.Wait(2000)){throw 'IPC read timed out'}
            $reply=$pending.Result | ConvertFrom-Json
            if(-not $reply.ok){throw $reply.error};return $reply
        } finally {$writer.Dispose();$reader.Dispose()}
    } finally {$pipe.Dispose()}
}
$checks=[Collections.Generic.List[string]]::new()
$samples=[Collections.Generic.List[object]]::new()
function Assert-Check([bool]$condition,[string]$message) {if(-not $condition){throw $message};$checks.Add($message)}
function Await-State([scriptblock]$condition) {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do {$s=Request-Pet @{op='status'};if(& $condition $s){return $s};Start-Sleep -Milliseconds 40}while($watch.ElapsedMilliseconds -lt 1800)
    throw ('State did not match: '+($s|ConvertTo-Json -Compress))
}
$previousDpi=[HoverNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
$cursor=[HoverNative+POINT]::new();[HoverNative]::GetCursorPos([ref]$cursor)|Out-Null
$pet=$null;$atlas=$null
try {
    $pet=Start-Process -FilePath $BinaryPath -ArgumentList @('--atlas',('"'+$atlasPath+'"'),'--pipe',$pipeName,'--data-dir',('"'+$dataDir+'"')) -PassThru
    Start-Sleep -Milliseconds 2600
    $handle=[HoverNative]::Find($pet.Id);if($handle -eq [IntPtr]::Zero){throw 'Test pet window not found'}
    [HoverNative]::SetWindowPos($handle,[IntPtr](-1),0,0,0,0,0x13)|Out-Null
    $rect=[HoverNative+RECT]::new();[HoverNative]::GetWindowRect($handle,[ref]$rect)|Out-Null
    $outsideX=[Math]::Max(0,$rect.Left-100);$outsideY=[Math]::Max(0,$rect.Top-100)
    [HoverNative]::SetCursorPos($outsideX,$outsideY)|Out-Null
    $baseline=Request-Pet @{op='status'}
    Assert-Check ($baseline.state -eq 'idle' -and $baseline.sessionCount -eq 0) 'Fixture starts in real idle with zero task sessions'
    $atlas=[Drawing.Bitmap]::FromFile($atlasPath)
    $best=1e9;$localX=96;$localY=104
    for($y=32;$y -lt 192;$y+=4){for($x=24;$x -lt 168;$x+=4){
        if($atlas.GetPixel($baseline.frame*192+$x,$baseline.row*208+$y).A -gt 200){
            $distance=($x-96)*($x-96)+($y-104)*($y-104)
            if($distance -lt $best){$best=$distance;$localX=$x;$localY=$y}
        }
    }}
    $insideX=$rect.Left+[int]($localX*($rect.Right-$rect.Left)/192)
    $insideY=$rect.Top+[int]($localY*($rect.Bottom-$rect.Top)/208)
    $point=[HoverNative+POINT]::new();$point.X=$insideX;$point.Y=$insideY
    [ordered]@{pid=$pet.Id;handle=$handle.ToInt64();windowAtPoint=([HoverNative]::WindowFromPoint($point)).ToInt64();left=$rect.Left;top=$rect.Top;width=($rect.Right-$rect.Left);height=($rect.Bottom-$rect.Top);insideX=$insideX;insideY=$insideY;atlasX=$localX;atlasY=$localY} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'coordinates.json') -Encoding utf8NoBOM
    [HoverNative]::SetCursorPos($insideX,$insideY)|Out-Null
    $enter=Await-State {param($s) $s.state -eq 'jumping'};$samples.Add($enter)
    Assert-Check ($enter.row -eq 4) 'Real mouse entry triggers the hover animation'
    [HoverNative]::SetCursorPos($outsideX,$outsideY)|Out-Null
    $leave=Await-State {param($s) $s.interaction -eq 'hover-leave'};$samples.Add($leave)
    Assert-Check ($leave.row -eq 7 -and $leave.state -eq 'idle' -and $leave.sessionCount -eq 0) 'Real mouse leave plays sunglasses while actual task state remains idle'
    Start-Sleep -Milliseconds 300
    $advanced=Request-Pet @{op='status'};$samples.Add($advanced)
    Assert-Check ($advanced.row -eq 7 -and $advanced.frame -ne $leave.frame) 'Sunglasses frames advance rather than showing a static icon'
    $bitmap=[Drawing.Bitmap]::new(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try {$graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $evidence 'idle-hover-leave.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
    Start-Sleep -Milliseconds 2600
    $settled=Request-Pet @{op='status'};$samples.Add($settled)
    Assert-Check ($settled.interaction -eq 'none' -and $settled.state -eq 'idle' -and $settled.row -eq 0) 'Interaction expires and returns to real idle'
    [HoverNative]::SetCursorPos($insideX,$insideY)|Out-Null
    $null=Await-State {param($s) $s.state -eq 'jumping'}
    [HoverNative]::SetCursorPos($outsideX,$outsideY)|Out-Null
    $replay=Await-State {param($s) $s.interaction -eq 'hover-leave'}
    Assert-Check ($replay.row -eq 7 -and $replay.frame -le 1) 'Every new mouse leave restarts the animation'
    [HoverNative]::SetCursorPos($insideX,$insideY)|Out-Null
    $reentry=Await-State {param($s) $s.state -eq 'jumping'}
    Assert-Check ($reentry.interaction -eq 'none') 'Mouse reentry cancels the previous leave effect'
    $null=Request-Pet @{op='session';sessionId=('a'*64);state='waiting'}
    $null=Request-Pet @{op='configure';followPointer=$true}
    [HoverNative]::SetCursorPos($outsideX,$outsideY)|Out-Null
    $waiting=Await-State {param($s) $s.interaction -eq 'hover-leave'};$samples.Add($waiting)
    Assert-Check ($waiting.state -eq 'waiting' -and $waiting.row -eq 7) 'Leave feedback also plays over a real task state without rewriting it'
    $hidden=Request-Pet @{op='hide'}
    Assert-Check ($hidden.interaction -eq 'none' -and -not $hidden.animationTimerRunning) 'Hiding cancels interaction and stops animation scheduling'
    $null=Request-Pet @{op='quit'}
    Assert-Check ($pet.WaitForExit(4000)) 'Isolated pet exits cleanly'
    [ordered]@{ok=$true;checks=$checks.ToArray();samples=$samples.ToArray();realMouseMovement=$true;isolatedDataDirectory=$true;binarySHA256=(Get-FileHash -LiteralPath $BinaryPath).Hash} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'hover-leave-smoke.json') -Encoding utf8NoBOM
    [pscustomobject]@{ok=$true;checks=$checks.Count;evidence=$evidence}
} finally {
    [HoverNative]::SetCursorPos($cursor.X,$cursor.Y)|Out-Null
    if($previousDpi -ne [IntPtr]::Zero){[HoverNative]::SetThreadDpiAwarenessContext($previousDpi)|Out-Null}
    if($atlas){$atlas.Dispose()}
    if($pet){$pet.Refresh();if(-not $pet.HasExited){try{$null=Request-Pet @{op='quit'}}catch{Stop-Process -Id $pet.Id -ErrorAction SilentlyContinue}}}
}
