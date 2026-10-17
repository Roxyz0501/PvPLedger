$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$build = Join-Path $projectRoot 'PvPLedger/bin/Release'
$output = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force $output | Out-Null
$archivePath = Join-Path $output 'PvPLedger-1.0.0.0.zip'
$allow = @('PvPLedger.dll', 'PvPLedger.json', 'PvPLedger.deps.json', 'LICENSE', 'NOTICE', 'PvpStats-LICENSE.txt', 'images/icon.png')
Add-Type -AssemblyName System.IO.Compression
$file = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::Create)
try {
    $zip = [System.IO.Compression.ZipArchive]::new($file, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $allow) {
            $source = Join-Path $build $name
            if (!(Test-Path -LiteralPath $source)) { throw "Missing package file: $name" }
            $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
            $stream = $entry.Open()
            try { $bytes = [System.IO.File]::ReadAllBytes($source); $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $file.Dispose() }
$manifest = Get-Content -LiteralPath (Join-Path $build 'PvPLedger.json') -Raw | ConvertFrom-Json
if ($manifest.Author -ne 'Roxyz0501' -or $manifest.InternalName -ne 'PvPLedger' -or $manifest.AssemblyVersion -ne '1.0.0.0' -or !$manifest.RepoUrl -or !$manifest.IconUrl) { throw 'Invalid packaged metadata' }
Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
