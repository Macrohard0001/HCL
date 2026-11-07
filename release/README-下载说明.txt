Hardmacro Craft Launcher 0.1.0 下载说明（独立开源 Minecraft 启动器，MIT 协议）
开发者：Fox_Ryn（Macrohard0001） · 仓库：https://github.com/Macrohard0001/HCL
====================================================================

【三个文件怎么选】
1. HardmacroCraft-Launcher-0.1.0-SingleFile.zip（62MB，推荐大多数用户）
   · 解压后只有一个 HardmacroCraftLauncher.exe，双击即用
   · 自带 .NET 运行时，无需安装任何依赖
2. HardmacroCraft-Launcher-0.1.0-Portable.zip（66MB）
   · 解压后是文件夹版（含全部 DLL），启动略快
   · 同样免安装、整个文件夹拷走即迁移
3. HardmacroCraft-source-0.1.0.zip（283KB）
   · 完整源码（src + tests + docs），开发者 / 自审用

【下载直链（qu.ax）】
· 单文件版   https://qu.ax/cQqx5
· 便携文件夹 https://qu.ax/UQZch
· 源码包     https://qu.ax/TsBAm

【⚠ 下载姿势很重要 —— 否则会得到「损坏的压缩包」】
qu.ax 开启了全站防盗链：直接用 IDM / wget / 某些下载器抓短链，
拿到的其实是一个 8KB 的网页（另存为 .zip 当然报损坏）。
正确做法（二选一）：
1. 【推荐】用浏览器打开上面的短链，在打开的页面里点「Download」按钮，
   浏览器会自动带上 Referer，正常下载真文件。
2. 或在浏览器地址栏直接访问下面的「文件直链」（同样自动带 Referer）：
   · 单文件版   https://qu.ax/x/cQqx5.zip
   · 便携文件夹 https://qu.ax/x/UQZch.zip
   · 源码包     https://qu.ax/x/TsBAm.zip

下载后先核对文件大小：
· SingleFile ≈ 62 MB（64882589 字节）
· Portable   ≈ 66 MB（69365208 字节）
· Source     ≈ 283 KB（292890 字节）
如果只有几 KB，说明抓到的是网页而不是文件，请按方法 1 重新下载。

【SHA1 校验（与 qu.ax 服务端返回一致）】
· Portable   9e27df1b209289e0942e14bd38c3de9529328564
· SingleFile d040df79935d91a003204d48e0e4d820ace81dc8
· Source     b0f1ee558d39de812267e07e8b32c033ef2cf455
本地校验：certutil -hashfile 文件名 SHA1（Windows）或 sha1sum 文件名（Linux/macOS）

【0.1.0 更新内容】
· 登录系统全面检查（重点修复离线登录用户名填写问题）：
  新增专用输入窗口，实时校验 + 非法输入就地红字提示（3-16 位，仅字母 /
  数字 / 下划线，与 Java 版官方规则一致）；此前非法输入会被静默丢弃，
  点确定毫无反应，看起来像按钮坏了
  —— 中文名 / 带符号 / 超长用户名现在都会明确告诉你为什么不行
· 离线账号重登同名账号不再丢失自定义皮肤设置
· 下载页五分类（全部 / 正式版 / 快照版 / 愚人节版 / 远古版），
  收录 2013-2026 历届共 10 个愚人节版本，附年份与简介徽章
· 全量文案检查：应用界面与日志不再出现其它启动器名称字样
· 默认 JVM 优化参数、自定义 JVM / 游戏参数、全屏启动
· 进度双行显示、启动 / 任务取消、Esc 返回

【实测建议】
1. 离线登录 → 输入中文名或带符号名字 → 应立即看到红字原因提示
2. 输入合法名（如 Steve123）→ 确定按钮亮起 → 正常登录并启动游戏
3. 下载页 → 点「愚人节版」→ 应列出 10 个版本，各带年份与简介徽章
4. 任意安装一个愚人节版（如 25w14craftmine）→ 正常启动游戏

【已知限制】
· 微软正版登录需自备 Azure Client ID（设置页有获取引导，免费，约 3 分钟）
· CurseForge 模组搜索需自备免费 API Key（设置页可填，Modrinth 无需）
· 配置与游戏文件均保存在 exe 同目录（config/ 与 .minecraft/），删除即卸载

—— Hardmacro Craft Launcher · 0.1.0 · Fox_Ryn (Macrohard0001) · MIT License
