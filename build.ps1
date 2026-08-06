param(
    [string]$Configuration = "Release"
)

Write-Host "Building FootballEngine ($Configuration)..."
dotnet build FootballEngine.Client/FootballEngine.Client.fsproj -c $Configuration
Write-Host "Build complete."
