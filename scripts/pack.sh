#!/usr/bin/env bash
# 打包：前端 + Service + WebApi + bridge + 安装脚本 -> publish/HL7Gateway.zip
# 安装方式：install.bat（中间件 Service+WebApi） + bridge/install-bridge-service.bat（桥接件单独）
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PUBLISH="$ROOT/publish"

echo "==> [1/6] Build frontend"
cd "$ROOT/frontend"
npm run build
rm -rf "$ROOT/src/HL7Gateway.WebApi/wwwroot"
mkdir -p "$ROOT/src/HL7Gateway.WebApi/wwwroot"
cp -R dist/. "$ROOT/src/HL7Gateway.WebApi/wwwroot/"

echo "==> [2/6] Publish Service + WebApi (win-x64)"
dotnet publish "$ROOT/src/HL7Gateway.Service/HL7Gateway.Service.csproj" \
  -c Release -r win-x64 --self-contained false -o "$PUBLISH/Service"
dotnet publish "$ROOT/src/HL7Gateway.WebApi/HL7Gateway.WebApi.csproj" \
  -c Release -r win-x64 --self-contained false -o "$PUBLISH/WebApi"

echo "==> [3/6] Sync bridge source to publish/bridge"
BRIDGE_PUB="$PUBLISH/bridge"
mkdir -p "$BRIDGE_PUB/tools"
for f in build-bridge.bat run-bridge.bat install-bridge-service.bat uninstall-bridge-service.bat; do
  if [[ -f "$ROOT/$f" ]]; then
    cp -f "$ROOT/$f" "$BRIDGE_PUB/"
  fi
done
rsync -a --delete \
  --exclude 'bin/' --exclude 'obj/' --exclude 'build.log' --exclude 'README.md' \
  "$ROOT/tools/PhilipsHifBridge/" "$BRIDGE_PUB/tools/PhilipsHifBridge/"

# 只复制桥接件必需的 dll_NEW 子集（见 scripts/bridge-dll-new.list），禁止整包 rsync
BRIDGE_DLL_LIST="$ROOT/scripts/bridge-dll-new.list"
BRIDGE_DLL_DST="$BRIDGE_PUB/dll_NEW"
if [[ -f "$BRIDGE_DLL_LIST" && -d "$ROOT/dll_NEW" ]]; then
  rm -rf "$BRIDGE_DLL_DST"
  mkdir -p "$BRIDGE_DLL_DST"
  while IFS= read -r name || [[ -n "$name" ]]; do
    [[ -z "$name" || "$name" =~ ^# ]] && continue
    src="$ROOT/dll_NEW/$name"
    if [[ -f "$src" ]]; then
      cp -f "$src" "$BRIDGE_DLL_DST/"
    else
      echo "WARN: missing bridge dll: $name" >&2
    fi
  done < "$BRIDGE_DLL_LIST"
fi

echo "==> [4/6] Clean stale wwwroot assets"
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

echo "==> [5/6] Verify publish layout"
test -d "$PUBLISH/bridge" || { echo "ERROR: publish/bridge missing"; exit 1; }
test -f "$PUBLISH/install.bat" || { echo "ERROR: publish/install.bat missing"; exit 1; }
test -f "$PUBLISH/bridge/install-bridge-service.bat" || { echo "ERROR: bridge installer missing"; exit 1; }
test -f "$PUBLISH/bridge/dll_NEW/Philips.PlatformServices.dll" || { echo "ERROR: bridge dll_NEW subset missing"; exit 1; }
if [[ -f "$PUBLISH/update.bat" ]]; then
  echo "ERROR: update.bat should be removed"
  exit 1
fi

echo "==> [6/6] Create HL7Gateway.zip"
cd "$PUBLISH"
rm -f HL7Gateway.zip
zip -r -q -X HL7Gateway.zip \
  Service WebApi bridge \
  install.bat uninstall.bat force-clean.bat 安装说明.txt \
  -x "*/backup/*" "backup/*" "*/jwt.key" "jwt.key"
ls -lh HL7Gateway.zip

echo "Done: $PUBLISH/HL7Gateway.zip"
