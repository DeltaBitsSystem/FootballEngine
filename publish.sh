#!/usr/bin/env bash
set -e

VERSION="${1:-1.0.0}"
echo "Publishing FootballEngine v$VERSION..."

for RUNTIME in linux-x64 win-x64 osx-x64 osx-arm64; do
    echo "  Building for $RUNTIME..."
    dotnet publish FootballEngine.Client/FootballEngine.Client.fsproj \
        --configuration Release \
        --runtime $RUNTIME \
        --self-contained true \
        --output ./publish/FootballEngine-$RUNTIME \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:VersionPrefix=$VERSION
done

echo "All platforms published to ./publish/"
