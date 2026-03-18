#!/bin/bash
set -e

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="dotnet"
if command -v godot4 > /dev/null 2>&1; then
    GODOT="godot4"
elif command -v godot > /dev/null 2>&1; then
    GODOT="godot"
else
    echo "Error: neither godot4 nor godot was found in PATH."
    exit 1
fi
BUILD_ROOT="$ROOT_DIR/build"
RELEASE_DIR="$BUILD_ROOT/RemoveMultiplayerPlayerLimit"
DLL_SOURCE="$ROOT_DIR/.godot/mono/temp/bin/Debug/RemoveMultiplayerPlayerLimit.dll"
PCK_SOURCE="$BUILD_ROOT/RemoveMultiplayerPlayerLimit.pck"
MANIFEST_PATH_BETA="$ROOT_DIR/RemoveMultiplayerPlayerLimit.json"

"$DOTNET" build "$ROOT_DIR/RemoveMultiplayerPlayerLimit.csproj" -c Debug
"$GODOT" --headless --path "$ROOT_DIR" --script "res://tools/build_pck.gd"

mkdir -p "$RELEASE_DIR"
rm -rf "$RELEASE_DIR"/*
rm -f "$BUILD_ROOT"/sts2-RMP-*.zip

cp "$DLL_SOURCE" "$RELEASE_DIR/RemoveMultiplayerPlayerLimit.dll"
cp "$PCK_SOURCE" "$RELEASE_DIR/RemoveMultiplayerPlayerLimit.pck"
if [ -f "$MANIFEST_PATH_BETA" ]; then
    cp "$MANIFEST_PATH_BETA" "$RELEASE_DIR/RemoveMultiplayerPlayerLimit.json"
fi

if ! command -v jq &> /dev/null; then
    echo "Error: jq is required to parse RemoveMultiplayerPlayerLimit.json. Please install it (e.g., sudo apt install jq)."
    exit 1
fi
VERSION=$(jq -r '.version // empty' "$MANIFEST_PATH_BETA")
MOD_FOLDER_NAME=$(jq -r 'if .pck_name and .pck_name != "" then .pck_name else .name end' "$MANIFEST_PATH_BETA")
if [ -z "$VERSION" ]; then
    echo "Error: RemoveMultiplayerPlayerLimit.json missing version field"
    exit 1
fi
if [ -z "$MOD_FOLDER_NAME" ]; then
    echo "Error: RemoveMultiplayerPlayerLimit.json missing name/pck_name field"
    exit 1
fi

ZIP_NAME="sts2-RMP-$VERSION.zip"
ZIP_PATH="$BUILD_ROOT/$ZIP_NAME"
ZIP_STAGE_ROOT="$BUILD_ROOT/_zip_stage"
ZIP_MOD_FOLDER="$ZIP_STAGE_ROOT/$MOD_FOLDER_NAME"

rm -f "$ZIP_PATH"
rm -rf "$ZIP_STAGE_ROOT"

mkdir -p "$ZIP_MOD_FOLDER"
cp -r "$RELEASE_DIR"/* "$ZIP_MOD_FOLDER/"

if ! command -v zip &> /dev/null; then
    echo "Error: zip is required. Please install it."
    exit 1
fi
pushd "$ZIP_STAGE_ROOT" > /dev/null
zip -qr "../$ZIP_NAME" "$MOD_FOLDER_NAME"
popd > /dev/null
rm -rf "$ZIP_STAGE_ROOT"
