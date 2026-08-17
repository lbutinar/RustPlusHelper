[CmdletBinding()]
param(
    [string]$OutputPath = '.\artifacts\ui\phase1-smoke.png',
    [ValidateSet('Map', 'Servers')]
    [string]$Section = 'Map',
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $repositoryRoot 'src\RustPlusHelper.Desktop\bin\Debug\net10.0-windows10.0.19041.0\RustPlusHelper.exe'
if (-not (Test-Path $executable)) {
    throw "Desktop executable not found. Build the solution first: $executable"
}

$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}

$startedAt = [DateTime]::UtcNow
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $executable
$startInfo.UseShellExecute = $false
$startInfo.Environment['RUSTPLUSHELPER_UI_CAPTURE_PATH'] = $resolvedOutput
$startInfo.Environment['RUSTPLUSHELPER_UI_CAPTURE_SECTION'] = $Section

$process = [System.Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
    throw 'Windows did not start RustPlusHelper.'
}

try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "RustPlusHelper did not finish its UI capture within $TimeoutSeconds seconds."
    }

    if ($process.ExitCode -ne 0) {
        throw "RustPlusHelper UI capture exited with code $($process.ExitCode)."
    }

    if (-not (Test-Path $resolvedOutput)) {
        throw "RustPlusHelper exited without writing the UI capture: $resolvedOutput"
    }

    $capture = Get-Item $resolvedOutput
    if ($capture.LastWriteTimeUtc -lt $startedAt -or $capture.Length -eq 0) {
        throw "RustPlusHelper did not produce a fresh, non-empty UI capture: $resolvedOutput"
    }

    Write-Output "Captured RustPlusHelper WebView content: $resolvedOutput"
    Write-Output "Image bytes: $($capture.Length)"
} finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id
    }

    $process.Dispose()
}
