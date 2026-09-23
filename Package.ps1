param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$OutputDirectory = "dist"
)

$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot
try {
    [xml]$project = Get-Content ".\SpireGPS.csproj"
    $version = $project.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = "0.1.0"
    }

    $assemblyName = $project.Project.PropertyGroup.AssemblyName | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = "BantersTweaks"
    }

    Write-Host "== $assemblyName: build ($Configuration) =="
    dotnet build ".\SpireGPS.csproj" -c $Configuration /p:InstallToGame=false
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }

    $dll = Join-Path $PSScriptRoot ".godot\mono\temp\bin\$Configuration\$assemblyName.dll"
    if (-not (Test-Path $dll)) {
        $dll = Join-Path $PSScriptRoot "bin\$Configuration\net9.0\$assemblyName.dll"
    }
    if (-not (Test-Path $dll)) {
        throw "Built DLL not found for $assemblyName."
    }

    $manifest = Join-Path $PSScriptRoot "$assemblyName.json"
    if (-not (Test-Path $manifest)) {
        throw "Manifest not found: $manifest"
    }

    $outRoot = Join-Path $PSScriptRoot $OutputDirectory
    $stageRoot = Join-Path $outRoot "_stage"
    $modRoot = Join-Path $stageRoot $assemblyName
    $zipPath = Join-Path $outRoot "$assemblyName-v$version.zip"

    if (Test-Path $stageRoot) {
        Remove-Item $stageRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $modRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

    Copy-Item $dll (Join-Path $modRoot "$assemblyName.dll")
    Copy-Item $manifest (Join-Path $modRoot "$assemblyName.json")

    if (Test-Path ".\THIRD_PARTY_NOTICES.md") {
        Copy-Item ".\THIRD_PARTY_NOTICES.md" $modRoot
    }

    if (Test-Path ".\Assets") {
        Copy-Item ".\Assets" (Join-Path $modRoot "Assets") -Recurse -Force
    }
    else {
        Write-Warning "Assets folder not found. Wishlist artwork will be missing from the package."
    }

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive -Path $modRoot -DestinationPath $zipPath -CompressionLevel Optimal

    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    $sizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 2)

    Write-Host ""
    Write-Host "== Package ready =="
    Write-Host $zipPath
    Write-Host "Size: $sizeMb MB"
    Write-Host "SHA256: $hash"
    Write-Host ""
    Write-Host "Tester install:"
    Write-Host "  Extract the BantersTweaks folder into the game's mods folder."

    Remove-Item $stageRoot -Recurse -Force
}
finally {
    Pop-Location
}
