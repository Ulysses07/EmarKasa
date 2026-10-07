# Publishes the complete portable Windows folder, smoke-tests THAT output, then zips it.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRepo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $env:RUNNER_TEMP) { throw 'Bu betik GitHub Windows runner üzerinde çalıştırılmalı.' }
[xml]$props = Get-Content -LiteralPath (Join-Path $taskRepo 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.KasaSurumu
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'Geçerli KasaSurumu bulunamadı.' }
$publish = Join-Path $env:RUNNER_TEMP 'kasa-windows-publish'
$artifact = Join-Path $env:RUNNER_TEMP 'kasa-windows-artifact'
if ((Test-Path -LiteralPath $publish) -or (Test-Path -LiteralPath $artifact)) {
    throw 'Çıktı dizini zaten var; eski paket üzerine yazılmadı.'
}
New-Item -ItemType Directory -Path $publish, $artifact | Out-Null
dotnet publish (Join-Path $taskRepo 'Kasa.App/Kasa.App.csproj') -c Release -f net10.0-windows10.0.19041.0 `
    -r win-x64 -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None `
    -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false --output $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows publish başarısız.' }
$exe = Join-Path $publish 'Kasa.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Yayınlanan Kasa.App.exe bulunamadı.' }

# Process.MainWindowHandle ignores hidden windows. Enumerate this process's actual unowned WinUI window.
# The window is intentionally launched hidden by automation; visibility is not an acceptance condition.
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class KasaWindowsWindowProbe
{
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);
    [DllImport("user32.dll")]
    public static extern bool IsHungAppWindow(IntPtr window);
    public static IntPtr FindMainWindow(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, parameter) =>
        {
            uint ownerProcess;
            GetWindowThreadProcessId(window, out ownerProcess);
            if (ownerProcess != (uint)processId || GetWindow(window, 4) != IntPtr.Zero)
                return true;
            var windowClass = new StringBuilder(256);
            GetClassName(window, windowClass, windowClass.Capacity);
            if (windowClass.ToString() != "WinUIDesktopWin32WindowClass")
                return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
"@
$previousApi = $env:KASA_API_URL
$env:KASA_API_URL = 'http://127.0.0.1:9/'
$process = $null
try {
    $process = Start-Process -FilePath $exe -WorkingDirectory $publish -WindowStyle Hidden -PassThru
    $windowOpened = $false
    for ($attempt = 0; $attempt -lt 60 -and -not $windowOpened; $attempt++) {
        Start-Sleep -Seconds 1
        if ($process.HasExited) { throw "Yayın paketi açılışta kapandı (kod $($process.ExitCode))." }
        $process.Refresh()
        $windowOpened = [KasaWindowsWindowProbe]::FindMainWindow($process.Id) -ne [IntPtr]::Zero
    }
    if (-not $windowOpened) { throw 'Yayın paketi 60 saniyede pencere oluşturmadı.' }
    Start-Sleep -Seconds 15
    if ($process.HasExited) { throw 'Yayın paketi pencere oluşturduktan sonra kapandı.' }
    $mainWindow = [KasaWindowsWindowProbe]::FindMainWindow($process.Id)
    if ($mainWindow -eq [IntPtr]::Zero -or [KasaWindowsWindowProbe]::IsHungAppWindow($mainWindow)) {
        throw 'Yayın paketi ana WinUI penceresini kaybetti veya pencere yanıt vermiyor.'
    }
} finally {
    $env:KASA_API_URL = $previousApi
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
$metadata = [ordered]@{
    version = $version
    runtime = 'win-x64'
    source_commit = $env:GITHUB_SHA
    sdk = '10.0.401'
    workload_set = '10.0.401.1'
    startup_smoke = 'passed'
    api_requests = 'local-loopback-only-during-smoke'
    signed = $false
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publish 'build.json') -Encoding utf8NoBOM
$zip = Join-Path $artifact "EmarKasa-$version-win-x64.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath (Join-Path $artifact 'SHA256SUMS.txt') -Encoding utf8NoBOM
if ($env:GITHUB_OUTPUT) { "version=$version" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8NoBOM }
if ($env:GITHUB_STEP_SUMMARY) {
    @"
## Windows package
Self-contained Windows x64 ZIP built; startup smoke passed against the same published output.
SHA-256: $hash
Source: $env:GITHUB_SHA
The executable is unsigned. Extract the whole ZIP before starting Kasa.App.exe.
"@ | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8NoBOM
}
