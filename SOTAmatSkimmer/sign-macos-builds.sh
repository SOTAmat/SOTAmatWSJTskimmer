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

# Sign all files in the macOS ARM64 folder
echo "Signing all files in ARM64 (M1) folder..."
find "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit" -type f | while read file; do
    codesign --force --options runtime --timestamp --sign "$DEVELOPER_CERTIFICATE_ID" "$file"
done

# Sign all files in the macOS Intel folder
echo "Signing all files in Intel folder..."
find "$SCRIPT_DIR/publish/mac-osx-intel-64bit" -type f | while read file; do
    codesign --force --options runtime --timestamp --sign "$DEVELOPER_CERTIFICATE_ID" "$file"
done

# Set executable permissions
echo "Setting executable permissions..."
chmod +x "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/SOTAmatSkimmer"
chmod +x "$SCRIPT_DIR/publish/mac-osx-intel-64bit/SOTAmatSkimmer"

# Create distribution directory
mkdir -p "$SCRIPT_DIR/publish/distribution"

# Create simple launcher script for each platform
echo "Creating launcher scripts..."

# For ARM64
cat > "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/run-SOTAmatSkimmer.command" << EOL
#!/bin/bash
cd "\$(dirname "\$0")"
./SOTAmatSkimmer
EOL
chmod +x "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/run-SOTAmatSkimmer.command"
codesign --force --options runtime --timestamp --sign "$DEVELOPER_CERTIFICATE_ID" "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/run-SOTAmatSkimmer.command"

# For Intel
cat > "$SCRIPT_DIR/publish/mac-osx-intel-64bit/run-SOTAmatSkimmer.command" << EOL
#!/bin/bash
cd "\$(dirname "\$0")"
./SOTAmatSkimmer
EOL
chmod +x "$SCRIPT_DIR/publish/mac-osx-intel-64bit/run-SOTAmatSkimmer.command"
codesign --force --options runtime --timestamp --sign "$DEVELOPER_CERTIFICATE_ID" "$SCRIPT_DIR/publish/mac-osx-intel-64bit/run-SOTAmatSkimmer.command"

# Add README files to explain usage
cat > "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/README.txt" << EOL
SOTAmatSkimmer for macOS (ARM64/M1)

To run the application:
1. Double-click "run-SOTAmatSkimmer.command" to launch
   - OR -
2. Open Terminal and run:
   cd /path/to/extracted/folder
   ./SOTAmatSkimmer

Note: All files in this folder must be kept together.
EOL

cat > "$SCRIPT_DIR/publish/mac-osx-intel-64bit/README.txt" << EOL
SOTAmatSkimmer for macOS (Intel)

To run the application:
1. Double-click "run-SOTAmatSkimmer.command" to launch
   - OR -
2. Open Terminal and run:
   cd /path/to/extracted/folder
   ./SOTAmatSkimmer

Note: All files in this folder must be kept together.
EOL

# Create ZIP archives for notarization
echo "Creating ZIP archives for notarization..."
ditto -c -k --keepParent "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit" "$SCRIPT_DIR/publish/distribution/SOTAmatSkimmer-arm64.zip"
ditto -c -k --keepParent "$SCRIPT_DIR/publish/mac-osx-intel-64bit" "$SCRIPT_DIR/publish/distribution/SOTAmatSkimmer-intel.zip"

# Create notarization requests
echo "Submitting ARM64 build for notarization..."
xcrun notarytool submit "$SCRIPT_DIR/publish/distribution/SOTAmatSkimmer-arm64.zip" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait

echo "Submitting Intel build for notarization..."
xcrun notarytool submit "$SCRIPT_DIR/publish/distribution/SOTAmatSkimmer-intel.zip" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait

echo "Signing and notarization complete!"
echo "Distribution packages are available in: $SCRIPT_DIR/publish/distribution/"
echo ""
echo "Distribute these files to your users:"
echo "- For M1 Macs: $SCRIPT_DIR/publish/distribution/SOTAmatSkimmer-arm64.zip"
echo "- For Intel Macs: $SCRIPT_DIR/publish/distribution/SOTAmatSkimmer-intel.zip"
