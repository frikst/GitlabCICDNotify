[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$ChangelogPath = 'CHANGELOG.md',
    [string]$OutputPath = 'artifacts/release-notes.md'
)

$ErrorActionPreference = 'Stop'
if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "Expected a stable version tag vX.Y.Z, got '$Tag'."
}
$version = $Tag.Substring(1)
# Windows Installer ProductVersion: major/minor <= 255, build <= 65535.
$parts = $version.Split('.')
if ([decimal]$parts[0] -gt 255 -or [decimal]$parts[1] -gt 255 -or [decimal]$parts[2] -gt 65535) {
    throw "Version '$version' exceeds MSI limits (255.255.65535)."
}

$changelog = Get-Content -LiteralPath $ChangelogPath -Raw
$escaped = [regex]::Escape($version)
$sections = [regex]::Matches($changelog, "(?m)^## (?:\[$escaped\]|$escaped)(?:[ \t]+-[ \t]+[^\r\n]+)?[ \t]*\r?$")
if ($sections.Count -ne 1) {
    throw "CHANGELOG must contain exactly one '## [$version] - YYYY-MM-DD' or '## $version' section. Unreleased is not used."
}
$tail = $changelog.Substring($sections[0].Index + $sections[0].Length).TrimStart("`r", "`n")
$next = [regex]::Match($tail, '(?m)^## ')
if ($next.Success) { $tail = $tail.Substring(0, $next.Index) }
# Keep a Changelog reference definitions are not release notes.
$tail = [regex]::Replace($tail, '(?m)^\[[^\]\r\n]+\]:[^\r\n]*\r?\n?', '').Trim()
if ([string]::IsNullOrWhiteSpace($tail) -or $tail -notmatch '(?m)^[-*] \S') {
    throw "Changelog section '$version' contains no change entries."
}
$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
Set-Content -LiteralPath $OutputPath -Value $tail -Encoding utf8NoBOM
if ($env:GITHUB_ENV) { Add-Content -LiteralPath $env:GITHUB_ENV -Value "RELEASE_VERSION=$version" -Encoding utf8 }
Write-Output "Release version: $version; notes: $OutputPath"
