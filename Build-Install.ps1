param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot
try {
    dotnet build .\SpireGPS.csproj -c $Configuration /p:InstallToGame=true
}
finally {
    Pop-Location
}
