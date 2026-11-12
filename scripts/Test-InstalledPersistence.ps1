param(
    [Parameter(Mandatory = $true)]
    [string]$InstalledExecutable
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedExe = (Resolve-Path -LiteralPath $InstalledExecutable).Path
$env:GESTIONMAGASIN_INSTALLED_EXE = $resolvedExe

Push-Location $repoRoot
try {
    & dotnet test 'GestionMagasin\Tests\GestionMagasin.Tests.csproj' -c Release -p:Platform=x64 `
        -p:NuGetAudit=false --no-restore `
        --filter 'FullyQualifiedName~InstalledPersistenceTests.PublishedExecutableCanReopenDatabaseWithoutLosingData'
    if ($LASTEXITCODE -ne 0) { throw "Le test de persistance T13 a échoué avec le code $LASTEXITCODE." }
}
finally {
    Remove-Item Env:\GESTIONMAGASIN_INSTALLED_EXE -ErrorAction SilentlyContinue
    Pop-Location
}
