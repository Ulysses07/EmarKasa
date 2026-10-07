# Pure release boundaries plus a hidden WinUI startup probe. No installer runs when this file is loaded.
function Assert-KasaPathWithin([string]$Path, [string]$Root) {
    $absolutePath = [System.IO.Path]::GetFullPath($Path)
    $absoluteRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $prefix = $absoluteRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $absolutePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Görev yolu izin verilen kökün dışında: $absolutePath"
    }
    return $absolutePath
}

function Assert-KasaNoReparseAncestors([string]$Path, [string]$Root) {
    $absolutePath = Assert-KasaPathWithin $Path $Root
    $absoluteRoot = [System.IO.Path]::GetFullPath($Root)
    $cursor = $absolutePath
    while ($cursor -and -not $cursor.Equals($absoluteRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw "Görev yolu bağlantı/junction içeriyor: $cursor"
            }
        }
        $cursor = Split-Path $cursor -Parent
    }
    return $absolutePath
}

function Get-KasaNextPatchVersion([string]$Version) {
    if ($Version -notmatch '^([0-9]+)\.([0-9]+)\.([0-9]+)$') { throw 'Üç parçalı sayısal sürüm gerekli.' }
    $patch = [int]$Matches[3]
    if ($patch -eq [int]::MaxValue) { throw 'Sentetik sürüm sınırı aşıldı.' }
    return "$($Matches[1]).$($Matches[2]).$($patch + 1)"
}

function Assert-KasaReleaseFeed([string]$Directory, [string]$Version) {
    $feedPath = Join-Path $Directory 'releases.win.json'
    $feed = Get-Content -LiteralPath $feedPath -Raw | ConvertFrom-Json
    $assets = @($feed.Assets)
    if ($assets.Count -ne 1) { throw 'Yeni feed yalnız bir full paket içermeli.' }
    $asset = $assets[0]
    if ($asset.PackageId -ne 'EmarKasa' -or $asset.Version -ne $Version -or $asset.Type -ne 'Full') {
        throw 'Feed kimliği, sürümü veya paket türü yayınla eşleşmiyor.'
    }
    if ($asset.FileName -ne [System.IO.Path]::GetFileName($asset.FileName) -or $asset.FileName -notlike '*-full.nupkg') {
        throw 'Feed dosya adı yerel full paket olmalı.'
    }
    $packagePath = Assert-KasaPathWithin (Join-Path $Directory $asset.FileName) $Directory
    $package = Get-Item -LiteralPath $packagePath
    if ($package.Length -ne [long]$asset.Size) { throw 'Full paket boyutu feed ile eşleşmiyor.' }
    $sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    if ($asset.SHA256 -ne $sha256) { throw 'Full paket SHA-256 feed ile eşleşmiyor.' }
    return $packagePath
}

function Assert-KasaInstalledVersion([string]$InstallRoot, [string]$Version) {
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $InstallRoot 'current/sq.version') -Raw
    $id = $manifest.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='id']")
    $installedVersion = $manifest.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='version']")
    if (-not $id -or -not $installedVersion -or $id.InnerText -ne 'EmarKasa' -or $installedVersion.InnerText -ne $Version) {
        throw 'Kurulu Velopack manifest kimliği/sürümü beklenen paketle eşleşmiyor.'
    }
}

function Assert-KasaUpdateReport([object]$Report, [string]$ExpectedVersion) {
    if ($Report.currentVersion -ne $ExpectedVersion -or $Report.installed -isnot [bool] -or $Report.updated -isnot [bool] -or -not $Report.installed -or -not $Report.updated) {
        throw 'Gerçek güncelleme/yeniden başlatma raporu beklenen kurulu sürümü doğrulamadı.'
    }
}

if (-not ('KasaWindowsWindowProbe' -as [type])) {
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
}

function Invoke-KasaStartupSmoke([string]$Exe, [string]$Label) {
    $process = $null
    try {
        $process = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe -Parent) -WindowStyle Hidden -PassThru
        $windowOpened = $false
        for ($attempt = 0; $attempt -lt 60 -and -not $windowOpened; $attempt++) {
            Start-Sleep -Seconds 1
            if ($process.HasExited) { throw "$Label açılışta kapandı (kod $($process.ExitCode))." }
            $windowOpened = [KasaWindowsWindowProbe]::FindMainWindow($process.Id) -ne [IntPtr]::Zero
        }
        if (-not $windowOpened) { throw "$Label 60 saniyede WinUI ana penceresini oluşturmadı." }
        Start-Sleep -Seconds 15
        if ($process.HasExited) { throw "$Label pencere oluşturduktan sonra kapandı." }
        $mainWindow = [KasaWindowsWindowProbe]::FindMainWindow($process.Id)
        if ($mainWindow -eq [IntPtr]::Zero -or [KasaWindowsWindowProbe]::IsHungAppWindow($mainWindow)) {
            throw "$Label ana pencereyi kaybetti veya pencere yanıt vermiyor."
        }
        Write-Output "$Label başlangıç kontrolü geçti."
    } finally {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
