#!/bin/bash

echo "Signing macOS builds..."

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

# Sign the ARM64 (M1) build
echo "Signing ARM64 (M1) build..."
codesign --force --options runtime --timestamp --sign "$DEVELOPER_CERTIFICATE_ID" "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/SOTAmatSkimmer"

# Sign the Intel build
echo "Signing Intel build..."
codesign --force --options runtime --timestamp --sign "$DEVELOPER_CERTIFICATE_ID" "$SCRIPT_DIR/publish/mac-osx-intel-64bit/SOTAmatSkimmer"

# Create notarization requests
echo "Submitting ARM64 build for notarization..."
xcrun notarytool submit "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/SOTAmatSkimmer" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait

echo "Submitting Intel build for notarization..."
xcrun notarytool submit "$SCRIPT_DIR/publish/mac-osx-intel-64bit/SOTAmatSkimmer" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait

# Staple the notarization to the app
echo "Stapling notarization to ARM64 build..."
xcrun stapler staple "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/SOTAmatSkimmer"

echo "Stapling notarization to Intel build..."
xcrun stapler staple "$SCRIPT_DIR/publish/mac-osx-intel-64bit/SOTAmatSkimmer"

# Verify the signatures
echo "Verifying signatures..."
echo "Verifying ARM64 build..."
codesign -vvv --deep --strict "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/SOTAmatSkimmer"

echo "Verifying Intel build..."
codesign -vvv --deep --strict "$SCRIPT_DIR/publish/mac-osx-intel-64bit/SOTAmatSkimmer"

echo "Signing complete!"
