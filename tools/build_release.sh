#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
EXPLICIT_STS2_ASSEMBLY="${2:-}"
EXPLICIT_STEAMWORKS_ASSEMBLY="${3:-}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET_PATH:-dotnet}"
BUILD_ROOT="$ROOT_DIR/build"
PACK_PROJECT="$BUILD_ROOT/_pack_project"
RELEASE_DIR="$BUILD_ROOT/RemoveMultiplayerPlayerLimit"
MANIFEST="$ROOT_DIR/RemoveMultiplayerPlayerLimit.json"
CSPROJ="$ROOT_DIR/RemoveMultiplayerPlayerLimit.csproj"
DLL_SOURCE="$ROOT_DIR/.godot/mono/temp/bin/$CONFIGURATION/RemoveMultiplayerPlayerLimit.dll"
TEMP_PCK_PATH="$PACK_PROJECT/build/RemoveMultiplayerPlayerLimit.pck"
FINAL_PCK_PATH="$BUILD_ROOT/RemoveMultiplayerPlayerLimit.pck"

fail() {
    echo "  [FAIL] $1" >&2
    exit 1
}

wait_for_path() {
    local path="$1"
    local retries="${2:-50}"
    local delay="${3:-0.2}"

    for ((i = 0; i < retries; i++)); do
        if [[ -e "$path" ]]; then
            return 0
        fi
        sleep "$delay"
    done

    return 1
}

remove_with_retry() {
    local path="$1"
    local retries="${2:-30}"
    local delay="${3:-0.3}"

    if [[ ! -e "$path" ]]; then
        return 0
    fi

    for ((i = 0; i < retries; i++)); do
        if rm -rf "$path" 2>/dev/null; then
            return 0
        fi
        sleep "$delay"
    done

    fail "Failed to remove $path after multiple retries."
}

resolve_godot() {
    if [[ -n "${GODOT_PATH:-}" && -f "$GODOT_PATH" ]]; then
        echo "$GODOT_PATH"
        return
    fi

    local candidates=(
        "$ROOT_DIR/libs/Godot_v4.5.1-stable_linux.x86_64"
        "$ROOT_DIR/libs/Godot_v4.5-stable_linux.x86_64"
        "$ROOT_DIR/libs/Godot_v4.5.1-stable_macos.universal"
        "$ROOT_DIR/libs/Godot_v4.5-stable_macos.universal"
    )

    for candidate in "${candidates[@]}"; do
        if [[ -f "$candidate" ]]; then
            echo "$candidate"
            return
        fi
    done

    if command -v godot4 >/dev/null 2>&1; then
        command -v godot4
        return
    fi

    if command -v godot >/dev/null 2>&1; then
        command -v godot
        return
    fi

    fail "Godot 4.5.x was not found. Put it under libs/ or set GODOT_PATH."
}

sts2_dll_candidates_for_game_path() {
    local game_path="$1"

    printf '%s\n' \
        "$game_path/data_sts2_windows_x86_64/sts2.dll" \
        "$game_path/data_sts2_linux_x86_64/sts2.dll" \
        "$game_path/data_sts2_macos_x86_64/sts2.dll" \
        "$game_path/SlayTheSpire2.app/Contents/MacOS/data_sts2_macos_x86_64/sts2.dll" \
        "$game_path/sts2.dll"
}

steamworks_dll_candidates_for_game_path() {
    local game_path="$1"

    printf '%s\n' \
        "$game_path/data_sts2_windows_x86_64/Steamworks.NET.dll" \
        "$game_path/data_sts2_linux_x86_64/Steamworks.NET.dll" \
        "$game_path/data_sts2_macos_x86_64/Steamworks.NET.dll" \
        "$game_path/SlayTheSpire2.app/Contents/MacOS/data_sts2_macos_x86_64/Steamworks.NET.dll" \
        "$game_path/Steamworks.NET.dll"
}

steam_library_roots() {
    local roots=()

    if [[ -n "${STEAM_DIR:-}" && -d "$STEAM_DIR" ]]; then
        roots+=("$STEAM_DIR")
    fi

    roots+=(
        "$HOME/.steam/steam"
        "$HOME/.local/share/Steam"
        "$HOME/Library/Application Support/Steam"
    )

    for root in "${roots[@]}"; do
        [[ -d "$root" ]] || continue
        printf '%s\n' "$root"

        local library_vdf="$root/steamapps/libraryfolders.vdf"
        [[ -f "$library_vdf" ]] || continue
        sed -nE 's/.*"path"[[:space:]]+"([^"]+)".*/\1/p' "$library_vdf" | sed 's#\\\\#/#g'
    done
}

resolve_sts2_assembly() {
    local candidates=()

    if [[ -n "$EXPLICIT_STS2_ASSEMBLY" ]]; then
        candidates+=("$EXPLICIT_STS2_ASSEMBLY")
    fi

    if [[ -n "${Sts2AssemblyPath:-}" ]]; then
        candidates+=("$Sts2AssemblyPath")
    fi
    if [[ -n "${STS2_ASSEMBLY_PATH:-}" ]]; then
        candidates+=("$STS2_ASSEMBLY_PATH")
    fi

    for game_path in "${STS2GamePath:-}" "${STS2_GAME_PATH:-}"; do
        [[ -n "$game_path" ]] || continue
        while IFS= read -r candidate; do
            candidates+=("$candidate")
        done < <(sts2_dll_candidates_for_game_path "$game_path")
    done

    while IFS= read -r library_root; do
        [[ -n "$library_root" ]] || continue
        while IFS= read -r candidate; do
            candidates+=("$candidate")
        done < <(sts2_dll_candidates_for_game_path "$library_root/steamapps/common/Slay the Spire 2")
    done < <(steam_library_roots)

    candidates+=("$ROOT_DIR/libs/sts2.dll")

    local seen=":"
    for candidate in "${candidates[@]}"; do
        [[ -n "$candidate" ]] || continue
        case "$seen" in
            *":$candidate:"*) continue ;;
        esac
        seen="$seen$candidate:"
        if [[ -f "$candidate" ]]; then
            echo "$candidate"
            return
        fi
    done

    fail "sts2.dll was not found. Set STS2GamePath or Sts2AssemblyPath."
}

resolve_steamworks_assembly() {
    local candidates=()

    if [[ -n "$EXPLICIT_STEAMWORKS_ASSEMBLY" ]]; then
        candidates+=("$EXPLICIT_STEAMWORKS_ASSEMBLY")
    fi

    if [[ -n "${SteamworksAssemblyPath:-}" ]]; then
        candidates+=("$SteamworksAssemblyPath")
    fi
    if [[ -n "${STEAMWORKS_ASSEMBLY_PATH:-}" ]]; then
        candidates+=("$STEAMWORKS_ASSEMBLY_PATH")
    fi

    for game_path in "${STS2GamePath:-}" "${STS2_GAME_PATH:-}"; do
        [[ -n "$game_path" ]] || continue
        while IFS= read -r candidate; do
            candidates+=("$candidate")
        done < <(steamworks_dll_candidates_for_game_path "$game_path")
    done

    while IFS= read -r library_root; do
        [[ -n "$library_root" ]] || continue
        while IFS= read -r candidate; do
            candidates+=("$candidate")
        done < <(steamworks_dll_candidates_for_game_path "$library_root/steamapps/common/Slay the Spire 2")
    done < <(steam_library_roots)

    candidates+=("$ROOT_DIR/libs/Steamworks.NET.dll")

    local seen=":"
    for candidate in "${candidates[@]}"; do
        [[ -n "$candidate" ]] || continue
        case "$seen" in
            *":$candidate:"*) continue ;;
        esac
        seen="$seen$candidate:"
        if [[ -f "$candidate" ]]; then
            echo "$candidate"
            return
        fi
    done

    fail "Steamworks.NET.dll was not found. Set STS2GamePath or SteamworksAssemblyPath."
}

write_minimal_project() {
    cat > "$1" <<'EOF'
; Auto-generated by tools/build_release.sh
config_version=5

[application]

config/name="Remove Multiplayer PlayerLimit"
config/features=PackedStringArray("4.5", "Forward Plus")
EOF
}

write_config_template() {
    cat > "$1" <<'EOF'
[macos]
tls_workaround=true

[multiplayer]
difficulty_scaling=true
EOF
}

new_pack_project() {
    rm -rf "$PACK_PROJECT"
    mkdir -p "$PACK_PROJECT/tools"
    write_minimal_project "$PACK_PROJECT/project.godot"
    cp "$MANIFEST" "$PACK_PROJECT/RemoveMultiplayerPlayerLimit.json"
    cp -R "$ROOT_DIR/RemoveMultiplayerPlayerLimit" "$PACK_PROJECT/RemoveMultiplayerPlayerLimit"
    cp "$ROOT_DIR/tools/build_pck.gd" "$PACK_PROJECT/tools/build_pck.gd"
}

manifest_value() {
    local query="$1"
    jq -r "$query" "$MANIFEST"
}

if [[ "$CONFIGURATION" != "Debug" && "$CONFIGURATION" != "Release" ]]; then
    fail "Configuration must be Debug or Release."
fi

GODOT="$(resolve_godot)"
STS2_ASSEMBLY="$(resolve_sts2_assembly)"
STEAMWORKS_ASSEMBLY="$(resolve_steamworks_assembly)"

echo ""
echo "====================================================="
echo "  RMP Build System  |  Current Mod Build"
echo "====================================================="
echo ""
echo "  Root          : $ROOT_DIR"
echo "  Configuration : $CONFIGURATION"
echo "  Dotnet        : $DOTNET"
echo "  Godot         : $GODOT"
echo "  sts2.dll      : $STS2_ASSEMBLY"
echo "  Steamworks    : $STEAMWORKS_ASSEMBLY"
echo ""

mkdir -p "$BUILD_ROOT"

echo "[1/5] Building DLL..."
"$DOTNET" build "$CSPROJ" -c "$CONFIGURATION" "/p:Sts2AssemblyPath=$STS2_ASSEMBLY" "/p:SteamworksAssemblyPath=$STEAMWORKS_ASSEMBLY"
[[ -f "$DLL_SOURCE" ]] || fail "Built DLL was not found at $DLL_SOURCE"
echo "  DLL built successfully."
echo ""

echo "[2/5] Preparing minimal pack project..."
new_pack_project
echo "  Minimal pack project prepared."
echo ""

echo "[3/5] Importing mod resources..."
"$GODOT" --headless --path "$PACK_PROJECT" --import
for _ in $(seq 1 20); do
    if compgen -G "$PACK_PROJECT/.godot/imported/mod_image.png-*.ctex" >/dev/null; then
        break
    fi
    sleep 0.2
done

if compgen -G "$PACK_PROJECT/.godot/imported/mod_image.png-*.ctex" >/dev/null; then
    echo "  mod_image.png .ctex generated successfully."
else
    echo "  [WARN] mod_image.png .ctex was not generated. Cover image may not display in-game."
fi
echo ""

echo "[4/5] Packing PCK resources..."
"$GODOT" --headless --path "$PACK_PROJECT" --script res://tools/build_pck.gd
wait_for_path "$TEMP_PCK_PATH" || fail "Packed PCK was not found at $TEMP_PCK_PATH"
cp "$TEMP_PCK_PATH" "$FINAL_PCK_PATH"
echo "  PCK packed successfully."
echo ""

echo "[5/5] Assembling release and ZIP..."
remove_with_retry "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"

cp "$DLL_SOURCE" "$RELEASE_DIR/RemoveMultiplayerPlayerLimit.dll"
cp "$FINAL_PCK_PATH" "$RELEASE_DIR/RemoveMultiplayerPlayerLimit.pck"
cp "$MANIFEST" "$RELEASE_DIR/RemoveMultiplayerPlayerLimit.json"

if [[ -f "$ROOT_DIR/config.ini" ]]; then
    cp "$ROOT_DIR/config.ini" "$RELEASE_DIR/config.ini"
else
    write_config_template "$RELEASE_DIR/config.ini"
fi

if ! command -v jq >/dev/null 2>&1; then
    fail "jq is required to read the manifest."
fi

VERSION="$(manifest_value '.version // empty')"
MOD_FOLDER_NAME="$(manifest_value 'if .pck_name and .pck_name != "" then .pck_name else .name end')"
[[ -n "$VERSION" ]] || fail "Manifest is missing the version field."
[[ -n "$MOD_FOLDER_NAME" ]] || fail "Manifest is missing the name/pck_name field."

ZIP_NAME="sts2-RMP-$VERSION.zip"
ZIP_PATH="$BUILD_ROOT/$ZIP_NAME"
ZIP_STAGE="$BUILD_ROOT/_zip_stage"
ZIP_MOD="$ZIP_STAGE/$MOD_FOLDER_NAME"

rm -f "$ZIP_PATH"
remove_with_retry "$ZIP_STAGE"
mkdir -p "$ZIP_MOD"
cp -R "$RELEASE_DIR"/. "$ZIP_MOD/"

if ! command -v zip >/dev/null 2>&1; then
    fail "zip is required to create the release archive."
fi

(
    cd "$ZIP_STAGE"
    zip -qr "../$ZIP_NAME" "$MOD_FOLDER_NAME"
)

remove_with_retry "$ZIP_STAGE"
remove_with_retry "$PACK_PROJECT"

DLL_SIZE="$(du -h "$RELEASE_DIR/RemoveMultiplayerPlayerLimit.dll" | cut -f1)"
PCK_SIZE="$(du -h "$RELEASE_DIR/RemoveMultiplayerPlayerLimit.pck" | cut -f1)"
ZIP_SIZE="$(du -h "$ZIP_PATH" | cut -f1)"

echo "  Release directory assembled."
echo ""
echo "====================================================="
echo "  Build Complete!"
echo "====================================================="
echo ""
echo "  Version  : $VERSION"
echo "  DLL      : $DLL_SIZE"
echo "  PCK      : $PCK_SIZE"
echo "  ZIP      : $ZIP_PATH ($ZIP_SIZE)"
echo "  Release  : $RELEASE_DIR"
echo ""
