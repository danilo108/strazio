[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet',
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace 'src/Strazio.Cli/Strazio.Cli.csproj'
$output = Join-Path $workspace "artifacts/$Runtime"
if (-not $SkipTests) {
    & $Dotnet test (Join-Path $workspace 'Strazio.sln') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'I test non sono riusciti. Pubblicazione interrotta.' }
}
& $Dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Pubblicazione non riuscita.' }
$files = @(Get-ChildItem -LiteralPath $output -File -Recurse)
if ($files.Count -ne 1 -or $files[0].Name -ne 'strazio.exe') {
    throw "La cartella di distribuzione non contiene soltanto strazio.exe: $output"
}
Write-Host "Pronto: $output/strazio.exe"
Get-FileHash -LiteralPath (Join-Path $output 'strazio.exe') -Algorithm SHA256
