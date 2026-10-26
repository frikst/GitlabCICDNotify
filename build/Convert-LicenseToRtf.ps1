param(
    [Parameter(Mandatory = $true)]
    [string] $LicensePath,
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

# Keep the installer license identical to the repository's canonical LICENSE.
$license = [IO.File]::ReadAllText($LicensePath)
$escaped = $license.Replace('\', '\\').Replace('{', '\{').Replace('}', '\}')
$escaped = [regex]::Replace($escaped, '\r\n|\r|\n', '\par' + [Environment]::NewLine)
$rtf = '{\rtf1\ansi\deff0{\fonttbl{\f0 Courier New;}}\f0\fs16 ' + $escaped + '}'
[IO.File]::WriteAllText($OutputPath, $rtf, [Text.UTF8Encoding]::new($false))
