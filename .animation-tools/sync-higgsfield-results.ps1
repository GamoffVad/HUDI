param([Parameter(Mandatory=$true)][string]$AssetRoot, [Parameter(Mandatory=$true)][string]$Snapshot)
$ErrorActionPreference = 'Stop'
$planPath = Join-Path $AssetRoot 'generation-run.json'
$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
$snapshotData = Get-Content -LiteralPath $Snapshot -Raw | ConvertFrom-Json
foreach ($job in $snapshotData.jobs) {
    $clip = $plan.clips | Where-Object { $_.index -eq $job.index }
    if (-not $clip) { throw "Unexpected job index $($job.index)" }
    foreach ($name in @('job_id','status','result_url','thumbnail_url','error')) {
        if ($job.PSObject.Properties.Name -contains $name) { $clip | Add-Member -NotePropertyName $name -NotePropertyValue $job.$name -Force }
    }
    if ($job.status -eq 'completed') {
        $destination = Join-Path $AssetRoot ($clip.name + '.mp4')
        if (-not (Test-Path -LiteralPath $destination)) { Invoke-WebRequest -Uri $job.result_url -OutFile $destination }
    }
}
$complete = @($plan.clips | Where-Object { $_.status -eq 'completed' }).Count
$plan.status = if ($complete -eq $plan.clips.Count) { 'completed' } else { 'in_progress' }
$plan | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $planPath -Encoding utf8
Write-Output "Completed and saved $complete/$($plan.clips.Count) videos."
