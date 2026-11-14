#!/bin/bash
# Hardmacro Craft Launcher v0.1.12 —— 构建 + 双发布 + 打包（沿用 v0.1.11 流程）
set -e
export DOTNET_ROOT=/tmp/my-project/dotnet2
export PATH=/tmp/my-project/dotnet2:$PATH
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_ROLL_FORWARD=LatestMajor
ROOT=/home/z/my-project/hcl2-src
VER=0.1.12
DL=/home/z/my-project/download
STG=/home/z/my-project/staging-0112

echo "== [1/5] 构建校验（已通过 574 测试）=="

echo "== [2/5] 发布单文件版 =="
rm -rf "$STG"; mkdir -p "$STG/single" "$STG/portable"
dotnet publish "$ROOT/src/HardmacroCraft/HardmacroCraft.csproj" \
  -c Release -r win-x64 -p:Platform=x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$STG/single" --nologo 2>&1 | tail -1

echo "== [3/5] 发布便携文件夹版 =="
dotnet publish "$ROOT/src/HardmacroCraft/HardmacroCraft.csproj" \
  -c Release -r win-x64 -p:Platform=x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o "$STG/portable" --nologo 2>&1 | tail -1

echo "== [4/5] 打 zip =="
mkdir -p "$DL"
mv "$STG/single/HardmacroCraftLauncher.exe" "$STG/"
cd "$STG"
zip -q -j "$DL/HardmacroCraft-Launcher-v$VER-SingleFile.zip" HardmacroCraftLauncher.exe
cd "$STG/portable" && zip -q -r "$DL/HardmacroCraft-Launcher-v$VER-Portable.zip" .
cd "$ROOT" && zip -q -r "$DL/HardmacroCraft-source-v$VER.zip" src tests check_lang_keys.py \
  -x "*/bin/*" -x "*/obj/*" -x "*/.vs/*"

echo "== [5/5] 校验 =="
ls -la "$DL" | grep "$VER"
echo DONE
