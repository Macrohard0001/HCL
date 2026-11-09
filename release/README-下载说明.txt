Hardmacro Craft Launcher 0.1.4 下载说明（独立开源 Minecraft 启动器，MIT 协议）
开发者：Fox_Ryn（Macrohard0001） · 仓库：https://github.com/Macrohard0001/HCL
====================================================================

【⚠ v0.1.2 / v0.1.3 用户必读：本版为紧急修复版】
v0.1.2 / v0.1.3 的发布包在部分真机上启动即崩（双击后闪退或弹出异常窗口，
日志为 XamlParseException → TypeLoadException @ FindMimeFromData）。
根因：v0.1.2 起对自包含发布启用了 .NET 裁剪（PublishTrimmed partial）以进一步
减小体积，而 WPF 官方不支持裁剪，裁剪破坏了 WPF 加载内嵌图标资源所依赖的
互操作链路，主窗口 XAML 初始化阶段即崩溃。v0.1.4 已撤销裁剪并永久禁用。
如果你手里的 v0.1.2 / v0.1.3 能正常启动，属于「未被裁剪问题命中的环境」，
仍然建议更新到 v0.1.4 以避免潜在的隐性异常。

【三个文件怎么选】
1. HardmacroCraft-Launcher-v0.1.4-SingleFile.zip（65MB，推荐大多数用户）
   · 解压后只有一个 HardmacroCraftLauncher.exe，双击即用
   · 自带 .NET 运行时（自包含），无需安装任何依赖
2. HardmacroCraft-Launcher-v0.1.4-Portable.zip（69MB）
   · 解压后是文件夹（含全部 DLL），启动略快
   · 内置「!运行说明.txt」；整个文件夹拷走即迁移
3. HardmacroCraft-source-v0.1.4.zip（311KB）
   · 完整源码（src + tests + docs），开发者 / 自审用

【下载直链（qu.ax）】
· 单文件版   https://qu.ax/J2jLd
· 便携文件夹 https://qu.ax/Nz1BO
· 源码包     https://qu.ax/Z7RGX
· GitHub Release（无防盗链，可直连下载器）：
  https://github.com/Macrohard0001/HCL/releases/tag/v0.1.4

【⚠ 下载姿势很重要 —— 否则会得到「损坏的压缩包」】
qu.ax 开启了全站防盗链：直接用 IDM / wget / 某些下载器抓短链，
拿到的其实是一个 8KB 的网页（另存为 .zip 当然报损坏）。
正确做法（二选一）：
1. 【推荐】用浏览器打开上面的短链，在打开的页面里点「Download」按钮，
   浏览器会自动带上 Referer，正常下载真文件。或直接用 GitHub Release。
2. 或在浏览器地址栏直接访问「文件直链」（同样自动带 Referer）：
   https://qu.ax/x/<短链ID>.zip

【⚠ 运行姿势同样重要 ——「提示需要 .NET 运行环境」的真相】
· 便携版：必须先解压整个文件夹再运行！在压缩软件里直接双击 exe，
  只会把 exe 单独解到临时目录、DLL 全部丢失，就会误报「需要安装 .NET」。
  包内已有「!运行说明.txt」，解压后第一眼就能看到。
· 单文件版：运行时已内嵌，任何位置双击即可；如杀毒软件拦截请加白名单。

【SHA1 校验（与 qu.ax 服务端返回一致）】
· SingleFile 3def460a04e3a3173b9c3bc50601c7864718b186
· Portable   c2cc8671539ec0485e9def51b0bd20a97ffc893f
· Source     c3c0a60c6faf723d0e6533abc13ae833668ccaa6

====================================================================
【v0.1.4 更新内容】

■ 紧急修复：撤销 .NET 裁剪，修复启动即崩
· 移除 PublishTrimmed（partial 裁剪）：WPF 不支持裁剪（NETSDK1168），
  裁剪破坏了 WPF 加载内嵌图标资源依赖的互操作链路，导致主窗口
  XAML 初始化阶段 TypeLoadException 崩溃
· 单文件版体积由 40.2MB 回升至约 70MB（仍比未压缩的 163.9MB 小 57%），
  单文件压缩保留，自包含免安装 .NET 特性不变
· 教训记录：沙盒中的自动化测试运行在未裁剪的普通构建上，无法暴露
  publish 产物特有的裁剪问题，裁剪相关开关自此永久禁用

■ 保留 v0.1.3 全部特性
· 内存优化：重大操作后自动回收 / 空闲每 5 分钟定时清理（忙碌不干扰）/
  日志环形修剪（最近 500 行）/ 工作站并发 GC / 设置页内存开关与占用显示
· UI 对齐微软式 Fluent 风格：纯文字标题层级、8px 圆角、语义色徽章、
  悬停反馈、图标去彩色化
· 多线程下载（1-256 可调）、校验缓存、进度中枢跨页回放

■ 质量保障：321 项自动化测试通过 / 0 失败（1 项环境条件用例跳过）
====================================================================
