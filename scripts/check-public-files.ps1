$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskFiles = @(git -C $taskRoot ls-files)
if ($LASTEXITCODE -ne 0 -or $taskFiles.Count -eq 0) { throw 'Initialize the repository and add the intended files first.' }
$taskAllowed = '^(README\.md|\.gitignore|\.gitattributes|apps/desktop/[^/]+\.(cs|config|manifest)|scripts/[^/]+\.ps1|assets/(README\.md|app-icon\.svg|app-icon\.ico|app-icon-\d+\.png)|docs/public/[^/]+\.md)$'
$taskSecret = '(?i)([A-Z]:[\\/]Users[\\/]|[A-Z]:[\\/]workspace[\\/]|-----BEGIN [A-Z ]*PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9]{24,}|eyJ[A-Za-z0-9_-]{30,}\.)'
$taskErrors = @()
foreach ($taskFile in $taskFiles) {
    if ($taskFile -notmatch $taskAllowed) { $taskErrors += ('Unapproved path: ' + $taskFile); continue }
    if ($taskFile -match '\.(png|ico)$') { continue }
    $taskText = (git -C $taskRoot show (':' + $taskFile)) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw ('Cannot inspect index entry: ' + $taskFile) }
    if ($taskText -match $taskSecret) { $taskErrors += ('Private path or credential pattern: ' + $taskFile) }
}
if ($taskErrors.Count) { throw ($taskErrors -join "`n") }
[pscustomobject]@{FilesChecked=$taskFiles.Count; UnapprovedPaths=0; DetectedPrivatePatterns=0}
