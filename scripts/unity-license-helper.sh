#!/bin/bash
set -euo pipefail

# Unity License Helper Script
# Helps with manual license activation for personal Unity accounts

UNITY_VERSION="2023.2.20f1"
UNITY_PATH="/opt/unity/editors/2023.2.20f1/Editor/Unity"

echo "Unity License Helper"
echo "===================="
echo "Unity Version: $UNITY_VERSION"
echo "Unity Path: $UNITY_PATH"
echo ""

# Check if Unity exists
if [[ ! -f "$UNITY_PATH" ]]; then
    echo "ERROR: Unity not found at $UNITY_PATH"
    exit 1
fi

echo "For personal Unity accounts, you need to:"
echo "1. Generate a license request file"
echo "2. Manually activate it on Unity's website"
echo "3. Download the license file"
echo ""

read -p "Do you want to generate a license request file? (y/n): " -r
if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo "Generating license request..."
    
    # Create license request
    $UNITY_PATH \
        -batchmode \
        -quit \
        -createManualActivationFile \
        -logFile /tmp/unity-license-request.log
    
    # Find the generated .alf file
    ALF_FILE=$(find . -name "*.alf" -type f -printf '%T@ %p\n' | sort -n | tail -1 | cut -d' ' -f2-)
    
    if [[ -n "$ALF_FILE" && -f "$ALF_FILE" ]]; then
        echo "License request file generated: $ALF_FILE"
        echo ""
        echo "Next steps:"
        echo "1. Go to: https://license.unity3d.com/manual"
        echo "2. Upload the file: $ALF_FILE"
        echo "3. Download the Unity_v2022.x.ulf file"
        echo "4. Save it as unity-license.ulf in this directory"
        echo ""
    else
        echo "ERROR: Could not find generated license file"
        exit 1
    fi
fi

read -p "Do you have a unity-license.ulf file to test? (y/n): " -r
if [[ $REPLY =~ ^[Yy]$ ]]; then
    if [[ -f "unity-license.ulf" ]]; then
        echo "Testing license file..."
        
        # Test the license
        export UNITY_LICENSE_FILE="$(pwd)/unity-license.ulf"
        
        $UNITY_PATH \
            -batchmode \
            -quit \
            -manualLicenseFile "$UNITY_LICENSE_FILE" \
            -logFile /tmp/unity-license-test.log
        
        echo "License test completed. Check /tmp/unity-license-test.log for details."
    else
        echo "ERROR: unity-license.ulf not found in current directory"
        exit 1
    fi
fi

echo "License helper complete!"