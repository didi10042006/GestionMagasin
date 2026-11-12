param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'GestionMagasin\GestionMagasin.csproj'
$publishPath = Join-Path $repoRoot 'artifacts\publish\win-x64'
$installerPath = Join-Path $repoRoot 'artifacts\installer'
$scriptPath = Join-Path $repoRoot 'Gestion-Magasin.iss'
$compiler = Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Inno Setup Compiler introuvable : $compiler"
}
if (-not (Test-Path -LiteralPath $projectPath)) { throw "Projet introuvable : $projectPath" }

New-Item -ItemType Directory -Force -Path $installerPath | Out-Null
if (Test-Path -LiteralPath $publishPath) {
    $resolvedPublishPath = (Resolve-Path -LiteralPath $publishPath).Path
    if (-not $resolvedPublishPath.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refus de nettoyer une sortie en dehors du dépôt : $resolvedPublishPath"
    }
    Remove-Item -LiteralPath $resolvedPublishPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishPath | Out-Null

Push-Location $repoRoot
try {
    & dotnet restore $projectPath -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:NuGetAudit=false --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "La restauration x64 a échoué avec le code $LASTEXITCODE." }

    & dotnet publish $projectPath -c $Configuration -r win-x64 --self-contained true `
        -p:Platform=x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=1.0.0 -p:NuGetAudit=false --no-restore -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué avec le code $LASTEXITCODE." }

    $exePath = Join-Path $publishPath 'GestionMagasin.exe'
    if (-not (Test-Path -LiteralPath $exePath)) { throw "Exécutable publié introuvable : $exePath" }

    & $compiler $scriptPath
    if ($LASTEXITCODE -ne 0) { throw "ISCC a échoué avec le code $LASTEXITCODE." }

    $setupPath = Join-Path $installerPath 'Gestion-Magasin-Setup.exe'
    if (-not (Test-Path -LiteralPath $setupPath)) { throw "Installateur non généré : $setupPath" }
    Write-Output "Exécutable autonome : $exePath"
    Write-Output "Installateur : $setupPath"
}
finally {
    Pop-Location
}
