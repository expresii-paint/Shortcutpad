# Builds the macOS app bundle. Run this ON the Mac.
#
#   cd ShortcutPad
#   cp NuGet.config.macOS NuGet.config   # or delete NuGet.config
#   bash tools/build-macos.sh
#
# Output: dist-mac/ShortcutPad.app  (self-contained, runs on Apple Silicon)

set -euo pipefail

cd "$(dirname "$0")/.."

RID="${RID:-osx-arm64}"          # osx-arm64 (Apple Silicon) or osx-x64 (Intel)

dotnet publish src/ShortcutPad.App/ShortcutPad.App.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -p:NuGetAudit=false \
  -o "dist-mac"

echo
echo "Built dist-mac/ShortcutPad.app"
echo
echo "First run: macOS will block the keystrokes until you grant access:"
echo "  System Settings -> Privacy & Security -> Accessibility -> enable ShortcutPad"
echo
echo "Run with:  open dist-mac/ShortcutPad.app"
