#!/bin/bash
# Build and deploy the AutoSpire mod
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/AutoSpire"
GAME_APP="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
GAME_DIR="$GAME_APP/Contents/Resources/data_sts2_macos_arm64"
MODS_DIR="$GAME_APP/Contents/MacOS/mods"

echo "=== Building AutoSpire mod ==="
cd "$PROJECT_DIR"
dotnet build -p:STS2_GAME_DIR="$GAME_DIR" -c Release -o "$SCRIPT_DIR/out" 2>&1

echo ""
echo "=== Creating PCK file ==="
python3 "$SCRIPT_DIR/create_pck.py" "$SCRIPT_DIR/out/AutoSpire.pck"

echo ""
echo "=== Deploying to $MODS_DIR ==="
mkdir -p "$MODS_DIR"
cp "$SCRIPT_DIR/out/AutoSpire.dll" "$MODS_DIR/"
cp "$SCRIPT_DIR/out/AutoSpire.pck" "$MODS_DIR/"

echo ""
echo "=== Done! ==="
echo "Mod installed at: $MODS_DIR"
ls -la "$MODS_DIR/"
echo ""
echo "Start the game and check logs at:"
echo "  ~/Library/Application Support/SlayTheSpire2/logs/godot.log"
echo "  grep AutoSpire from the log to verify loading"
echo ""
echo "Test with: curl http://localhost:31452/ping"
