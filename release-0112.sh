#!/bin/bash
# Hardmacro Craft Launcher v0.1.12 —— GitHub Release 发布链（删旧 release/tag → 重建 → 传资产）
set -e
T=$(cat /tmp/.ghtoken)
REPO=Macrohard0001/HCL
TAG=v0.1.12
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
## v0.1.12 更新内容

### 新增「一键配置 Mods」标签页
- 预设 = 一组云端 Mod 清单（Modrinth / CurseForge 项目），安装时按所选版本与加载器自动匹配最新兼容文件
- 「开始配置」：预设全部条目一键安装到所选版本（已安装自动跳过，全程任务中心可取消）
- 「开始移除」：把该预设安装到所选版本的文件一键移除（先备份到 config/mod-backup/ 再删除，可在设置中关闭备份）
- 预设来源：Mod 页勾选保存（沿用 v0.1.5 预设机制），或**从已装版本一键导入**——逐 jar 计算 SHA1 反查 Modrinth 项目自动生成清单（CurseForge 专属 / 本地 Mod 无法反查，单独汇报跳过数）
- 预设管理：新建导入 / 重命名 / 删除；安装成功的文件名自动记录，作为移除依据

### 第三方登录多角色免密切换
- 登录时保存验证站账号下全部可用角色，账号管理内「切换角色」凭 accessToken 直接绑定新角色，**无需重新输入密码**
- 选择账号改为**点击账号卡片**直接切换（移除「切换」按钮）
- 当前账号左侧显示 **Fluent 强调色竖指示条**（PCL 同款视觉锚点）

### 启动器内存占用口径修复
- 此前展示的是完整物理工作集（含大量共享系统库映射），与任务管理器显示对不上，且只在进入设置页时读取一次
- 现改为「专用内存（提交大小）+ 工作集」双口径，设置页每 2 秒实时刷新
- 「立即清理」的收益同步展示真实数字（物理工作集前后 + 托管堆前后）

### 启动卡住修复 + 启动提速
- 创建游戏进程阶段每 30 秒输出心跳日志（网络慢时能明确看到"仍在推进"），取消令牌即时生效不再无限干等
- 等待安装闸门期间每 10 秒输出等待日志（此前静默等满 15 分钟像死机）
- 第三方验证站令牌校验 / 续期补齐 15 / 20 秒超时
- Java 版本探测按路径缓存：每次启动不再重复起子进程等待（最长 15 秒的固定延迟消除）

### 取消任务不再"卡住"
- 点「取消」立即反馈：任务卡片写入「正在取消…」日志行，不再长时间停留在"运行中"无响应状态
- Mod 页预设安装改为可取消任务（逐条目粒度响应）

### 界面人性化优化
- 账号管理窗口可调整大小，账号多时列表可滚动不再溢出
- 账号管理 / 模组预设 / 角色选择等弹窗支持 Esc 关闭

---

**下载**（自包含，无需安装 .NET）：
- `HardmacroCraft-Launcher-v0.1.12-SingleFile.zip` —— 单文件版，解压后运行 `HardmacroCraftLauncher.exe`
- `HardmacroCraft-Launcher-v0.1.12-Portable.zip` —— 便携文件夹版，整个文件夹拷走即可迁移（配置存同目录 `config/`；必须解压后再运行）
- `HardmacroCraft-source-v0.1.12.zip` —— 源码

574 项自动化测试全绿（+16） · 语言键检查通过（543=543） · MIT License · 开发者 Fox_Ryn (Macrohard0001)
BODY

python3 - <<'PY'
import json
body = open('/tmp/release_body.md', encoding='utf-8').read()
json.dump({'tag_name': 'v0.1.12', 'name': 'v0.1.12',
           'body': body, 'draft': False, 'prerelease': False,
           'target_commitish': 'main'},
          open('/tmp/release_payload.json', 'w', encoding='utf-8'), ensure_ascii=False)
PY

curl -s -X POST -H "Authorization: Bearer $T" -H "Accept: application/vnd.github+json" \
  https://api.github.com/repos/$REPO/releases \
  -d @/tmp/release_payload.json > /tmp/release_resp.json
RID=$(python3 -c "import json; print(json.load(open('/tmp/release_resp.json'))['id'])")
echo "release id=$RID"

for f in "HardmacroCraft-Launcher-v0.1.12-SingleFile.zip" \
         "HardmacroCraft-Launcher-v0.1.12-Portable.zip" \
         "HardmacroCraft-source-v0.1.12.zip"; do
  code=$(curl -s -o /tmp/up.json -w "%{http_code}" \
    -X POST -H "Authorization: Bearer $T" -H "Content-Type: application/zip" \
    --data-binary @"$DL/$f" \
    "https://uploads.github.com/repos/$REPO/releases/$RID/assets?name=$f")
  echo "$f -> HTTP $code"
done

curl -s -H "Authorization: Bearer $T" "https://api.github.com/repos/$REPO/releases/tags/$TAG" \
  | python3 -c "import json,sys; r=json.load(sys.stdin); print('tag:', r['tag_name'], '| assets:'); [print(' -', a['name'], a['size']) for a in r['assets']]"
