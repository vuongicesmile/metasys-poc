[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')][string]$Version)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $repo
try {
    $tag = "dev-v$Version"
    if ((git branch --show-current) -ne 'main') { throw 'Create releases from main.' }
    if (git status --porcelain) { throw 'Commit reviewed changes first; release requires a clean checkout.' }
    git fetch origin main --tags
    if ($LASTEXITCODE -ne 0) { throw 'Fetch failed.' }
    git merge-base --is-ancestor origin/main HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Local main must include origin/main; pull/reconcile before releasing.' }
    if (git tag --list $tag) { throw "Tag $tag already exists. Never move a release tag." }
    foreach ($part in $Version.Split('.')) { if ([long]$part -gt 65535) { throw 'Version segments must be <= 65535.' } }
    git tag -a $tag -m "Deploy FMCentralBms Developer $Version"
    if ($LASTEXITCODE -ne 0) { throw 'Tag creation failed.' }
    git push --atomic origin HEAD:refs/heads/main "refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw "Push failed; local tag $tag retained. Inspect the remote and retry this exact tag without moving it." }
    Write-Host "Published $tag. Follow https://github.com/vuongicesmile/metasys-poc/actions"
} finally { Pop-Location }
