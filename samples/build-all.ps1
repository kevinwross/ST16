# build-all.ps1 — Phase 4: build every sample in one shot.
#
# Assembles each samples/NN-*/<name>.st16 with `st16 as` and compiles
# each samples/NN-*/<name>.c with `st16 cc`; the prebuilt .bin + .st16d
# land next to the source. Exits non-zero if any sample fails.
param(
    [switch]$NoBuild   # skip `dotnet build` of the app (assumes a fresh one)
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app  = Join-Path $root "src/ST16.App"

if (-not $NoBuild) {
    dotnet build $app --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
}

$failed = 0
Get-ChildItem $PSScriptRoot -Directory | Sort-Object Name | ForEach-Object {
    $dir = $_.FullName
    $st16 = Get-ChildItem $dir -Filter *.st16 | Select-Object -First 1
    if ($null -ne $st16) {
        dotnet run --project $app --no-build -- as $st16.FullName | Out-Null
        if ($LASTEXITCODE -ne 0) { $failed = 1; Write-Error "assembly failed: $($st16.Name)" }
    }
    $c = Get-ChildItem $dir -Filter *.c | Select-Object -First 1
    if ($null -ne $c) {
        dotnet run --project $app --no-build -- cc $c.FullName | Out-Null
        if ($LASTEXITCODE -ne 0) { $failed = 1; Write-Error "compile failed: $($c.Name)" }
    }
}

if ($failed) { throw "one or more samples failed to build" }
Write-Host "all samples built."
