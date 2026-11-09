Hardmacro Craft Launcher 0.1.3 下载说明（独立开源 Minecraft 启动器，MIT 协议）
开发者：Fox_Ryn（Macrohard0001） · 仓库：https://github.com/Macrohard0001/HCL
====================================================================

【三个文件怎么选】
1. HardmacroCraft-Launcher-0.1.3-SingleFile.zip（33MB，推荐大多数用户）
   · 解压后只有一个 HardmacroCraftLauncher.exe，双击即用
   · 自带 .NET 运行时（自包含，已打包自检），无需安装任何依赖
2. HardmacroCraft-Launcher-0.1.3-Portable.zip（37MB）
   · 解压后是 HardmacroCraft 文件夹（含全部 DLL），启动略快
   · 内置「!运行说明.txt」；整个文件夹拷走即迁移
3. HardmacroCraft-source-0.1.3.zip（304KB）
   · 完整源码（src + tests + docs），开发者 / 自审用

【下载直链（qu.ax）】
· 单文件版   https://qu.ax/UBRms
· 便携文件夹 https://qu.ax/Ilxyr
· 源码包     https://qu.ax/PHG0I
· GitHub Release（无防盗链，可直连下载器）：
  https://github.com/Macrohard0001/HCL/releases/tag/v0.1.3

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
· Portable   ac5c7e789b3cf6eee8379531b92cd44484d17e5d
· SingleFile ac411765a57d1652ca3c9a09efe73953e3acbb13
· Source     22c892f4eea3271273c042e0486fba912beb1056

====================================================================
【v0.1.3 更新内容】

■ 内存优化（参考 PCL / HMCL 的内存管理思路）
· 重大操作（版本安装 / 整合包导入 / 批量下载 / 游戏启动）结束后，
  自动回收下载缓冲与临时对象占用的内存，并把物理内存还给系统
· 空闲时段每 5 分钟定时清理（启动器忙碌时绝不干扰，不影响下载 / 启动）
· 主页日志环形修剪：只保留最近 500 行，长会话不再撑爆内存
· 显式声明工作站并发 GC（桌面应用最优策略）
· 设置页新增「启动器内存优化」开关（默认开）+ 当前内存占用显示

■ UI 对齐微软式 Fluent 风格
· 移除页面标题彩色强调条与卡片彩色图标芯片，改为更收敛的纯文字标题
· 卡片圆角统一为 Fluent 规范的 8px
· 任务中心状态徽章改用 Fluent 语义色（成功绿 / 失败红 / 强调蓝）
· 快捷入口卡片新增悬停反馈；主页图标去彩色化

■ 质量保障：323 项自动化测试全绿（新增 8 项内存优化用例）
