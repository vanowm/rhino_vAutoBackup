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

$messageWasSupplied = -not [string]::IsNullOrWhiteSpace($Message)
$messageWasPrompted = $false

if ($messageWasSupplied) {
    $summary = $Message.Trim()
} elseif (Test-Path -LiteralPath $pendingFile) {
    $summary = [System.IO.File]::ReadAllText($pendingFile).Trim()
    Write-Host "Using existing semantic pending message: $summary" -ForegroundColor Green
} elseif ($ComposeOnly) {
    Write-Error 'A semantic release message is required. Supply it with -Message.'
    exit 1
} else {
    $promptedMessage = Read-Host 'Describe plug-in behavior and build changes since the last commit'
    $summary = if ($null -eq $promptedMessage) { '' } else { $promptedMessage.Trim() }
    $messageWasPrompted = $true
}

$genericPart = '(?i)(^|;\s*)(?:add commands?:\s*[^;]+|update commands?:\s*[^;]+|[^:;]+:\s*update|build:\s*(?:align release workflow|publish release binary)|maintenance:\s*apply project updates)(?=\s*(?:;|$))'
if ([string]::IsNullOrWhiteSpace($summary) -or $summary.Length -lt 20 -or $summary -match $genericPart) {
    Write-Error 'The release message must describe the actual behavior changed; category-only summaries such as "panel: update" are rejected.'
    exit 1
}

if ($summary -match '(?i)\b[^\s;]+\.py\b') {
    Write-Error 'Release messages must describe plug-in behavior without naming source script files.'
    exit 1
}

if ($messageWasSupplied -or $messageWasPrompted) {
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($pendingFile, $summary, $encoding)
    Write-Host "Saved semantic pending message: $summary" -ForegroundColor Green
}

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
