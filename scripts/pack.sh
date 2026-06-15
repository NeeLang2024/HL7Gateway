#!/usr/bin/env bash
# 打包：前端 + Service + WebApi + bridge + 安装脚本 -> publish/HL7Gateway.zip
# 安装方式：install.bat（中间件 Service+WebApi） + bridge/install-bridge-service.bat（桥接件单独）
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PUBLISH="$ROOT/publish"

echo "==> [1/5] Build frontend"
cd "$ROOT/frontend"
npm run build
rm -rf "$ROOT/src/HL7Gateway.WebApi/wwwroot"
mkdir -p "$ROOT/src/HL7Gateway.WebApi/wwwroot"
cp -R dist/. "$ROOT/src/HL7Gateway.WebApi/wwwroot/"

echo "==> [2/5] Publish Service + WebApi (win-x64)"
dotnet publish "$ROOT/src/HL7Gateway.Service/HL7Gateway.Service.csproj" \
  -c Release -r win-x64 --self-contained false -o "$PUBLISH/Service"
dotnet publish "$ROOT/src/HL7Gateway.WebApi/HL7Gateway.WebApi.csproj" \
  -c Release -r win-x64 --self-contained false -o "$PUBLISH/WebApi"

echo "==> [3/5] Clean stale wwwroot assets"
ASSETS="$PUBLISH/WebApi/wwwroot/assets"
DIST_ASSETS="$ROOT/frontend/dist/assets"
if [[ -d "$ASSETS" && -d "$DIST_ASSETS" ]]; then
  for f in "$ASSETS"/*; do
    base="$(basename "$f")"
    base="${base%.br}"
    base="${base%.gz}"
    if [[ ! -f "$DIST_ASSETS/$base" ]]; then
      rm -f "$f"
    fi
  done
fi

echo "==> [4/5] Verify publish layout"
test -d "$PUBLISH/bridge" || { echo "ERROR: publish/bridge missing"; exit 1; }
test -f "$PUBLISH/install.bat" || { echo "ERROR: publish/install.bat missing"; exit 1; }
test -f "$PUBLISH/bridge/install-bridge-service.bat" || { echo "ERROR: bridge installer missing"; exit 1; }
if [[ -f "$PUBLISH/update.bat" ]]; then
  echo "ERROR: update.bat should be removed"
  exit 1
fi

echo "==> [5/5] Create HL7Gateway.zip"
cd "$PUBLISH"
rm -f HL7Gateway.zip
zip -r -q -X HL7Gateway.zip \
  Service WebApi bridge \
  install.bat uninstall.bat force-clean.bat 安装说明.txt \
  -x "*/backup/*" "backup/*" "*/jwt.key" "jwt.key"
ls -lh HL7Gateway.zip

echo "Done: $PUBLISH/HL7Gateway.zip"
