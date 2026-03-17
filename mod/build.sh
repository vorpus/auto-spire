#!/bin/bash
# Build and deploy the AutoSpire mod
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/AutoSpire"
PACK_DIR="$SCRIPT_DIR/pack"
OUT_DIR="$SCRIPT_DIR/out"
GAME_APP="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
GAME_DIR="$GAME_APP/Contents/Resources/data_sts2_macos_arm64"
MODS_DIR="$GAME_APP/Contents/MacOS/mods"
GODOT="$HOME/.local/share/mise/installs/godot/4.5.1-stable/Godot.app/Contents/MacOS/Godot"

mkdir -p "$OUT_DIR"

echo "=== Building AutoSpire DLL ==="
cd "$PROJECT_DIR"
dotnet build -p:STS2_GAME_DIR="$GAME_DIR" -c Release -o "$OUT_DIR" 2>&1

echo ""
echo "=== Creating PCK with Godot ==="
cd "$PACK_DIR"
"$GODOT" --headless --export-pack "Windows Desktop" "$OUT_DIR/AutoSpire.pck" 2>&1

echo ""
echo "=== Deploying to $MODS_DIR ==="
mkdir -p "$MODS_DIR"
cp "$OUT_DIR/AutoSpire.dll" "$MODS_DIR/"
cp "$OUT_DIR/AutoSpire.pck" "$MODS_DIR/"

echo ""
echo "=== Done! ==="
ls -la "$MODS_DIR/"
echo ""
echo "Restart the game, then test with: curl http://localhost:31452/ping"
