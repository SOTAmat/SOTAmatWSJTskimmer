#!/bin/bash

echo "Signing macOS builds..."

# Source the environment file if it exists
ENV_FILE="$(dirname "${BASH_SOURCE[0]}")/sign-macos-builds.env"
if [ -f "$ENV_FILE" ]; then
    echo "Sourcing environment variables from $ENV_FILE"
    source "$ENV_FILE"
else
    echo "Warning: Environment file $ENV_FILE not found"
    echo "Will rely on environment variables already set"
fi

# Set .NET 6 environment
export PATH="/opt/homebrew/opt/dotnet@6/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@6/libexec"

# Check for required environment variables
if [ -z "$DEVELOPER_CERTIFICATE_ID" ]; then
    echo "Error: DEVELOPER_CERTIFICATE_ID environment variable is not set"
    echo "Please set it with: export DEVELOPER_CERTIFICATE_ID='Developer ID Application: Your Name (XXXXXXXXXX)'"
    exit 1
fi

if [ -z "$APPLE_ID" ]; then
    echo "Error: APPLE_ID environment variable is not set"
    echo "Please set it with: export APPLE_ID='your.apple.id@example.com'"
    exit 1
fi

if [ -z "$APPLE_ID_PASSWORD" ]; then
    echo "Error: APPLE_ID_PASSWORD environment variable is not set"
    echo "Please set it with: export APPLE_ID_PASSWORD='your-app-specific-password'"
    exit 1
fi

if [ -z "$APPLE_TEAM_ID" ]; then
    echo "Error: APPLE_TEAM_ID environment variable is not set"
    echo "Please set it with: export APPLE_TEAM_ID='your-team-id'"
    exit 1
fi

# Get the directory of the script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Build the application for both architectures
echo "Building application for ARM64..."
dotnet publish -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit"

echo "Building application for Intel..."
dotnet publish -c Release -r osx-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o "$SCRIPT_DIR/publish/mac-osx-intel-64bit"

# Function to sign and prepare binary
prepare_binary() {
    local source_dir="$1"
    local binary_name="$2"
    
    # Set executable permissions
    chmod +x "$source_dir/SOTAmatSkimmer"
    
    # Clean any resource forks or Finder metadata
    xattr -cr "$source_dir/SOTAmatSkimmer"
    
    # Sign the binary
    echo "Signing binary at $source_dir/SOTAmatSkimmer..."
    codesign -s "$DEVELOPER_CERTIFICATE_ID" \
        -f -v \
        --timestamp \
        -o runtime \
        --entitlements "$SCRIPT_DIR/SOTAmatSkimmer.entitlements" \
        "$source_dir/SOTAmatSkimmer"
    
    # Verify the signature
    echo "Verifying signature..."
    codesign -v --deep --strict --verbose=2 "$source_dir/SOTAmatSkimmer"
    
    # Rename the binary to include architecture
    mv "$source_dir/SOTAmatSkimmer" "$source_dir/$binary_name"
    
    # Create simple README
    cat > "$source_dir/README.txt" << EOL
SOTAmatSkimmer for macOS

Installation:
1. Copy $binary_name to any directory (e.g., ~/bin or /usr/local/bin)
2. Make sure the directory is in your PATH
3. Run the program by typing: $binary_name [options]

For help with command-line options:
   $binary_name --help

Note: You may need to run 'chmod +x $binary_name' after copying to a new location.
EOL
}

# Create distribution directory
mkdir -p "$SCRIPT_DIR/publish/distribution"

# Process ARM64 build
echo "Processing ARM64 build..."
prepare_binary "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit" "sotamat-arm64"

# Process Intel build
echo "Processing Intel build..."
prepare_binary "$SCRIPT_DIR/publish/mac-osx-intel-64bit" "sotamat-intel"

# Create ZIP archives for notarization
echo "Creating ZIP archives for notarization..."
ditto -c -k --keepParent "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/sotamat-arm64" "$SCRIPT_DIR/publish/distribution/sotamat-arm64.zip"
ditto -c -k --keepParent "$SCRIPT_DIR/publish/mac-osx-intel-64bit/sotamat-intel" "$SCRIPT_DIR/publish/distribution/sotamat-intel.zip"

# Submit for notarization
echo "Submitting ARM64 build for notarization..."
xcrun notarytool submit "$SCRIPT_DIR/publish/distribution/sotamat-arm64.zip" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait

echo "Submitting Intel build for notarization..."
xcrun notarytool submit "$SCRIPT_DIR/publish/distribution/sotamat-intel.zip" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait

# Create final distribution packages with binaries and READMEs
echo "Creating final distribution packages..."
cd "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit" && zip -r "$SCRIPT_DIR/publish/distribution/sotamat-arm64-final.zip" sotamat-arm64 README.txt
cd "$SCRIPT_DIR/publish/mac-osx-intel-64bit" && zip -r "$SCRIPT_DIR/publish/distribution/sotamat-intel-final.zip" sotamat-intel README.txt

echo "Signing and notarization complete!"
echo "Distribution packages are available in: $SCRIPT_DIR/publish/distribution/"
echo ""
echo "Distribute these files to your users:"
echo "- For M1 Macs: $SCRIPT_DIR/publish/distribution/sotamat-arm64-final.zip"
echo "- For Intel Macs: $SCRIPT_DIR/publish/distribution/sotamat-intel-final.zip"
echo ""
echo "Users can simply:"
echo "1. Unzip the file"
echo "2. Copy the binary to any directory in their PATH"
echo "3. Run 'sotamat-arm64' or 'sotamat-intel' from any location"
