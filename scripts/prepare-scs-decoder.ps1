param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$version = "1.3.7"
$expectedSha256 = "7d8521e482646f11ac986d97063dbe0cc8c7b98716a6fe02cf7b621c19a64e2d"
$url = "https://github.com/CoffeSiberian/DecryptTruck/releases/download/$version/decrypt_truck_windows.exe"

$repoRoot = Split-Path -Parent $PSScriptRoot
$licenseSource = Join-Path $repoRoot "third-party\decrypt-truck\LICENSE.txt"
$noticeSource = Join-Path $repoRoot "third-party\decrypt-truck\NOTICE.txt"

if (-not (Test-Path $licenseSource)) { throw "DecryptTruck license file is missing." }
if (-not (Test-Path $noticeSource)) { throw "DecryptTruck notice file is missing." }

$destinationFull = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destinationFull -Force | Out-Null

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nmc-decrypt-truck-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    $download = Join-Path $tempRoot "decrypt_truck_windows.exe"
    Invoke-WebRequest -Uri $url -OutFile $download -UseBasicParsing

    $actualSha256 = (Get-FileHash -Path $download -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        throw "DecryptTruck SHA-256 mismatch. Expected $expectedSha256 but received $actualSha256."
    }

    $sample = Join-Path $tempRoot "plain.sii"
    $decodedSample = Join-Path $tempRoot "decoded.sii"
    $sampleText = "SiiNunit" + [Environment]::NewLine + "{" + [Environment]::NewLine + " test_value: 1" + [Environment]::NewLine + "}" + [Environment]::NewLine
    [System.IO.File]::WriteAllText($sample, $sampleText, [System.Text.UTF8Encoding]::new($false))

    $process = Start-Process -FilePath $download -ArgumentList @(
        ('"' + $sample + '"'),
        ('"' + $decodedSample + '"')
    ) -Wait -PassThru -NoNewWindow

    if ($process.ExitCode -ne 0) {
        throw "DecryptTruck smoke test exited with code $($process.ExitCode)."
    }
    if (-not (Test-Path $decodedSample)) {
        throw "DecryptTruck smoke test produced no output file."
    }

    $decodedText = [System.IO.File]::ReadAllText($decodedSample)
    if (-not $decodedText.TrimStart().StartsWith("SiiNunit", [System.StringComparison]::Ordinal)) {
        throw "DecryptTruck smoke test output is not a textual SiiNunit document."
    }

    Copy-Item $download (Join-Path $destinationFull "decrypt_truck_windows.exe") -Force
    Copy-Item $licenseSource (Join-Path $destinationFull "LICENSE.txt") -Force
    Copy-Item $noticeSource (Join-Path $destinationFull "NOTICE.txt") -Force

    Write-Host "Prepared DecryptTruck $version."
    Write-Host "SHA-256: $actualSha256"
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
