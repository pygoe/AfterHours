param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [string]$OutputDirectory = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$resolvedGame = (Resolve-Path -LiteralPath $GameDir).Path
$log = Join-Path $resolvedGame 'MelonLoader/Latest.log'
if (!(Test-Path -LiteralPath $log -PathType Leaf)) { throw 'MelonLoader/Latest.log not found. Check the game directory.' }
$files = @($log)
$reports = Join-Path $resolvedGame 'UserData/AfterHours/Reports'
if (Test-Path -LiteralPath $reports) {
    $latest = Get-ChildItem -LiteralPath $reports -Filter 'AfterHours-support-*.json' -File |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($latest) { $files += $latest.FullName }
}
if ($files.Count -eq 1) { Write-Warning 'No After Hours report found. The ZIP will contain only Latest.log.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zip = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path ('AfterHours-support-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.zip')
Add-Type -AssemblyName System.IO.Compression
$output = [System.IO.File]::Open($zip, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
$archive = $null
try {
    $archive = [System.IO.Compression.ZipArchive]::new($output, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    foreach ($file in $files) {
        # MelonLoader holds Latest.log open while playing. Take a bounded snapshot
        # with shared read access instead of asking Compress-Archive to lock it.
        $input = [System.IO.File]::Open($file, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
        $entryStream = $null
        try {
            $entryStream = $archive.CreateEntry([System.IO.Path]::GetFileName($file)).Open()
            $remaining = $input.Length
            $buffer = [byte[]]::new(65536)
            while ($remaining -gt 0) {
                $read = $input.Read($buffer, 0, [int][Math]::Min($remaining, $buffer.Length))
                if ($read -eq 0) { break }
                $entryStream.Write($buffer, 0, $read)
                $remaining -= $read
            }
        } finally {
            if ($entryStream) { $entryStream.Dispose() }
            $input.Dispose()
        }
    }
} finally {
    if ($archive) { $archive.Dispose() }
    $output.Dispose()
}
Write-Host "Support ZIP created: $zip"
Write-Host 'Review logs for personal details before sharing. Nothing was uploaded; no saves or preferences were collected.'
