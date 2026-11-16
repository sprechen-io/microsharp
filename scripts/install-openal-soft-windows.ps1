[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Destination
)

$ErrorActionPreference = 'Stop'
if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -ne 'X64') {
    throw "The pinned OpenAL Soft binary is Win64. This process is $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)."
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$manifestPath = Join-Path $root 'native/openal-soft-windows.json'
$manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
if ($manifest.packedInNuGet) {
    throw 'The Windows OpenAL binary must stay out of the NuGet package.'
}

$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }
$temp = Join-Path $tempRoot 'openal-soft-install'
New-Item -ItemType Directory -Force -Path $temp | Out-Null
$zip = Join-Path $temp 'openal-soft-bin.zip'
Invoke-WebRequest -Uri $manifest.source -OutFile $zip
$archiveHash = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
if ($archiveHash -ne $manifest.archiveSha256) {
    throw "OpenAL Soft archive hash $archiveHash does not match $($manifest.archiveSha256)"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    if ($null -eq $archive.GetEntry($manifest.licenseFile)) {
        throw "OpenAL Soft license file $($manifest.licenseFile) is missing from the archive."
    }

    $entry = $archive.GetEntry($manifest.dllEntry)
    if ($null -eq $entry) {
        throw "OpenAL Soft binary $($manifest.dllEntry) is missing from the archive."
    }

    $extracted = Join-Path $temp 'soft_oal.dll'
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $extracted, $true)
}
finally {
    $archive.Dispose()
}

$dllHash = (Get-FileHash -Algorithm SHA256 -Path $extracted).Hash.ToLowerInvariant()
if ($dllHash -ne $manifest.dllSha256) {
    throw "OpenAL Soft binary hash $dllHash does not match $($manifest.dllSha256)"
}

$fullDestination = [System.IO.Path]::GetFullPath($Destination)
if ($fullDestination -match 'artifacts[\\/]packages') {
    throw 'Refusing to place OpenAL Soft in the package output.'
}

New-Item -ItemType Directory -Force -Path $fullDestination | Out-Null
$target = Join-Path $fullDestination $manifest.loadAs
Copy-Item -Force $extracted $target
if ($env:GITHUB_PATH) {
    Add-Content -Path $env:GITHUB_PATH -Value $fullDestination
}

Write-Output "OpenAL Soft $($manifest.version) $dllHash installed as $target"
