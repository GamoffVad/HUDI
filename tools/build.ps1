param([switch]$SkipUiChecks,[switch]$SkipPublisher,[switch]$UseLocalPublisherKey)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $projectRoot 'output/hudi-pet-v7'
Set-Location -LiteralPath $projectRoot
if (-not (Test-Path -LiteralPath 'output/hudi-pet-v6.3/sprites.zip')) { throw 'Missing approved v6.3 animation pack.' }
New-Item -ItemType Directory -Force $releaseRoot | Out-Null
function Run-Dotnet([string[]]$Arguments) { & dotnet @Arguments; if ($LASTEXITCODE -ne 0) { throw ('dotnet failed: '+($Arguments -join ' ')) } }
function Run-Check([string]$Exe,[string[]]$Arguments) {
    $check = Start-Process -FilePath $Exe -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw ('Check failed: '+$Exe+' '+($Arguments -join ' ')) }
}
if ($UseLocalPublisherKey) {
    Run-Dotnet @('run','--project','src/LicenseTool','--','init',"$releaseRoot/license-public.key")
} elseif (-not (Test-Path -LiteralPath "$releaseRoot/license-public.key")) {
    throw 'Missing public verification key. Restore it from Git, or explicitly use -UseLocalPublisherKey.'
}
Run-Dotnet @('run','--project','src/LicenseTool','--','verify',"$releaseRoot/license-tests.json")
Run-Dotnet @('publish','src/HudiPet/HudiPet.csproj','-c','Release',"-p:OutputPath=$releaseRoot/build/HudiPet/",'-o',"$releaseRoot/windows")
Run-Check "$releaseRoot/windows/HUDI.exe" @('--verify')
Run-Check "$releaseRoot/windows/HUDI.exe" @('--verify-chat')
Run-Check "$releaseRoot/windows/HUDI.exe" @('--verify-pets',"$releaseRoot/qa/pets")
if (-not $SkipUiChecks) { Run-Check "$releaseRoot/windows/HUDI.exe" @('--render-ui',"$releaseRoot/qa/chat") }
Run-Dotnet @('publish','src/Uninstaller/Uninstaller.csproj','-c','Release','-o',"$releaseRoot/uninstaller")
(Get-FileHash "$releaseRoot/windows/HUDI.exe" -Algorithm SHA256).Hash | Set-Content -LiteralPath "$releaseRoot/payload.sha256" -NoNewline
Run-Dotnet @('publish','src/Installer/Installer.csproj','-c','Release','-o',"$releaseRoot/distribution")
if (-not $SkipPublisher) { Run-Dotnet @('publish','src/KeyGenerator/KeyGenerator.csproj','-c','Release','-o',"$releaseRoot/publisher") }
if ($UseLocalPublisherKey -and -not (Test-Path -LiteralPath "$releaseRoot/publisher/Owner.hudi-key")) { Run-Dotnet @('run','--project','src/LicenseTool','--','owner',"$releaseRoot/publisher/Owner.hudi-key") }
if (-not $SkipPublisher) {
    Run-Check "$releaseRoot/publisher/PetKeys.exe" @('--verify-catalog',"$releaseRoot/qa/catalog")
    Run-Check "$releaseRoot/publisher/PetKeys.exe" @('--verify-generation',"$releaseRoot/qa/generation")
}
Run-Check "$releaseRoot/distribution/HUDI.Setup.exe" @('--verify-install',"$releaseRoot/qa/installed")
if (-not $SkipUiChecks) {
    Run-Check "$releaseRoot/distribution/HUDI.Setup.exe" @('--render-ui',"$releaseRoot/qa/installer.png")
    if (-not $SkipPublisher) { Run-Check "$releaseRoot/publisher/PetKeys.exe" @('--render-ui',"$releaseRoot/qa/key-generator.png") }
}
Copy-Item -LiteralPath README.md -Destination "$releaseRoot/README.md" -Force
New-Item -ItemType Directory -Force "$releaseRoot/docs" | Out-Null
Copy-Item -LiteralPath docs/pet-package-format.md -Destination "$releaseRoot/docs/pet-package-format.md" -Force
Copy-Item -LiteralPath docs/model-providers.md -Destination "$releaseRoot/docs/model-providers.md" -Force
Copy-Item -LiteralPath docs/window-transparency.md -Destination "$releaseRoot/docs/window-transparency.md" -Force
$artifacts=@("$releaseRoot/distribution/HUDI.Setup.exe")
if (-not $SkipPublisher) { $artifacts+="$releaseRoot/publisher/PetKeys.exe" }
Get-FileHash -LiteralPath $artifacts -Algorithm SHA256 | Select-Object Path,Hash | ConvertTo-Json | Set-Content -LiteralPath "$releaseRoot/checksums.json"
Write-Output 'HUDI v7: installer, pet, publisher tools and checks complete.'
