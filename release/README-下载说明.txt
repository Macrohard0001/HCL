Hardmacro Craft Launcher 0.1.1 下载说明（独立开源 Minecraft 启动器，MIT 协议）
开发者：Fox_Ryn（Macrohard0001） · 仓库：https://github.com/Macrohard0001/HCL
====================================================================

【三个文件怎么选】
1. HardmacroCraft-Launcher-0.1.1-SingleFile.zip（62MB，推荐大多数用户）
   · 解压后只有一个 HardmacroCraftLauncher.exe，双击即用
   · 自带 .NET 运行时（70MB 全自包含，已打包自检），无需安装任何依赖
2. HardmacroCraft-Launcher-0.1.1-Portable.zip（66MB）
   · 解压后是 HardmacroCraft 文件夹（含全部 DLL），启动略快
   · 内置「!运行说明.txt」；整个文件夹拷走即迁移
3. HardmacroCraft-source-0.1.1.zip（294KB）
   · 完整源码（src + tests + docs），开发者 / 自审用

【下载直链（qu.ax）】
· 单文件版   https://qu.ax/seKT9
· 便携文件夹 https://qu.ax/CjAAK
· 源码包     https://qu.ax/ZKNVt

【⚠ 下载姿势很重要 —— 否则会得到「损坏的压缩包」】
qu.ax 开启了全站防盗链：直接用 IDM / wget / 某些下载器抓短链，
拿到的其实是一个 8KB 的网页（另存为 .zip 当然报损坏）。
正确做法（二选一）：
1. 【推荐】用浏览器打开上面的短链，在打开的页面里点「Download」按钮，
   浏览器会自动带上 Referer，正常下载真文件。
2. 或在浏览器地址栏直接访问「文件直链」（同样自动带 Referer）：
   https://qu.ax/x/<短链ID>.zip

【⚠ 运行姿势同样重要 ——「提示需要 .NET 运行环境」的真相】
· 便携版：必须先解压整个文件夹再运行！在压缩软件里直接双击 exe，
  只会把 exe 单独解到临时目录、DLL 全部丢失，就会误报「需要安装 .NET」。
  包内已有「!运行说明.txt」，解压后第一眼就能看到。
· 单文件版：运行时已内嵌，任何位置双击即可；如杀毒软件拦截请加白名单。

【SHA1 校验（与 qu.ax 服务端返回一致）】
· Portable   4e9bc9994e0ad90bf14d2228221067361932346d
· SingleFile 3e9281843a687b12425cb0943ebe85324d226912
· Source     704143a2f758da87b2e1e70ef7d7271d06efe795
本地校验：certutil -hashfile 文件名 SHA1（Windows）或 sha1sum 文件名（Linux/macOS）

【0.1.1 更新内容】
· 多线程下载：设置页新增「下载线程数」（1-32，默认 8）；启动补全 / 资源
  预下载按线程数并发（已完整的文件自动跳过），首次安装 2800+ 资源文件明显提速
· 校验缓存：每个版本完整校验一次后直接快速启动，不再每次启动都重新
  补全全部游戏文件；怀疑文件损坏可在设置中开启「每次启动重新校验」
· 修复切换标签页丢进度：启动进度改为全局进度中枢跨页面回放，
  启动中切到别的页面再回来，进度 / 阶段 / 取消按钮完整保留
· 取消能力增强：等待安装闸门排队期间也能取消；任何页面实例都能触发取消
· 修正 README 中残留的历史版本号文案（v1.x 时代旧标记全部清理）

【实测建议】
1. 启动一个已装过的版本 → 日志应出现「游戏文件已校验过，快速启动（跳过补全）」
2. 删除某版本的 assets 后再启动 → 应看到「多线程预取」只补缺失文件
3. 启动下载过程中切到「版本」页再回主页 → 进度条 / 阶段行应原样恢复
4. 设置页 → 应有「下载线程数」（1-32）与「每次启动重新校验文件」两个新选项

【已知限制】
· 微软正版登录需自备 Azure Client ID（设置页有获取引导，免费，约 3 分钟）
· CurseForge 模组搜索需自备免费 API Key（设置页可填，Modrinth 无需）
· 配置与游戏文件均保存在 exe 同目录（config/ 与 .minecraft/），删除即卸载

—— Hardmacro Craft Launcher · 0.1.1 · Fox_Ryn (Macrohard0001) · MIT License
