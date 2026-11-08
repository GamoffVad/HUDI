param([Parameter(Mandatory=$true)][string]$Version,[Parameter(Mandatory=$true)][string]$BackupDirectory)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/HUDI'))
$installed=Join-Path $installRoot 'HUDI.exe'
$candidate=Join-Path $projectRoot 'output/hudi-pet-v7/windows/HUDI.exe'
$backup=[IO.Path]::GetFullPath($BackupDirectory)
if (-not $backup.StartsWith((Join-Path $projectRoot 'output/') ,[StringComparison]::OrdinalIgnoreCase)) { throw 'Backup must stay in the project output directory.' }
if ((Get-Item -LiteralPath $candidate).VersionInfo.FileVersion -ne "$Version.0") { throw 'Unexpected candidate version.' }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $backup 'HUDI.exe'))) { Copy-Item -LiteralPath $installed -Destination (Join-Path $backup 'HUDI.exe') }
$savedGeometry=Join-Path $env:LOCALAPPDATA 'HUDI/settings.json'
$geometryBackup=Join-Path $backup 'settings.json'
if ((Test-Path -LiteralPath $savedGeometry) -and -not (Test-Path -LiteralPath $geometryBackup)) { Copy-Item -LiteralPath $savedGeometry -Destination $geometryBackup }
$before=@{}
foreach ($name in @('favorite-sites.json','mouse-controls.json','notes.json')) {
    $file=Join-Path $env:LOCALAPPDATA "HUDI/$name"
    if (Test-Path -LiteralPath $file) { $before[$name]=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class HudiWindowClose {
    private delegate bool EnumCallback(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback,IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    public static bool Close(uint pid) {
        bool sent=false;
        EnumWindows((window,state)=>{
            uint owner;GetWindowThreadProcessId(window,out owner);
            if(owner!=pid)return true;
            var title=new StringBuilder(512);GetWindowText(window,title,title.Capacity);
            // ASCII escapes also work when Windows PowerShell reads UTF-8 without a BOM.
            if(title.ToString().EndsWith("\u2014 \u0436\u0438\u0432\u043e\u0439 \u043f\u0438\u0442\u043e\u043c\u0435\u0446",StringComparison.Ordinal))sent=PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero)||sent;
            return true;
        },IntPtr.Zero);
        return sent;
    }
}
'@
foreach ($entry in @(Get-CimInstance Win32_Process -Filter "Name = 'HUDI.exe'" | Where-Object { $_.ExecutablePath -eq $installed })) {
    $process=Get-Process -Id $entry.ProcessId
    if (-not [HudiWindowClose]::Close([uint32]$entry.ProcessId)) { throw 'Cannot locate the installed pet window for a normal close.' }
    if (-not $process.WaitForExit(6000)) { throw 'Installed HUDI did not close normally.' }
}
$staged=Join-Path $installRoot 'HUDI.pending-update.exe'
Copy-Item -LiteralPath $candidate -Destination $staged -Force
$expected=(Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash -ne $expected) { throw 'Staged hash mismatch.' }
[IO.File]::Replace($staged,$installed,[NullString]::Value)
$metadata=Join-Path $installRoot 'installation.json'
if (Test-Path -LiteralPath $metadata) {
    Copy-Item -LiteralPath $metadata -Destination (Join-Path $backup 'installation.json') -Force
    $info=Get-Content -LiteralPath $metadata -Raw | ConvertFrom-Json
    $info.version=$Version
    $info | ConvertTo-Json | Set-Content -LiteralPath $metadata -Encoding utf8
}
$registry='HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/HUDI'
if (Test-Path -LiteralPath $registry) {
    Set-ItemProperty -LiteralPath $registry -Name DisplayVersion -Value $Version
    Set-ItemProperty -LiteralPath $registry -Name EstimatedSize -Value ([int]((Get-Item -LiteralPath $installed).Length/1024))
}
$app=Start-Process -FilePath $installed -WorkingDirectory $installRoot -WindowStyle Normal -PassThru
$unchanged=$true
foreach ($name in $before.Keys) { $unchanged=$unchanged -and ((Get-FileHash -LiteralPath (Join-Path $env:LOCALAPPDATA "HUDI/$name") -Algorithm SHA256).Hash -eq $before[$name]) }
[PSCustomObject]@{ProcessId=$app.Id;Version=(Get-Item -LiteralPath $installed).VersionInfo.FileVersion;InstalledHashMatches=((Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -eq $expected);UserListsUnchanged=$unchanged;Backup=$backup} | ConvertTo-Json
