[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src\KuikuiGameAssistant\KuikuiGameAssistant.csproj"
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "publish\$Runtime"
$portableDir = Join-Path $artifacts "portable\KuikuiGameAssistant"
$installerScript = Join-Path $root "installer\KuikuiGameAssistant.iss"
$dotnet = Join-Path $root ".dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

if ($Version.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $Version = $Version.Substring(1)
}

$assemblyVersion = $Version.Split("+")[0].Split("-")[0]
$parsedVersion = $null
if (-not [System.Version]::TryParse($assemblyVersion, [ref]$parsedVersion)) {
    throw "Version '$Version' does not contain a valid numeric assembly version."
}

$versionParts = $assemblyVersion.Split(".")
$fileVersion = switch ($versionParts.Length) {
    2 { "$assemblyVersion.0.0" }
    3 { "$assemblyVersion.0" }
    default { $assemblyVersion }
}

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $portableDir -Recurse -Force -ErrorAction SilentlyContinue

& $dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $publishDir `
    /p:Version=$Version `
    /p:FileVersion=$fileVersion `
    /p:AssemblyVersion=$fileVersion `
    /p:InformationalVersion=$Version `
    /p:PublishSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $portableDir -Recurse -Force
Set-Content -Path (Join-Path $portableDir "portable.marker") -Value "portable" -Encoding ASCII

$portableZip = Join-Path $artifacts "KuikuiGameAssistant-$Version-$Runtime-portable.zip"
Remove-Item -LiteralPath $portableZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $portableZip -CompressionLevel Optimal
Write-Host "Portable package: $portableZip"

if ($SkipInstaller) {
    Write-Host "Installer packaging skipped."
    return
}

$iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $defaultIscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (Test-Path -LiteralPath $defaultIscc) {
        $iscc = [pscustomobject]@{ Source = $defaultIscc }
    }
}

if ($null -eq $iscc) {
    Write-Warning "Inno Setup was not found. Install it or pass -SkipInstaller to only create the portable zip."
    return
}

& $iscc.Source `
    "/DAppVersion=$Version" `
    "/DSourceDir=$publishDir" `
    "/DOutputDir=$artifacts" `
    $installerScript
