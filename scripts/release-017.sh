#!/bin/bash
set -e
T=$(cat /tmp/.ghtoken)
REPO=Macrohard0001/HCL
TAG=v0.1.7
DL=/home/z/my-project/download

cat > /tmp/release_body.md <<'BODY'
## v0.1.7 更新内容

### 运行中的游戏实例管理（多实例）
- 主页实时列出每个由启动器拉起的实例：版本 / PID / 状态（等待窗口出现 → 运行中）
- 一键「结束」整个进程树（含 Java 派生子进程），进程退出自动移出列表并记录退出码
- 支持同时运行多个版本 / 多开

### 启动进度更完整（等待游戏窗口阶段）
- 新增阶段【4/4】「等待游戏窗口出现」：游戏进程创建后进度区持续有阶段说明，消除「已启动 → 游戏窗口出现」之间的空白期
- 窗口出现 / 进程提前退出（含退出码）/ 等待超时均有明确反馈；等待期取消按钮隐藏（改为实例卡结束进程）
- 启动流程本体在进程创建后即结束，可立即再启动第二个实例

### 布局响应式（主容器不滚动）
- 首页：账号卡置顶、日志卡展开后撑满剩余空间、启动卡钉在窗口底缘；移除底部快捷入口
- 模组页：搜索结果列表撑满剩余空间；设置页 / 关于页：标题固定 + 内容区内滚
- 版本管理页：搜索框旁新增版本计数（搜索时显示匹配数 / 总数）

### 紧急修复
- 首页因图标名无效导致 XAML 解析崩溃（一点首页即闪退）的问题已修复；全部图标名已对照 Wpf.Ui 4.3 枚举逐项审计

---

**下载**（自包含，无需安装 .NET）：
- `HardmacroCraft-Launcher-v0.1.7-SingleFile.zip` —— 单文件版，解压后运行 `HardmacroCraftLauncher.exe`
- `HardmacroCraft-Launcher-v0.1.7-Portable.zip` —— 便携文件夹版，整个文件夹拷走即可迁移（配置存同目录 `config/`；必须解压后再运行）
- `HardmacroCraft-source-v0.1.7.zip` —— 源码

432 项自动化测试全绿 · 文案检查通过 · MIT License · 开发者 Fox_Ryn (Macrohard0001)
BODY

python3 - <<'PY'
import json
body = open('/tmp/release_body.md', encoding='utf-8').read()
json.dump({'tag_name': 'v0.1.7', 'name': 'v0.1.7',
           'body': body, 'draft': False, 'prerelease': False,
           'target_commitish': 'main'},
          open('/tmp/release_payload.json', 'w', encoding='utf-8'), ensure_ascii=False)
PY

curl -s -X POST -H "Authorization: Bearer $T" -H "Accept: application/vnd.github+json" \
  https://api.github.com/repos/$REPO/releases \
  -d @/tmp/release_payload.json > /tmp/release_resp.json
RID=$(python3 -c "import json; print(json.load(open('/tmp/release_resp.json'))['id'])")
echo "release id=$RID"

for f in "HardmacroCraft-Launcher-v0.1.7-SingleFile.zip" \
         "HardmacroCraft-Launcher-v0.1.7-Portable.zip" \
         "HardmacroCraft-source-v0.1.7.zip"; do
  code=$(curl -s -o /tmp/up.json -w "%{http_code}" \
    -X POST -H "Authorization: Bearer $T" -H "Content-Type: application/zip" \
    --data-binary @"$DL/$f" \
    "https://uploads.github.com/repos/$REPO/releases/$RID/assets?name=$f")
  echo "$f -> HTTP $code"
done

curl -s -H "Authorization: Bearer $T" "https://api.github.com/repos/$REPO/releases/tags/$TAG" \
  | python3 -c "import json,sys; r=json.load(sys.stdin); print('tag:', r['tag_name'], '| assets:'); [print(' -', a['name'], a['size']) for a in r['assets']]"
