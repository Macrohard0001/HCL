Hardmacro Craft Launcher 0.1.0-fix9 下载说明（独立开源 Minecraft 启动器，MIT 协议）
====================================================================

【三个文件怎么选】
1. HardmacroCraft-Launcher-0.1.0-fix9-SingleFile.zip（62MB，推荐大多数用户）
   · 解压后只有一个 HardmacroCraftLauncher.exe，双击即用
   · 自带 .NET 运行时，无需安装任何依赖
2. HardmacroCraft-Launcher-0.1.0-fix9-Portable.zip（66MB）
   · 解压后是文件夹版（含全部 DLL），启动略快
   · 同样免安装、整个文件夹拷走即迁移
3. HardmacroCraft-source-0.1.0-fix9.zip（282KB）
   · 完整源码（src + tests + docs），开发者 / 自审用

【下载直链（qu.ax）】
· 单文件版   https://qu.ax/Gr12a
· 便携文件夹 https://qu.ax/fy42X
· 源码包     https://qu.ax/oiIq9

【⚠ 下载姿势很重要 —— 否则会得到「损坏的压缩包」】
qu.ax 开启了全站防盗链：直接用 IDM / wget / 某些下载器抓短链，
拿到的其实是一个 8KB 的网页（另存为 .zip 当然报损坏）。
正确做法（二选一）：
1. 【推荐】用浏览器打开上面的短链，在打开的页面里点「Download」按钮，
   浏览器会自动带上 Referer，正常下载真文件。
2. 或在浏览器地址栏直接访问下面的「文件直链」（同样自动带 Referer）：
   · 单文件版   https://qu.ax/x/Gr12a.zip
   · 便携文件夹 https://qu.ax/x/fy42X.zip
   · 源码包     https://qu.ax/x/oiIq9.zip

下载后先核对文件大小：
· SingleFile ≈ 62 MB（64882284 字节）
· Portable   ≈ 66 MB（69364932 字节）
· Source     ≈ 282 KB（288531 字节）
如果只有几 KB，说明抓到的是网页而不是文件，请按方法 1 重新下载。

【SHA1 校验（与 qu.ax 服务端返回一致）】
· Portable   6510a0fe4c77f00c4c6e9d6d6325b487692d204c
· SingleFile d3e058348a28af6eb8ad83040b187ab324676101
· Source     52153e1d06735b88dcb273c8f4a60c5bd6385c89
本地校验：certutil -hashfile 文件名 SHA1（Windows）或 sha1sum 文件名（Linux/macOS）

【fix9 更新内容】
· 下载页新增「愚人节版」独立分类（五分类对齐主流启动器：全部 / 正式版 /
  快照版 / 愚人节版 / 远古版），收录 2013-2026 历届共 10 个愚人节版本
  （Minecraft 2.0、3D Shareware、无限维度、毒土豆更新、合成矿、Herdcraft 等），
  每个版本附一句中文 / 英文简介，紫红色徽章与普通快照区分
  —— 愚人节版在 Mojang 清单里 type 与快照同为 snapshot，按已知 ID 识别，
  「快照版」分类下自动排除避免重复
· 全量文案检查：应用界面与日志不再出现其它启动器名称字样（含 README）
· 沿用 fix8 全部能力：默认 JVM 优化参数、自定义 JVM / 游戏参数、全屏启动等
· 沿用 fix7 全部能力：进度双行显示、启动/任务取消、Esc 返回等

【实测建议】
1. 下载页 → 点「愚人节版」→ 应列出 10 个版本，各带年份与简介徽章
2. 点「快照版」→ 列表不应再混入愚人节版本
3. 任意安装一个愚人节版（如 25w14craftmine）→ 正常启动游戏
4. 全应用界面与日志中不应出现「PCL / HMCL」字样

【已知限制】
· 微软正版登录需自备 Azure Client ID（设置页有获取引导，免费，约 3 分钟）
· CurseForge 模组搜索需自备免费 API Key（设置页可填，Modrinth 无需）
· 配置与游戏文件均保存在 exe 同目录（config/ 与 .minecraft/），删除即卸载

—— Hardmacro Craft Launcher · 0.1.0-fix9 · MIT License
