[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$fixtureRoot = Join-Path $repo 'docs/examples/spo-import'

function Convert-SpoImportFixture([string]$Path) {
    $document = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($document.schemaVersion -ne 1) { throw 'schemaVersion must be 1.' }
    if ($null -eq $document.items -or @($document.items).Count -gt 100) { throw 'items must contain at most 100 rows.' }

    $seen = @{}
    $rows = @()
    $ordinal = 0
    foreach ($item in @($document.items)) {
        $ordinal++
        if ($null -eq $item.code -or $item.code -isnot [string]) { throw "item $ordinal code is required." }
        if ($null -eq $item.name -or $item.name -isnot [string]) { throw "item $ordinal name is required." }
        if ($null -eq $item.floorCount -or $item.floorCount -isnot [int] -and $item.floorCount -isnot [long]) {
            throw "item $ordinal floorCount must be an integer."
        }
        $code = $item.code.Trim().ToUpperInvariant()
        $name = $item.name.Trim()
        if ($code.Length -lt 1 -or $code.Length -gt 50) { throw "item $ordinal code length is invalid." }
        if ($name.Length -lt 1 -or $name.Length -gt 255) { throw "item $ordinal name length is invalid." }
        if ($item.floorCount -lt 0 -or $item.floorCount -gt 200) { throw "item $ordinal floorCount is outside 0..200." }
        if ($seen.ContainsKey($code)) { throw "duplicate normalized code: $code." }
        $seen[$code] = $true
        $rows += [pscustomobject]@{ ordinal = $ordinal; code = $code; name = $name; floorCount = [int]$item.floorCount }
    }
    return $rows
}

$valid = @(Convert-SpoImportFixture (Join-Path $fixtureRoot 'buildings-v1.json'))
if ($valid.Count -ne 2 -or $valid[0].code -ne 'BLD001' -or $valid[1].floorCount -ne 3) {
    throw 'Valid fixture mapping differs from the SPO import contract.'
}

$invalid = Get-ChildItem -LiteralPath $fixtureRoot -Filter 'invalid-*.json'
foreach ($fixture in $invalid) {
    $failed = $false
    try { $null = Convert-SpoImportFixture $fixture.FullName }
    catch { $failed = $true }
    if (-not $failed) { throw "Invalid fixture unexpectedly passed: $($fixture.Name)" }
}

[pscustomobject]@{
    ValidFixtures = 1
    InvalidFixtures = $invalid.Count
    ValidRows = $valid.Count
    Result = 'PASS'
} | Format-List
