Hardmacro Craft Launcher 0.1.2 下载说明（独立开源 Minecraft 启动器，MIT 协议）
开发者：Fox_Ryn（Macrohard0001） · 仓库：https://github.com/Macrohard0001/HCL
====================================================================

【三个文件怎么选】
1. HardmacroCraft-Launcher-0.1.2-SingleFile.zip（33MB，推荐大多数用户）
   · 解压后只有一个 HardmacroCraftLauncher.exe，双击即用
   · 自带 .NET 运行时（40MB 全自包含，已打包自检），无需安装任何依赖
2. HardmacroCraft-Launcher-0.1.2-Portable.zip（37MB）
   · 解压后是 HardmacroCraft 文件夹（含全部 DLL），启动略快
   · 内置「!运行说明.txt」；整个文件夹拷走即迁移
3. HardmacroCraft-source-0.1.2.zip（299KB）
   · 完整源码（src + tests + docs），开发者 / 自审用

【下载直链（qu.ax）】
· 单文件版   https://qu.ax/mweRq
· 便携文件夹 https://qu.ax/ueZMW
· 源码包     https://qu.ax/JdOXA
· GitHub Release（无防盗链，可直连下载器）：
  https://github.com/Macrohard0001/HCL/releases/tag/v0.1.2

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
· Portable   b7e7ea8fb01fb75f631fda8b4958e33abef8dbee
· SingleFile e050e5a617ba7d54f5f644487be7eb7f3dd26ee1
· Source     5418e8632ced2185c86c065f5dfa68f1d11cb134
本地校验：certutil -hashfile 文件名 SHA1（Windows）或 sha1sum 文件名（Linux/macOS）

【0.1.2 更新内容】
· 体积大幅瘦身：partial 裁剪 + 单文件压缩，自包含单文件 70.8MB → 40.2MB
  （-43%），发行 zip 62MB → 33MB；免运行环境特性不变
  （只裁剪 .NET 基础库未用到的部分，WPF / 启动器 / CmlLib 程序集完整保留）
· 修复下载断流重试后进度膨胀：服务器不支持断点续传时从头重下，
  此前已计入的字节不回退，进度可超过 100%
· 修复 .hmc-part 临时文件满长时 Range 请求 416 导致重试 3 次同因失败（下载卡死）
· 修复校验缓存戳文件名碰撞：极端路径 / 版本名（空格与下划线、超长截断）
  下 A 版本的戳可能被 B 版本误用导致跳过校验；现加指纹后缀彻底消除
· 启动结束后切页回来不再丢失「已取消 / 启动失败 / 游戏运行中」终态文案
· 315 测试全绿（新增第 25 节 10 项：断流回退 / 416 / 碰撞 / 终态回放）

【实测建议】
1. 下载体积应约 33MB（单文件）/ 37MB（便携），比 0.1.1 小一半左右
2. 启动一个已装过的版本 → 日志应出现「游戏文件已校验过，快速启动（跳过补全）」
3. 断网拖动下载进度条场景：启动下载中切到「版本」页再回主页 → 进度原样恢复
4. 设置页 → 下载线程数（1-256，默认 64）与「每次启动重新校验文件」两个选项仍在

【已知限制】
· 微软正版登录需自备 Azure Client ID（设置页有获取引导，免费，约 3 分钟）
· CurseForge 模组搜索需自备免费 API Key（设置页可填，Modrinth 无需）
· 配置与游戏文件均保存在 exe 同目录（config/ 与 .minecraft/），删除即卸载

—— Hardmacro Craft Launcher · 0.1.2 · Fox_Ryn (Macrohard0001) · MIT License
