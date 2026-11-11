#!/bin/bash
# Hardmacro Craft Launcher v0.1.10 —— 构建 + 打包三件套（hcl2 工作副本布局）
set -e
export DOTNET_ROOT=/tmp/my-project/dotnet2
export PATH=/tmp/my-project/dotnet2:$PATH
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_ROLL_FORWARD=LatestMajor
ROOT=/home/z/my-project/hcl2
VER=0.1.10
DL=/home/z/my-project/download
STG=/tmp/hcl-staging

cd "$ROOT"

echo "== [1/6] 构建（版本校验）=="
dotnet build src/HardmacroCraft/HardmacroCraft.csproj -c Release -p:Platform=x64 -r win-x64 --nologo 2>&1 | tail -2
rg -n '"0.1.10"' src/HardmacroCraft/Services/LauncherService.cs | head -1

echo "== [2/6] 发布单文件版 =="
rm -rf "$STG"; mkdir -p "$STG/single" "$STG/portable"
dotnet publish src/HardmacroCraft/HardmacroCraft.csproj \
  -c Release -r win-x64 -p:Platform=x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$STG/single" --nologo 2>&1 | tail -2

echo "== [3/6] 发布便携文件夹版 =="
dotnet publish src/HardmacroCraft/HardmacroCraft.csproj \
  -c Release -r win-x64 -p:Platform=x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o "$STG/portable" --nologo 2>&1 | tail -2

echo "== [4/6] 组装 staging =="
mv "$STG/single/HardmacroCraftLauncher.exe" "$STG/"
cat > "$STG/portable/!运行说明.txt" <<'NOTE'
Hardmacro Craft Launcher —— 便携文件夹版
· 整个文件夹拷走即可迁移（配置存同目录 config/）
· 必须「解压后再运行」HardmacroCraftLauncher.exe：
  在压缩软件里直接双击会丢失 DLL，导致误报「需要安装 .NET」
· 自带 .NET 运行时，无需安装任何依赖；杀软拦截请加白名单
NOTE
ls -la "$STG/"

echo "== [5/6] 打 zip =="
mkdir -p "$DL"
cd "$STG"
zip -q -j "$DL/HardmacroCraft-Launcher-v$VER-SingleFile.zip" HardmacroCraftLauncher.exe
cd "$STG/portable" && zip -q -r "$DL/HardmacroCraft-Launcher-v$VER-Portable.zip" .
cd "$ROOT" && zip -q -r "$DL/HardmacroCraft-source-v$VER.zip" src tests LICENSE README.md docs \
  -x "*/bin/*" -x "*/obj/*" -x "*/.vs/*"

echo "== [6/6] 校验 =="
ls -la "$DL" | grep "$VER"
echo "---- SHA1 ----"
sha1sum "$DL"/HardmacroCraft-Launcher-v$VER-SingleFile.zip \
        "$DL"/HardmacroCraft-Launcher-v$VER-Portable.zip \
        "$DL"/HardmacroCraft-source-v$VER.zip
echo DONE
