#!/bin/bash
# Hardmacro Craft Launcher v0.1.10 —— GitHub Release 发布链（删旧 release/tag → 重建 → 传资产）
set -e
T=$(cat /tmp/.ghtoken)
REPO=Macrohard0001/HCL
TAG=v0.1.10
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
## v0.1.10 更新内容

### 启动进度更详细（速度 + 预计剩余时间）
- 下载 / 补全游戏文件时实时显示下载速度与预计剩余时间（按字节速度或已耗时估算）
- Java 下载此前只有百分比，现在同样显示「大小 / 速度 / 剩余约 xx 秒」
- 启动文案更详细：明确告知所需 / 推荐 Java、首次启动约需下载 300–500 MB 等

### 任务中心修复与增强
- 修复「重复任务」：任务支持去重 Key，同一逻辑任务重复登记时复用同一条
- 点击任务卡片可查看任务详情：状态 / 进度 / 起止时间 / 耗时 / 全部进度日志（可一键复制）

### 版本列表修复（装载器双版本）
- 修复安装带装载器（Forge / NeoForge / Fabric…）的版本后列表出现两个版本的问题
- 被装载器版本继承且无所有权标记的纯原版底座自动隐藏（PCL 同款规则）；用户显式下载过的原版不受影响

### 版本图标识别加固
- 加载器识别增加 json 自身 id 字段与安装器参数标记（--fml.forgeVersion / forgeclient / cpw.mods.bootstraplauncher 等）
- Legacy Fabric（带连字符特征）不再漏判；目录改名后的自定义版本同样正确显示加载器图标

### 预设 Mods 一键匹配安装
- 模组预设一键安装按条目类型分别装入对应目录（mod / 资源包 / 光影 / 数据包，不再因当前筛选类型不同整条跳过）
- 已安装的同名文件自动跳过（只补缺失）；全程登记任务中心任务（带进度日志）

### 游戏窗口大小设置对齐 PCL
- 新增「默认 1280×720 / 与启动器窗口大小一致 / 自定义宽高」三种模式
- 版本独立设置亦可按版本覆盖

### 设置增强
- 启动器内存优化支持「立即清理内存」手动触发（显示托管堆回收前后对比）
- 版本独立设置新增「窗口大小」「Mod 与内容」分组（一键打开该版本的 Mod 管理页并自动选中目标版本；目录入口新增「打开资源包」）

### 下载提示文案更详细
- 装载器安装分段提示（阶段 1 安装器处理 / 阶段 2 游戏文件下载），显示速度与剩余时间

---

**下载**（自包含，无需安装 .NET）：
- `HardmacroCraft-Launcher-v0.1.10-SingleFile.zip` —— 单文件版，解压后运行 `HardmacroCraftLauncher.exe`
- `HardmacroCraft-Launcher-v0.1.10-Portable.zip` —— 便携文件夹版，整个文件夹拷走即可迁移（配置存同目录 `config/`；必须解压后再运行）
- `HardmacroCraft-source-v0.1.10.zip` —— 源码

515 项自动化测试全绿 · 文案检查通过 · MIT License · 开发者 Fox_Ryn (Macrohard0001)
BODY

python3 - <<'PY'
import json
body = open('/tmp/release_body.md', encoding='utf-8').read()
json.dump({'tag_name': 'v0.1.10', 'name': 'v0.1.10',
           'body': body, 'draft': False, 'prerelease': False,
           'target_commitish': 'main'},
          open('/tmp/release_payload.json', 'w', encoding='utf-8'), ensure_ascii=False)
PY

curl -s -X POST -H "Authorization: Bearer $T" -H "Accept: application/vnd.github+json" \
  https://api.github.com/repos/$REPO/releases \
  -d @/tmp/release_payload.json > /tmp/release_resp.json
RID=$(python3 -c "import json; print(json.load(open('/tmp/release_resp.json'))['id'])")
echo "release id=$RID"

for f in "HardmacroCraft-Launcher-v0.1.10-SingleFile.zip" \
         "HardmacroCraft-Launcher-v0.1.10-Portable.zip" \
         "HardmacroCraft-source-v0.1.10.zip"; do
  code=$(curl -s -o /tmp/up.json -w "%{http_code}" \
    -X POST -H "Authorization: Bearer $T" -H "Content-Type: application/zip" \
    --data-binary @"$DL/$f" \
    "https://uploads.github.com/repos/$REPO/releases/$RID/assets?name=$f")
  echo "$f -> HTTP $code"
done

curl -s -H "Authorization: Bearer $T" "https://api.github.com/repos/$REPO/releases/tags/$TAG" \
  | python3 -c "import json,sys; r=json.load(sys.stdin); print('tag:', r['tag_name'], '| assets:'); [print(' -', a['name'], a['size']) for a in r['assets']]"
