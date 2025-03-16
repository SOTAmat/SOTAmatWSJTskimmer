#!/bin/bash

# Explicitly set .NET 6 SDK/Runtime to use
export PATH="/opt/homebrew/opt/dotnet@6/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@6/libexec"

# Verify .NET version being used
echo "Using .NET version:"
dotnet --version

# Publish for all platforms with explicit .NET 6 targeting
dotnet publish -c Release -r win-x64 -p:TargetFramework=net6.0 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o ./publish/windows-intel-64bit
dotnet publish -c Release -r linux-arm64 -p:TargetFramework=net6.0 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o ./publish/linux-arm-64bit
dotnet publish -c Release -r linux-arm -p:TargetFramework=net6.0 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o ./publish/linux-arm-32bit
dotnet publish -c Release -r linux-x64 -p:TargetFramework=net6.0 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o ./publish/linux-intel-64bit

# macOS builds - mimicking Windows-style builds that seem to work better
echo "Building macOS ARM64 version..."
# These settings mimic how Windows builds for macOS
dotnet publish -c Release -r osx-arm64 -p:TargetFramework=net6.0 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishReadyToRun=false /p:EnableCompressionInSingleFile=false /p:DebugType=None /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/mac-osx-arm-M1-64bit
# Remove any files that might interfere with proper execution
find ./publish/mac-osx-arm-M1-64bit -name "*.pdb" -delete

echo "Building macOS x64 version..."
dotnet publish -c Release -r osx-x64 -p:TargetFramework=net6.0 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishReadyToRun=false /p:EnableCompressionInSingleFile=false /p:DebugType=None /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/mac-osx-intel-64bit
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
