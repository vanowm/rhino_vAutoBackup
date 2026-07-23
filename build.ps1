param(
    [switch]$ComposeOnly
)

Set-Location $PSScriptRoot

$projectFile = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.csproj' -File | Select-Object -First 1
if (-not $projectFile) {
    Write-Error "No project file found in $PSScriptRoot."
    exit 1
}

$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
$pendingFile = '.git\release-pending-message.txt'

function Get-Label([string]$name) {
    if ($name.StartsWith('v') -and $name.Length -gt 1) { return $name.Substring(1) }
    return $name
}

function Build-LabelList([System.Collections.Generic.List[string]]$items) {
    if ($items.Count -eq 0) { return '' }
    $labels = New-Object System.Collections.Generic.List[string]
    foreach ($item in $items) { [void]$labels.Add((Get-Label $item)) }
    if ($labels.Count -le 2) { return ($labels -join ', ') }
    return (($labels[0..1] -join ', ') + ', +' + ($labels.Count - 2) + ' more')
}

# Rebuild the message from the complete working-tree state before every normal build.
$changes = New-Object System.Collections.Generic.List[object]
git status --porcelain=v1 --untracked-files=all -- . | ForEach-Object {
    if ($_.Length -ge 4) {
        $status = $_.Substring(0, 2).Trim()
        $path = $_.Substring(3).Trim('"')
        if ($path.Contains(' -> ')) { $path = ($path -split ' -> ', 2)[-1].Trim('"') }
        $changes.Add([pscustomobject]@{ Status = $status; Path = ($path -replace '\\', '/') })
    }
}

$commandAdds = New-Object System.Collections.Generic.List[string]
$commandUpdates = New-Object System.Collections.Generic.List[string]
$hasBuildWorkflow = $false
$hasDocs = $false
$hasOptions = $false
$hasViews = $false
$hasResources = $false
$hasPluginCode = $false
$hasVerifiedFinalization = $false
$hasDll = $false

foreach ($change in $changes) {
    $path = $change.Path
    if ($path -eq 'README.md') { $hasDocs = $true }
    if ($path -eq 'AGENTS.md' -or $path -eq 'build.ps1' -or $path -eq 'Build.Release.targets' -or
        $path -eq $projectFile.Name -or $path -eq 'Properties/AssemblyInfo.cs') { $hasBuildWorkflow = $true }
    if ($path -like 'Options/*.cs') { $hasOptions = $true }
    if ($path -like 'Views/*.cs') { $hasViews = $true }
    if ($path -like 'Resources/*') { $hasResources = $true }
    if ($path -eq "bin/Release/net7.0-windows/$projectName.dll") { $hasDll = $true }

    if ($path -like 'Commands/*.cs') {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($path)
        if ($change.Status -eq '??' -or $change.Status -like 'A*') {
            if (-not $commandAdds.Contains($name)) { [void]$commandAdds.Add($name) }
        } elseif (-not $commandUpdates.Contains($name)) {
            [void]$commandUpdates.Add($name)
        }
    } elseif ($path -eq 'AutoBackupMonitor.cs') {
        $hasVerifiedFinalization = $true
    } elseif ($path -like '*.cs' -and $path -ne 'Properties/AssemblyInfo.cs' -and
              $path -notlike 'obj/*' -and $path -notlike 'bin/*') {
        $hasPluginCode = $true
    }
}

$parts = New-Object System.Collections.Generic.List[string]
if ($commandAdds.Count -eq 1) { $parts.Add('add ' + (Get-Label $commandAdds[0]) + ' command') }
elseif ($commandAdds.Count -gt 1) { $parts.Add('add commands: ' + (Build-LabelList $commandAdds)) }
if ($commandUpdates.Count -eq 1) { $parts.Add((Get-Label $commandUpdates[0]) + ': update') }
elseif ($commandUpdates.Count -gt 1) { $parts.Add('update commands: ' + (Build-LabelList $commandUpdates)) }
if ($hasOptions) { $parts.Add('options: update') }
if ($hasViews) { $parts.Add('panel: update') }
if ($hasResources) { $parts.Add('resources: update') }
if ($hasVerifiedFinalization) { $parts.Add('verify backup archives and finalize asynchronously') }
if ($hasPluginCode) { $parts.Add('plugin: update') }
if ($hasDocs) { $parts.Add('docs: update') }
if ($hasBuildWorkflow) { $parts.Add('build: align release workflow') }
if ($hasDll -and $parts.Count -eq 0) { $parts.Add('build: publish release binary') }
if ($parts.Count -eq 0) { $parts.Add('maintenance: apply project updates') }

$summary = ($parts -join '; ')
$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot $pendingFile), $summary, $encoding)
Write-Host "Composed pending message: $summary" -ForegroundColor Green

if ($ComposeOnly) { exit 0 }

$buildOutput = @(& dotnet build $projectFile.FullName -c Release --no-incremental 2>&1)
$buildExitCode = $LASTEXITCODE
$buildOutput | ForEach-Object { Write-Host $_ }

if ($buildExitCode -ne 0) {
    $text = $buildOutput -join [Environment]::NewLine
    if ($text -match 'being used by another process' -or
        $text -match 'cannot access the file' -or
        $text -match 'Cannot write file') {
        Write-Host "WARNING: $projectName release DLL is locked; the pending message remains for the next build." -ForegroundColor Yellow
        exit 0
    }
    exit $buildExitCode
}
