#!/usr/bin/env bash
set -e

CONFIGURATION="${1:-Release}"
echo "Building FootballEngine ($CONFIGURATION)..."

dotnet build FootballEngine.Client/FootballEngine.Client.fsproj -c $CONFIGURATION

echo "Build complete."
