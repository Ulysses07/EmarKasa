# Boundary tests only. The real installer and apply/restart gate run separately on the hosted runner.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'windows-release-lib.ps1')
$checks = 0
function Assert-Equal($Actual, $Expected, [string]$Label) {
    if ($Actual -ne $Expected) { throw "$Label failed: expected '$Expected', received '$Actual'." }
    $script:checks++
}
function Assert-Rejected([scriptblock]$Action, [string]$Label) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "$Label accepted an invalid release boundary." }
    $script:checks++
}
$fixtureRoot = Assert-KasaNoReparseAncestors (Join-Path $env:TEMP ('kasa-release-tests-' + [Guid]::NewGuid().ToString('N'))) $env:TEMP
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    $child = Join-Path $fixtureRoot 'child'
    Assert-Equal (Assert-KasaPathWithin $child ($fixtureRoot + '\')) $child 'Normal child path'
    Assert-Rejected { Assert-KasaPathWithin $fixtureRoot $fixtureRoot } 'Root itself'
    Assert-Rejected { Assert-KasaPathWithin ($fixtureRoot + '-sibling/file') $fixtureRoot } 'Sibling prefix'
    Assert-Rejected { Assert-KasaPathWithin (Join-Path $fixtureRoot '../outside') $fixtureRoot } 'Parent traversal'
    Assert-Equal (Get-KasaNextPatchVersion '2.4.1') '2.4.2' 'Synthetic next patch'
    Assert-Rejected { Get-KasaNextPatchVersion '2.4.1-test' } 'Nonproduction version'
    Assert-Rejected { Get-KasaNextPatchVersion '2.4.2147483647' } 'Patch overflow'
    # Validate the actual shipping argv without executing publish, pack or an installer.
    # Velopack 1.2.161 PackOptionsValidator forbids noPortable + noInst together.
    $releaseScriptPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'windows-release.ps1'
    $releaseAst = [System.Management.Automation.Language.Parser]::ParseFile($releaseScriptPath, [ref]$null, [ref]$null)
    $commonAssignments = @($releaseAst.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'commonPack'
    }, $true))
    Assert-Equal $commonAssignments.Count 1 'Shared pack argv found'
    $commonFlags = @($commonAssignments[0].Right.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.StringConstantExpressionAst]
    }, $true) | ForEach-Object { $_.Value })
    $packCommands = @($releaseAst.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.CommandElements[0] -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.CommandElements[0].VariablePath.UserPath -eq 'vpk'
    }, $true))
    Assert-Equal $packCommands.Count 2 'Production and synthetic pack calls found'
    foreach ($packCommand in $packCommands) {
        $variables = @($packCommand.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.VariableExpressionAst] } | ForEach-Object { $_.VariablePath.UserPath })
        $flags = @($commonFlags) + @($packCommand.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.StringConstantExpressionAst] } | ForEach-Object { $_.Value })
        Assert-Equal (($flags -contains '--noPortable') -and ($flags -contains '--noInst')) $false 'Velopack mutually exclusive pack flags'
        if ($variables -contains 'release') {
            Assert-Equal ($flags -contains '--noPortable') $true 'Production skips portable output'
        } elseif ($variables -contains 'feed') {
            Assert-Equal ($flags -contains '--noInst') $true 'Synthetic skips installer output'
        } else { throw 'Unexpected pack output; production/synthetic boundary cannot be verified.' }
    }
    $junctionTarget = Join-Path $fixtureRoot 'junction-target'
    $junction = Join-Path $fixtureRoot 'junction'
    New-Item -ItemType Directory -Path $junctionTarget | Out-Null
    New-Item -ItemType Junction -Path $junction -Target $junctionTarget | Out-Null
    try { Assert-Rejected { Assert-KasaNoReparseAncestors (Join-Path $junction 'file') $fixtureRoot } 'Reparse ancestor' }
    finally { Remove-Item -LiteralPath $junction -Force }
    $feedDirectory = Join-Path $fixtureRoot 'feed'
    New-Item -ItemType Directory -Path $feedDirectory | Out-Null
    $packageName = 'EmarKasa-2.4.1-full.nupkg'
    $packagePath = Join-Path $feedDirectory $packageName
    [System.IO.File]::WriteAllBytes($packagePath, [byte[]]@(1,2,3,4))
    $asset = [ordered]@{
        PackageId = 'EmarKasa'; Version = '2.4.1'; Type = 'Full'; FileName = $packageName
        SHA256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
        Size = (Get-Item -LiteralPath $packagePath).Length
    }
    function Write-FixtureFeed { @{ Assets = @($asset) } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $feedDirectory 'releases.win.json') -Encoding utf8NoBOM }
    Write-FixtureFeed
    Assert-Equal (Assert-KasaReleaseFeed $feedDirectory '2.4.1') $packagePath 'Matching full feed'
    Assert-Rejected { Assert-KasaReleaseFeed $feedDirectory '2.4.2' } 'Synthetic version in production feed'
    $originalHash = $asset.SHA256
    $asset.SHA256 = 'wrong-hash'; Write-FixtureFeed
    Assert-Rejected { Assert-KasaReleaseFeed $feedDirectory '2.4.1' } 'Package checksum mismatch'
    $asset.SHA256 = $originalHash; $asset.Size = 99; Write-FixtureFeed
    Assert-Rejected { Assert-KasaReleaseFeed $feedDirectory '2.4.1' } 'Package size mismatch'
    $asset.Size = 4; $asset.Type = 'Delta'; Write-FixtureFeed
    Assert-Rejected { Assert-KasaReleaseFeed $feedDirectory '2.4.1' } 'Delta instead of full package'
    $asset.Type = 'Full'; $asset.FileName = '../EmarKasa-2.4.1-full.nupkg'; Write-FixtureFeed
    Assert-Rejected { Assert-KasaReleaseFeed $feedDirectory '2.4.1' } 'Feed path traversal'
    $currentDirectory = Join-Path $fixtureRoot 'installed/current'
    New-Item -ItemType Directory -Path $currentDirectory -Force | Out-Null
    '<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"><metadata><id>EmarKasa</id><version>2.4.1</version></metadata></package>' | Set-Content -LiteralPath (Join-Path $currentDirectory 'sq.version')
    Assert-KasaInstalledVersion (Split-Path $currentDirectory -Parent) '2.4.1'; $checks++
    Assert-Rejected { Assert-KasaInstalledVersion (Split-Path $currentDirectory -Parent) '2.4.2' } 'Installed manifest version mismatch'
    $validReport = '{"currentVersion":"2.4.2","installed":true,"updated":true}' | ConvertFrom-Json
    Assert-KasaUpdateReport $validReport '2.4.2'; $checks++
    Assert-Rejected { Assert-KasaUpdateReport $validReport '2.4.1' } 'Wrong restart report version'
    Assert-Rejected { Assert-KasaUpdateReport ('{"currentVersion":"2.4.2","installed":true,"updated":false}' | ConvertFrom-Json) '2.4.2' } 'Failed update report'
    Assert-Rejected { Assert-KasaUpdateReport ('{"currentVersion":"2.4.2","installed":"true","updated":"true"}' | ConvertFrom-Json) '2.4.2' } 'Nonboolean update report'
    Assert-Rejected { Assert-KasaUpdateReport ('{"currentVersion":"2.4.2","installed":true}' | ConvertFrom-Json) '2.4.2' } 'Incomplete update report'
    Assert-Equal ([KasaWindowsWindowProbe]::FindMainWindow([int]::MaxValue).ToInt64()) 0 'Unrelated HWND rejected'
    Write-Output "$checks Windows release boundary checks passed. No installer or app was started."
} finally {
    $safeFixture = Assert-KasaNoReparseAncestors $fixtureRoot $env:TEMP
    Remove-Item -LiteralPath $safeFixture -Recurse -Force
}
