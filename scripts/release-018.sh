#!/bin/bash
set -e
T=$(cat /tmp/.ghtoken)
REPO=Macrohard0001/HCL
TAG=v0.1.8
DL=/home/z/my-project/download

cat > /tmp/release_body.md <<'BODY'
## v0.1.8 更新内容

### 模组筛选弹出卡（对齐 PCL）
- 搜索条上平铺的 5 个下拉框（平台 / 排序 / 类型 / 加载器 / 目标版本）收进一个「筛选」按钮，点击弹出独立筛选卡片，点外部关闭
- 按钮上实时显示激活条件数（如「筛选（2）」），非默认条件一目了然
- 新增一键「重置」：平台 / 类型 / 加载器回默认（排序与目标版本保留）
- 目标版本切换即记忆，下次进页自动选中同一版本

### 下载游戏页主容器去滚动（响应式）
- 版本浏览器由「整页 ScrollViewer + 列表限高 430」改为标题固定 + 列表撑满剩余空间（仅列表内滚），进度卡钉底
- 小窗口下不再出现整页滚动，与首页 / 模组 / 整合包 / 设置 / 关于 / 版本管理页行为一致

### 启动链路紧急修复
- 修复「点启动没反应」：未登录 / 未选版本 / Java 缺失 / 令牌失效 / 启动异常等前置失败，此前只写进默认折叠的日志框（看起来毫无反应），现改为弹窗提示 + 日志自动展开
- 首页版本列表改为每次进入都刷新：在下载页安装完成后回到首页，下拉框立即可选新版本（此前必须手动刷新）
- 安装完成后直接询问「是否立即启动该版本」（对齐主流启动器下载完即玩）
- 版本图标对齐主流启动器自动图标：有加载器按版本 json 内容显示对应加载器图标（自定义版本名同样有效，目录内全部 json 参与识别），纯净版一律原版草方块

### 性能优化
- 模组搜索结果 / 本机列表 / 整合包结果 / 版本列表 / 原版版本列表全部启用列表虚拟化回收模式（VirtualizationMode=Recycling），大数据量滚动更流畅、内存占用更低
- 修复模组页结果区行高不受约束、结果一多可能撑爆页面的隐患

---

**下载**（自包含，无需安装 .NET）：
- `HardmacroCraft-Launcher-v0.1.8-SingleFile.zip` —— 单文件版，解压后运行 `HardmacroCraftLauncher.exe`
- `HardmacroCraft-Launcher-v0.1.8-Portable.zip` —— 便携文件夹版，整个文件夹拷走即可迁移（配置存同目录 `config/`；必须解压后再运行）
- `HardmacroCraft-source-v0.1.8.zip` —— 源码

446 项自动化测试全绿 · 文案检查通过 · MIT License · 开发者 Fox_Ryn (Macrohard0001)
BODY

python3 - <<'PY'
import json
body = open('/tmp/release_body.md', encoding='utf-8').read()
json.dump({'tag_name': 'v0.1.8', 'name': 'v0.1.8',
           'body': body, 'draft': False, 'prerelease': False,
           'target_commitish': 'main'},
          open('/tmp/release_payload.json', 'w', encoding='utf-8'), ensure_ascii=False)
PY

curl -s -X POST -H "Authorization: Bearer $T" -H "Accept: application/vnd.github+json" \
  https://api.github.com/repos/$REPO/releases \
  -d @/tmp/release_payload.json > /tmp/release_resp.json
RID=$(python3 -c "import json; print(json.load(open('/tmp/release_resp.json'))['id'])")
echo "release id=$RID"

for f in "HardmacroCraft-Launcher-v0.1.8-SingleFile.zip" \
         "HardmacroCraft-Launcher-v0.1.8-Portable.zip" \
         "HardmacroCraft-source-v0.1.8.zip"; do
  code=$(curl -s -o /tmp/up.json -w "%{http_code}" \
    -X POST -H "Authorization: Bearer $T" -H "Content-Type: application/zip" \
    --data-binary @"$DL/$f" \
    "https://uploads.github.com/repos/$REPO/releases/$RID/assets?name=$f")
  echo "$f -> HTTP $code"
done

curl -s -H "Authorization: Bearer $T" "https://api.github.com/repos/$REPO/releases/tags/$TAG" \
  | python3 -c "import json,sys; r=json.load(sys.stdin); print('tag:', r['tag_name'], '| assets:'); [print(' -', a['name'], a['size']) for a in r['assets']]"
