#!/bin/bash
# Hardmacro Craft Launcher v0.1.9 —— GitHub Release 发布链（沿用 0.1.8 模式：删旧 release/tag → 重建 → 传资产）
set -e
T=$(cat /tmp/.ghtoken)
REPO=Macrohard0001/HCL
TAG=v0.1.9
DL=/home/z/my-project/download

# ---------- 0. 预清理：已存在的同名 release / tag 删除（幂等重发） ----------
OLD_ID=$(curl -s -H "Authorization: Bearer $T" "https://api.github.com/repos/$REPO/releases/tags/$TAG" \
  | python3 -c "import json,sys; r=json.load(sys.stdin); print(r.get('id',''))")
if [ -n "$OLD_ID" ]; then
  echo "删除旧 release $OLD_ID"
  curl -s -X DELETE -H "Authorization: Bearer $T" "https://api.github.com/repos/$REPO/releases/$OLD_ID"
  curl -s -X DELETE -H "Authorization: Bearer $T" "https://api.github.com/repos/$REPO/git/refs/tags/$TAG"
fi

cat > /tmp/release_body.md <<'BODY'
## v0.1.9 更新内容

### 推荐 Java 智能化（按游戏版本兼容矩阵）
- 新增 Java 兼容矩阵：<1.12 → 仅 Java 8；1.12–1.16 → 8–11；1.17 → 16–17；1.18–1.20.4 → 17–21；≥1.20.5 → 21+
- 缺 Java 时弹窗默认推荐「当前版本支持使用的最高 Java」：1.20.4 推荐 21、1.8.9 只推荐 8（不再一刀切最低版本）
- 启动自动挑选托管 Java 同样在支持范围内取最高，不再给老版本误选新 Java 导致崩溃
- 版本设置窗口新增推荐 Java 提示（含最低要求）与「浏览」选择按钮

### 下载行为对齐 PCL
- 安装 Forge / NeoForge / Fabric / Quilt / Legacy Fabric / LiteLoader 等装载器版本时，装载器装完立刻自动下载该版本的全部游戏文件（主 jar / 库 / 资源文件），装完即可直接启动
- 此前只有原版直装会下载完整文件，装载器版本要等首次启动才补全

### Mod 管理完全对齐 PCL
- 本机 Mod 列表直接从 jar 内解析元数据：名字 / 版本 / 描述 / 加载器徽章
- 支持五类清单：fabric.mod.json、quilt.mod.json、META-INF/mods.toml、META-INF/neoforge.mods.toml、mcmod.info（Forge 1.12-）；坏文件自动回退文件名
- 列表支持 Ctrl / Shift 多选、双击切换启用状态、本地搜索、批量启用 / 禁用 / 删除（删除带确认弹窗）
- 修复：版本独立设置覆盖了「版本隔离」时，Mod 目录仍按全局开关放置的问题 —— 现在严格按「版本覆盖 > 全局设置」生效值放置

### 版本独立设置对齐 PCL
- 新增「目录」分组：打开版本目录 / 存档 / Mod 目录 / 日志 四个快捷入口，目录位置自动跟随该版本隔离状态

### 首页日志滚动根治
- 查明根因：Wpf-Ui NavigationView 默认把页面包在 DynamicScrollViewer 里，日志展开后 TextBox 按全部文本高度测量、整页被撑高出现外层滚动条
- 已禁用外层动态滚动（各页面均自管滚动），日志展开后只在其内部滚动

---

**下载**（自包含，无需安装 .NET）：
- `HardmacroCraft-Launcher-v0.1.9-SingleFile.zip` —— 单文件版，解压后运行 `HardmacroCraftLauncher.exe`
- `HardmacroCraft-Launcher-v0.1.9-Portable.zip` —— 便携文件夹版，整个文件夹拷走即可迁移（配置存同目录 `config/`；必须解压后再运行）
- `HardmacroCraft-source-v0.1.9.zip` —— 源码

480 项自动化测试全绿 · 文案检查通过 · MIT License · 开发者 Fox_Ryn (Macrohard0001)
BODY

python3 - <<'PY'
import json
body = open('/tmp/release_body.md', encoding='utf-8').read()
json.dump({'tag_name': 'v0.1.9', 'name': 'v0.1.9',
           'body': body, 'draft': False, 'prerelease': False,
           'target_commitish': 'main'},
          open('/tmp/release_payload.json', 'w', encoding='utf-8'), ensure_ascii=False)
PY

curl -s -X POST -H "Authorization: Bearer $T" -H "Accept: application/vnd.github+json" \
  https://api.github.com/repos/$REPO/releases \
  -d @/tmp/release_payload.json > /tmp/release_resp.json
RID=$(python3 -c "import json; print(json.load(open('/tmp/release_resp.json'))['id'])")
echo "release id=$RID"

for f in "HardmacroCraft-Launcher-v0.1.9-SingleFile.zip" \
         "HardmacroCraft-Launcher-v0.1.9-Portable.zip" \
         "HardmacroCraft-source-v0.1.9.zip"; do
  code=$(curl -s -o /tmp/up.json -w "%{http_code}" \
    -X POST -H "Authorization: Bearer $T" -H "Content-Type: application/zip" \
    --data-binary @"$DL/$f" \
    "https://uploads.github.com/repos/$REPO/releases/$RID/assets?name=$f")
  echo "$f -> HTTP $code"
done

curl -s -H "Authorization: Bearer $T" "https://api.github.com/repos/$REPO/releases/tags/$TAG" \
  | python3 -c "import json,sys; r=json.load(sys.stdin); print('tag:', r['tag_name'], '| assets:'); [print(' -', a['name'], a['size']) for a in r['assets']]"
