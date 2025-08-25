#!/bin/bash

echo "Signing macOS builds..."

# Get the directory of the script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Source the environment file if it exists
ENV_FILE="$SCRIPT_DIR/sign-macos-builds.env"
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

# Build the application for both architectures
echo "Building application for ARM64..."
dotnet publish -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit"

echo "Building application for Intel..."
dotnet publish -c Release -r osx-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true /p:DebugType=None -o "$SCRIPT_DIR/publish/mac-osx-intel-64bit"

# Function to sign and prepare binary
prepare_binary() {
    local source_dir="$1"
    local binary_name="$2"
    
    echo "=== Processing $binary_name ==="
    echo "Source directory: $source_dir"
    
    # Check if binary exists
    if [ ! -f "$source_dir/SOTAmatSkimmer" ]; then
        echo "ERROR: Binary not found at $source_dir/SOTAmatSkimmer"
        return 1
    fi
    
    # Show binary details
    echo "Binary details:"
    ls -la "$source_dir/SOTAmatSkimmer"
    file "$source_dir/SOTAmatSkimmer"
    
    # Set executable permissions
    chmod +x "$source_dir/SOTAmatSkimmer"
    
    # Clean any resource forks or Finder metadata
    echo "Cleaning resource forks and metadata..."
    xattr -cr "$source_dir/SOTAmatSkimmer"
    
    # Check entitlements file
    echo "Checking entitlements file..."
    if [ -f "$SCRIPT_DIR/SOTAmatSkimmer.entitlements" ]; then
        echo "Entitlements file exists:"
        cat "$SCRIPT_DIR/SOTAmatSkimmer.entitlements"
    else
        echo "WARNING: Entitlements file not found at $SCRIPT_DIR/SOTAmatSkimmer.entitlements"
    fi
    
    # Check certificate availability
    echo "Checking certificate availability..."
    security find-identity -v -p codesigning | grep "$DEVELOPER_CERTIFICATE_ID" || {
        echo "ERROR: Certificate not found in keychain!"
        echo "Available certificates:"
        security find-identity -v -p codesigning
        return 1
    }
    
    # Check keychain access
    echo "Checking keychain access..."
    security list-keychains
    security default-keychain
    
    # Sign the binary
    echo "Signing binary at $source_dir/SOTAmatSkimmer..."
    echo "Using certificate: Developer ID Application: Brian Mathews (B8AYRC6H39)"
    
    # Try signing without hardened runtime first to isolate the issue
    echo "Attempting code signing without hardened runtime..."
    codesign -s "Developer ID Application: Brian Mathews (B8AYRC6H39)" \
        -f -v \
        --timestamp \
        --entitlements "$SCRIPT_DIR/SOTAmatSkimmer.entitlements" \
        "$source_dir/SOTAmatSkimmer" 2>&1
    
    local sign_result=$?
    if [ $sign_result -ne 0 ]; then
        echo "ERROR: Code signing failed with exit code $sign_result!"
        echo "This usually indicates:"
        echo "1. Certificate chain issues (missing intermediate certificates)"
        echo "2. Keychain access problems"
        echo "3. Certificate expiration or revocation"
        echo "4. Hardened runtime conflicts"
        return 1
    fi
    
    echo "Code signing successful! Now applying hardened runtime..."
    
    # Now try to add hardened runtime
    codesign -s "Developer ID Application: Brian Mathews (B8AYRC6H39)" \
        -f -v \
        --timestamp \
        -o runtime \
        --entitlements "$SCRIPT_DIR/SOTAmatSkimmer.entitlements" \
        "$source_dir/SOTAmatSkimmer" 2>&1
    
    local runtime_result=$?
    if [ $runtime_result -ne 0 ]; then
        echo "WARNING: Could not apply hardened runtime, but basic signing succeeded"
        echo "This may cause notarization issues"
    else
        echo "Hardened runtime applied successfully"
    fi
    
    # Verify the signature
    echo "Verifying signature..."
    codesign -v --deep --strict --verbose=2 "$source_dir/SOTAmatSkimmer"
    
    # Additional verification
    echo "Checking code signing details..."
    codesign -d --entitlements - "$source_dir/SOTAmatSkimmer" 2>/dev/null || echo "No entitlements found in binary"
    
    # Check hardened runtime
    echo "Checking hardened runtime..."
    codesign -d --entitlements - "$source_dir/SOTAmatSkimmer" 2>/dev/null | grep -i "runtime" || echo "No runtime info found"
    
    # Only rename if signing succeeded
    echo "Renaming binary to $binary_name..."
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
    
    echo "=== Completed processing $binary_name ==="
    echo ""
}

# Create distribution directory
mkdir -p "$SCRIPT_DIR/publish/distribution"

# Process ARM64 build
echo "Processing ARM64 build..."
if ! prepare_binary "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit" "sotamat-arm64"; then
    echo "ERROR: ARM64 build processing failed. Cannot continue with notarization."
    exit 1
fi

# Process Intel build
echo "Processing Intel build..."
if ! prepare_binary "$SCRIPT_DIR/publish/mac-osx-intel-64bit" "sotamat-intel"; then
    echo "ERROR: Intel build processing failed. Cannot continue with notarization."
    exit 1
fi

# Verify binaries exist before proceeding
if [ ! -f "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/sotamat-arm64" ] || [ ! -f "$SCRIPT_DIR/publish/mac-osx-intel-64bit/sotamat-intel" ]; then
    echo "ERROR: One or more signed binaries are missing. Cannot continue with notarization."
    echo "ARM64 binary exists: $([ -f "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/sotamat-arm64" ] && echo "YES" || echo "NO")"
    echo "Intel binary exists: $([ -f "$SCRIPT_DIR/publish/mac-osx-intel-64bit/sotamat-intel" ] && echo "YES" || echo "NO")"
    exit 1
fi

# Create ZIP archives for notarization
echo "Creating ZIP archives for notarization..."
ditto -c -k --keepParent "$SCRIPT_DIR/publish/mac-osx-arm-M1-64bit/sotamat-arm64" "$SCRIPT_DIR/publish/distribution/sotamat-arm64.zip"
ditto -c -k --keepParent "$SCRIPT_DIR/publish/mac-osx-intel-64bit/sotamat-intel" "$SCRIPT_DIR/publish/distribution/sotamat-intel.zip"

# Submit for notarization
echo "Submitting ARM64 build for notarization..."
ARM64_SUBMISSION=$(xcrun notarytool submit "$SCRIPT_DIR/publish/distribution/sotamat-arm64.zip" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait)

echo "ARM64 notarization result: $ARM64_SUBMISSION"

# Extract submission ID for logging
ARM64_ID=$(echo "$ARM64_SUBMISSION" | grep "id:" | head -1 | awk '{print $2}')
if [ -n "$ARM64_ID" ]; then
    echo "ARM64 submission ID: $ARM64_ID"
    echo "Retrieving detailed notarization log..."
    xcrun notarytool log "$ARM64_ID" \
        --apple-id "$APPLE_ID" \
        --password "$APPLE_ID_PASSWORD" \
        --team-id "$APPLE_TEAM_ID" || echo "Could not retrieve log for ARM64"
else
    echo "Could not extract ARM64 submission ID"
fi

echo "Submitting Intel build for notarization..."
INTEL_SUBMISSION=$(xcrun notarytool submit "$SCRIPT_DIR/publish/distribution/sotamat-intel.zip" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait)

echo "Intel notarization result: $INTEL_SUBMISSION"

# Extract submission ID for logging
INTEL_ID=$(echo "$INTEL_SUBMISSION" | grep "id:" | head -1 | awk '{print $2}')
if [ -n "$INTEL_ID" ]; then
    echo "Intel submission ID: $INTEL_ID"
    echo "Retrieving detailed notarization log..."
    xcrun notarytool log "$INTEL_ID" \
        --apple-id "$APPLE_ID" \
        --password "$APPLE_ID_PASSWORD" \
        --team-id "$APPLE_TEAM_ID" || echo "Could not retrieve log for Intel"
else
    echo "Could not extract Intel submission ID"
fi

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
