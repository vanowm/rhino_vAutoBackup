param(
    [switch]$ComposeOnly,
    [string]$Message
)

Set-Location $PSScriptRoot

$projectFile = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.csproj' -File | Select-Object -First 1
if (-not $projectFile) {
    Write-Error "No project file found in $PSScriptRoot."
    exit 1
}

$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
$pendingFile = Join-Path $PSScriptRoot '.git\release-pending-message.txt'

if ([string]::IsNullOrWhiteSpace($Message)) {
    Write-Error 'A semantic release message is required. Describe the behavior and build changes since the last commit with -Message.'
    exit 1
}

$summary = $Message.Trim()
$genericPart = '(?i)(^|;\s*)(?:add commands?:\s*[^;]+|update commands?:\s*[^;]+|[^:;]+:\s*update|build:\s*(?:align release workflow|publish release binary)|maintenance:\s*apply project updates)(?=\s*(?:;|$))'
if ($summary.Length -lt 20 -or $summary -match $genericPart) {
    Write-Error 'The release message must describe the actual behavior changed; category-only summaries such as "panel: update" are rejected.'
    exit 1
}

if ($summary -match '(?i)\b[^\s;]+\.py\b') {
    Write-Error 'Release messages must describe plug-in behavior without naming source script files.'
    exit 1
}

$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($pendingFile, $summary, $encoding)
Write-Host "Saved semantic pending message: $summary" -ForegroundColor Green

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
