Set-Location $PSScriptRoot

# Version is auto-computed at build time via $(BuildVersion) in vAutoBackup.csproj.
# Do not manually update vAutoBackup.csproj or Properties\AssemblyInfo.cs.

$pendingFile = '.git\vautobackup-pending-message.txt'

function Get-Label([string]$name) {
    if ($name.StartsWith('vAutoBackup') -and $name.Length -gt 11) { return $name.Substring(11) }
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

# Generate the commit body before building unless one was supplied explicitly.
if (-not (Test-Path $pendingFile)) {
    $changes = @()
    git diff --name-status -- . | ForEach-Object {
        $parts = $_ -split '\t+', 2
        if ($parts.Count -ge 2) {
            $changes += [pscustomobject]@{ Status = $parts[0]; Path = $parts[1] }
        }
    }

    $cmdAdds = New-Object System.Collections.Generic.List[string]
    $cmdMods = New-Object System.Collections.Generic.List[string]
    $hasMonitor = $false
    $hasBuildConfig = $false

    foreach ($change in $changes) {
        $path = ($change.Path -replace '\\','/')
        if ($path -eq 'AutoBackupMonitor.cs') { $hasMonitor = $true }
        if ($path -eq 'vAutoBackup.csproj' -or $path -eq 'Properties/AssemblyInfo.cs' -or $path -eq 'build.ps1') {
            $hasBuildConfig = $true
        }
        if ($path -like 'Commands/*.cs') {
            $name = [System.IO.Path]::GetFileNameWithoutExtension($path)
            if ($name -like 'v*') {
                if ($change.Status -like 'A*') {
                    if (-not $cmdAdds.Contains($name)) { [void]$cmdAdds.Add($name) }
                } else {
                    if (-not $cmdMods.Contains($name)) { [void]$cmdMods.Add($name) }
                }
            }
        }
    }

    $messageParts = New-Object System.Collections.Generic.List[string]
    if ($hasMonitor) { $messageParts.Add('AutoBackup: update') }
    if ($cmdAdds.Count -eq 1) { $messageParts.Add('add ' + (Get-Label $cmdAdds[0]) + ' command') }
    elseif ($cmdAdds.Count -gt 1) { $messageParts.Add('add commands: ' + (Build-LabelList $cmdAdds)) }
    if ($cmdMods.Count -eq 1) { $messageParts.Add((Get-Label $cmdMods[0]) + ': update') }
    elseif ($cmdMods.Count -gt 1) { $messageParts.Add('update: ' + (Build-LabelList $cmdMods)) }
    if ($hasBuildConfig -and $messageParts.Count -eq 0) { $messageParts.Add('build: sync release workflow') }
    if ($messageParts.Count -eq 0) { $messageParts.Add('maintenance: apply project updates') }

    $summary = ($messageParts -join '; ')
    Set-Content -Path $pendingFile -Value $summary -NoNewline -Encoding utf8
    Write-Host "Created pending message file: $pendingFile -> $summary" -ForegroundColor Green
}

$buildOutput = dotnet build vAutoBackup.csproj -c Release --no-incremental 2>&1
$buildExitCode = $LASTEXITCODE

if ($buildExitCode -ne 0) {
    if ($buildOutput -match 'being used by another process' -or
        $buildOutput -match 'cannot access the file' -or
        $buildOutput -match 'Cannot write file') {
        Write-Host 'WARNING: vAutoBackup build reported a locked DLL; the pending commit message was preserved for the next build.' -ForegroundColor Yellow
        exit 0
    }

    Write-Host $buildOutput
    exit $buildExitCode
}

Write-Host $buildOutput
