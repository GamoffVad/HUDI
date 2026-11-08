$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source=Join-Path $projectRoot 'src/NativeBackdrop/Backdrop.cpp'
$destination=Join-Path $projectRoot 'output/hudi-pet-v7/native'
$library=Join-Path $destination 'Hudi.Backdrop.dll'
if((Test-Path -LiteralPath $library) -and (Get-Item -LiteralPath $library).LastWriteTimeUtc -ge (Get-Item -LiteralPath $source).LastWriteTimeUtc -and (Get-Item -LiteralPath $library).LastWriteTimeUtc -ge (Get-Item -LiteralPath $PSCommandPath).LastWriteTimeUtc){exit 0}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio=(& $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
if(-not $visualStudio){throw 'Visual Studio C++ x64 tools are required to build the shaped matte backdrop.'}
$vcTools=(Get-ChildItem -LiteralPath (Join-Path $visualStudio 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdkRoot=(Get-ItemProperty 'HKLM:/SOFTWARE/Microsoft/Windows Kits/Installed Roots').KitsRoot10
$sdkVersion=(Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'cppwinrt/winrt/Windows.UI.Composition.h')} | Sort-Object Name -Descending | Select-Object -First 1).Name
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$arguments=@('/nologo','/std:c++20','/utf-8','/EHsc','/O2','/MT','/LD','/DUNICODE','/D_UNICODE',"/I$vcTools/include")
foreach($include in @('ucrt','shared','um','winrt','cppwinrt')){$arguments+="/I$(Join-Path $sdkRoot "Include/$sdkVersion/$include")"}
$arguments+=@($source,"/Fo$(Join-Path $destination 'Backdrop.obj')",'/link',"/OUT:$library","/IMPLIB:$(Join-Path $destination 'Hudi.Backdrop.lib')","/LIBPATH:$vcTools/lib/x64","/LIBPATH:$(Join-Path $sdkRoot "Lib/$sdkVersion/um/x64")","/LIBPATH:$(Join-Path $sdkRoot "Lib/$sdkVersion/ucrt/x64")",'windowsapp.lib','dwmapi.lib','d2d1.lib','CoreMessaging.lib','user32.lib','gdi32.lib','/OPT:REF','/OPT:ICF')
& (Join-Path $vcTools 'bin/Hostx64/x64/cl.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Native backdrop compilation failed.'}
