param([Parameter(Mandatory=$true)][string]$PetId,[Parameter(Mandatory=$true)][string]$Package,[Parameter(Mandatory=$true)][string]$BackupDirectory)
$ErrorActionPreference='Stop'
$parsed=[Guid]::Empty
if(-not [Guid]::TryParseExact($PetId,'N',[ref]$parsed)){throw 'Invalid installed pet identifier.'}
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$petRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'HUDI/pets'))
$target=[IO.Path]::GetFullPath((Join-Path $petRoot $PetId))
if(-not $target.StartsWith($petRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Pet target outside catalog.'}
$source=[IO.Path]::GetFullPath($Package)
$backup=[IO.Path]::GetFullPath($BackupDirectory)
if(-not $backup.StartsWith((Join-Path $projectRoot 'output/'),[StringComparison]::OrdinalIgnoreCase)){throw 'Backup must remain in project output.'}
$installed=Join-Path $target 'pet.zip';$metadata=Join-Path $target 'pet.json'
if(-not (Test-Path -LiteralPath $installed)){throw 'Pet is not installed.'}
$expected=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach($name in @('pet.zip','pet.json')){if(-not (Test-Path -LiteralPath (Join-Path $backup $name))){Copy-Item -LiteralPath (Join-Path $target $name) -Destination (Join-Path $backup $name)}}
$selected=Join-Path $petRoot 'selected.txt'
$selectionBefore=if(Test-Path -LiteralPath $selected){[IO.File]::ReadAllText($selected)}else{''}
$info=[IO.File]::ReadAllText($metadata,[Text.Encoding]::UTF8) | ConvertFrom-Json
# Windows PowerShell 5.1 defaults Get-Content to ANSI for BOM-less files.
# Restore the authoritative package name as well as preventing future mojibake.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($source)
try {
 $entry=$archive.GetEntry('manifest.json')
 if($null -eq $entry){throw 'Package manifest missing.'}
 $reader=[IO.StreamReader]::new($entry.Open(),[Text.Encoding]::UTF8)
 try {$manifest=$reader.ReadToEnd() | ConvertFrom-Json} finally {$reader.Dispose()}
 $packageName=[string]$manifest.name
 if([string]::IsNullOrWhiteSpace($packageName) -or $packageName.Length -gt 80){throw 'Invalid package name.'}
 $info.name=$packageName
} finally {$archive.Dispose()}
$info.sha256=$expected
$exe=Join-Path $env:LOCALAPPDATA 'Programs/HUDI/HUDI.exe'
Add-Type @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class PetPackageClose {
 private delegate bool Callback(IntPtr h,IntPtr p);
 [DllImport("user32.dll")]private static extern bool EnumWindows(Callback c,IntPtr p);
 [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(IntPtr h,StringBuilder t,int n);
 [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public static bool Close(uint pid){bool sent=false;EnumWindows((h,p)=>{uint id;GetWindowThreadProcessId(h,out id);if(id!=pid)return true;var t=new StringBuilder(512);GetWindowText(h,t,512);if(t.ToString().EndsWith("\u2014 \u0436\u0438\u0432\u043e\u0439 \u043f\u0438\u0442\u043e\u043c\u0435\u0446"))sent=PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero)||sent;return true;},IntPtr.Zero);return sent;}
}
'@
$running=@(Get-CimInstance Win32_Process -Filter "Name = 'HUDI.exe'" | Where-Object {$_.ExecutablePath -eq $exe})
foreach($entry in $running){$process=Get-Process -Id $entry.ProcessId;if(-not [PetPackageClose]::Close([uint32]$entry.ProcessId)){throw 'Cannot close installed pet normally.'};if(-not $process.WaitForExit(6000)){throw 'Pet did not close normally.'}}
$settingsPath=Join-Path $env:LOCALAPPDATA 'HUDI/settings.json'
$geometryBefore=if(Test-Path -LiteralPath $settingsPath){[IO.File]::ReadAllText($settingsPath,[Text.Encoding]::UTF8) | ConvertFrom-Json}else{$null}
$staged=Join-Path $target 'pet.edge-fix.pending'
Copy-Item -LiteralPath $source -Destination $staged -Force
if((Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash -ne $expected){throw 'Staged package hash mismatch.'}
try {
 [IO.File]::Replace($staged,$installed,[NullString]::Value)
 $metadataPending=Join-Path $target 'pet.metadata.pending'
 [IO.File]::WriteAllText($metadataPending,($info | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 [IO.File]::Replace($metadataPending,$metadata,[NullString]::Value)
} catch {
 Copy-Item -LiteralPath (Join-Path $backup 'pet.zip') -Destination $installed -Force
 Copy-Item -LiteralPath (Join-Path $backup 'pet.json') -Destination $metadata -Force
 throw
} finally {
 if($running.Count -gt 0){$app=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Normal -PassThru}
}
$selectionAfter=if(Test-Path -LiteralPath $selected){[IO.File]::ReadAllText($selected)}else{''}
[pscustomobject]@{success=$true;petId=$PetId;hash=$expected;installedHashMatches=((Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -eq $expected);selectionUnchanged=($selectionBefore -eq $selectionAfter);processId=$app.Id;geometryBefore=$geometryBefore;backup=$backup} | ConvertTo-Json -Depth 5
