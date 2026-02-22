#!/bin/bash

# Use .NET 10 SDK (Homebrew: brew install dotnet)
export PATH="/opt/homebrew/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"

# Verify .NET version being used
echo "Using .NET version:"
dotnet --version

# Publish for all platforms (project targets net10.0)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o ./publish/windows-intel-64bit
dotnet publish -c Release -r linux-arm64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o ./publish/linux-arm-64bit
dotnet publish -c Release -r linux-arm --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o ./publish/linux-arm-32bit
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o ./publish/linux-intel-64bit

# macOS builds - mimicking Windows-style builds that seem to work better
echo "Building macOS ARM64 version..."
dotnet publish -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishReadyToRun=false /p:EnableCompressionInSingleFile=false /p:DebugType=None /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/mac-osx-arm-M1-64bit
# Remove any files that might interfere with proper execution
find ./publish/mac-osx-arm-M1-64bit -name "*.pdb" -delete

echo "Building macOS x64 version..."
dotnet publish -c Release -r osx-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishReadyToRun=false /p:EnableCompressionInSingleFile=false /p:DebugType=None /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/mac-osx-intel-64bit
# Remove any files that might interfere with proper execution
find ./publish/mac-osx-intel-64bit -name "*.pdb" -delete

# Remove PDB files from non-macOS builds
rm ./publish/windows-intel-64bit/*.pdb
rm ./publish/linux-arm-64bit/*.pdb
rm ./publish/linux-arm-32bit/*.pdb
rm ./publish/linux-intel-64bit/*.pdb

# Set executable permissions for Unix platforms
chmod +x ./publish/linux-arm-64bit/SOTAmatSkimmer
chmod +x ./publish/linux-arm-32bit/SOTAmatSkimmer
chmod +x ./publish/linux-intel-64bit/SOTAmatSkimmer
chmod +x ./publish/mac-osx-arm-M1-64bit/SOTAmatSkimmer
chmod +x ./publish/mac-osx-intel-64bit/SOTAmatSkimmer

echo "All builds completed successfully"
