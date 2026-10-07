# Real Windows release: one publish, ZIP + Velopack installer/feed, real install and apply/restart gate.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $env:RUNNER_TEMP) {
    throw 'Kurulum/güncelleme testi yalnız geçici GitHub-hosted Windows runner üzerinde çalıştırılabilir.'
}
. (Join-Path $PSScriptRoot 'windows-release-lib.ps1')
$taskRepo = [System.IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$taskTemp = (Get-Item -LiteralPath $env:RUNNER_TEMP -Force).FullName
if (-not (Test-Path -LiteralPath (Join-Path $taskRepo 'Kasa.App/Kasa.App.csproj'))) { throw 'Proje kökü doğrulanamadı.' }
if ($taskRepo.StartsWith($taskTemp + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Checkout ile geçici yayın dizini ayrı olmalı.'
}
[xml]$props = Get-Content -LiteralPath (Join-Path $taskRepo 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.KasaSurumu
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'Geçerli KasaSurumu bulunamadı.' }
$syntheticVersion = Get-KasaNextPatchVersion $version
$paths = @{}
foreach ($name in @('publish', 'artifact', 'velopack-release', 'update-feed', 'installed', 'vpk-tools')) {
    $paths[$name] = Assert-KasaNoReparseAncestors (Join-Path $taskTemp "kasa-windows-$name") $taskTemp
    if (Test-Path -LiteralPath $paths[$name]) { throw "Görev dizini zaten var; üzerine yazılmadı: $($paths[$name])" }
}
$publish = $paths['publish']
$artifact = $paths['artifact']
$release = $paths['velopack-release']
$feed = $paths['update-feed']
$installed = $paths['installed']
$toolPath = $paths['vpk-tools']
$reportPath = Assert-KasaNoReparseAncestors (Join-Path $taskTemp 'kasa-windows-update-report.json') $taskTemp
if (Test-Path -LiteralPath $reportPath) { throw 'Önceki güncelleme raporu var; kullanılmadı.' }
New-Item -ItemType Directory -Path $publish, $artifact, $release, $feed | Out-Null

dotnet publish (Join-Path $taskRepo 'Kasa.App/Kasa.App.csproj') -c Release -f net10.0-windows10.0.19041.0 `
    -r win-x64 -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None `
    -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false --output $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows publish başarısız.' }
$exe = Join-Path $publish 'Kasa.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Yayınlanan Kasa.App.exe bulunamadı.' }

dotnet tool install vpk --version 1.2.161 --tool-path $toolPath --allow-roll-forward
if ($LASTEXITCODE -ne 0) { throw 'Sabit Velopack CLI kurulamadı.' }
$vpk = Join-Path $toolPath 'vpk.exe'
$previousApi = $env:KASA_API_URL
$env:KASA_API_URL = 'http://127.0.0.1:9/'
$installedByThisRun = $false
try {
    Invoke-KasaStartupSmoke $exe 'Dağıtılan ZIP klasörü'
    $publishMetadata = [ordered]@{
        version = $version; runtime = 'win-x64'; source_commit = $env:GITHUB_SHA
        sdk = '10.0.401'; workload_set = '10.0.401.1'; velopack = '1.2.161'
        startup_smoke = 'passed'; api_requests = 'local-loopback-only-during-smoke'; signed = $false
    }
    $publishMetadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publish 'build.json') -Encoding utf8NoBOM
    $zip = Join-Path $artifact "EmarKasa-$version-win-x64.zip"
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $commonPack = @('--packId', 'EmarKasa', '--packDir', $publish, '--mainExe', 'Kasa.App.exe', '--packTitle', 'Emar Kasa',
        '--packAuthors', 'Emar', '--aumid', 'EmarKasa.Masaustu', '--channel', 'win', '--runtime', 'win-x64', '--noPortable', '--delta', 'None')
    # No skipVeloAppCheck: the entry executable must really bootstrap Velopack before WinUI.
    & $vpk --yes --skip-updates --legacyConsole pack @commonPack --packVersion $version --outputDir $release
    if ($LASTEXITCODE -ne 0) { throw 'Gerçek Velopack yayını paketlenemedi.' }
    $fullPackage = Assert-KasaReleaseFeed $release $version
    $setups = @(Get-ChildItem -LiteralPath $release -Filter '*-Setup.exe' -File)
    if ($setups.Count -ne 1) { throw 'Tek gerçek Setup.exe bulunamadı.' }
    foreach ($source in @($setups[0].FullName, $fullPackage, (Join-Path $release 'releases.win.json'))) {
        Copy-Item -LiteralPath $source -Destination $artifact
    }
    # The next patch only changes Velopack package metadata. Product/app assembly version is not bumped.
    # Its output is disjoint from the upload directory; neither synthetic feed nor package is distributed.
    & $vpk --yes --skip-updates --legacyConsole pack @commonPack --packVersion $syntheticVersion --outputDir $feed --noInst
    if ($LASTEXITCODE -ne 0) { throw 'Yerel güncelleme testi için sentetik paket oluşturulamadı.' }
    $null = Assert-KasaReleaseFeed $feed $syntheticVersion

    $installedByThisRun = $true
    $installer = Start-Process -FilePath $setups[0].FullName -ArgumentList @('--silent', '--installto', ('"' + $installed + '"')) `
        -WorkingDirectory $release -WindowStyle Hidden -PassThru
    try {
        if (-not $installer.WaitForExit(180000)) { throw 'Gerçek Setup kurulum testi zaman aşımına uğradı.' }
        if ($installer.ExitCode -ne 0) { throw "Gerçek Setup başarısız (kod $($installer.ExitCode))." }
    } finally {
        if (-not $installer.HasExited) { Stop-Process -Id $installer.Id -Force }
    }
    $installedExe = Assert-KasaNoReparseAncestors (Join-Path $installed 'current/Kasa.App.exe') $taskTemp
    if (-not (Test-Path -LiteralPath $installedExe)) { throw 'Setup kurulu exe üretmedi.' }
    Assert-KasaInstalledVersion $installed $version
    Invoke-KasaStartupSmoke $installedExe "Kurulu $version uygulaması"

    $updateProcess = Start-Process -FilePath $installedExe `
        -ArgumentList @('--guncelleme-duman-testi', ('"' + $feed + '"'), ('"' + $reportPath + '"'), $syntheticVersion) `
        -WorkingDirectory (Split-Path $installedExe -Parent) -WindowStyle Hidden -PassThru
    $updateReport = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(240)
    while (-not $updateReport -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 1
        if (Test-Path -LiteralPath $reportPath) {
            # The restarted executable writes this small file; wait for a complete JSON write.
            try { $updateReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json } catch { $updateReport = $null }
        }
    }
    if (-not $updateReport) { throw 'Gerçek download/apply/restart işlemi yeni process raporu üretmedi.' }
    Assert-KasaUpdateReport $updateReport $syntheticVersion
    Assert-KasaInstalledVersion $installed $syntheticVersion
    # Ensure the report-writing test process has finished before opening normal WinUI.
    if (-not $updateProcess.HasExited) { $null = $updateProcess.WaitForExit(10000) }
    Start-Sleep -Seconds 2
    Invoke-KasaStartupSmoke $installedExe "Güncellenen $syntheticVersion uygulaması"
    $releaseMetadata = $publishMetadata
    $releaseMetadata['installer_smoke'] = 'passed'
    $releaseMetadata['update_smoke'] = @{ from = $version; to = $syntheticVersion; installed = $true; updated = $true }
    $releaseMetadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $artifact 'build.json') -Encoding utf8NoBOM
    $hashes = Get-ChildItem -LiteralPath $artifact -File | Sort-Object Name | ForEach-Object {
        "$( (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() )  $($_.Name)"
    }
    $hashes | Set-Content -LiteralPath (Join-Path $artifact 'SHA256SUMS.txt') -Encoding utf8NoBOM
    if ($env:GITHUB_OUTPUT) { "version=$version" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8NoBOM }
    if ($env:GITHUB_STEP_SUMMARY) {
        @"
## Windows release validation
Production ${version}: complete self-contained ZIP, Velopack Setup, full nupkg and releases.win.json.
Actual Setup installed to this runner's temporary folder; installed WinUI startup passed.
Real local-feed download/apply/restart $version → $syntheticVersion passed; updated WinUI startup passed.
Synthetic packages remain outside the artifact. No public GitHub release or live API request was made.
Source: $env:GITHUB_SHA. Executables are unsigned.
"@ | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8NoBOM
    }
} finally {
    $env:KASA_API_URL = $previousApi
    if ($installedByThisRun) {
        # Only processes from our verified temporary install are eligible for termination.
        $safeInstall = Assert-KasaNoReparseAncestors $installed $taskTemp
        foreach ($ownedProcess in @(Get-Process -Name 'Kasa.App', 'Update' -ErrorAction SilentlyContinue)) {
            try { $processPath = $ownedProcess.Path } catch { continue }
            if ($processPath -and $processPath.StartsWith($safeInstall + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $ownedProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
        if (Test-Path -LiteralPath $safeInstall) {
            Remove-Item -LiteralPath $safeInstall -Recurse -Force
        }
    }
}
