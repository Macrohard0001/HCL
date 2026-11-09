# Hardmacro Craft Launcher

**v0.1.5** · Hardmacro Craft Launcher · 一个独立的开源 Minecraft: Java Edition 启动器 · Windows 11 Fluent 设计

> **开发者：Fox_Ryn（Macrohard0001）** · 仓库：https://github.com/Macrohard0001/HCL · License: MIT

> **v0.1.5 更新说明**：修复模组加载器版本获取（重点 NeoForge）——自研 NeoForge 官方目录源：补齐 **1.20.1 专属 artifact（net/neoforged/forge，此前版本列表恒为空）**、支持 2026 新版本线（26.x）与愚人节快照特例映射、修复 beta 版本排序方向、版本列表带 10 分钟缓存，NeoForge 安装改为自构安装器直链（不再依赖 CmlLib 的 NeoForge 目录，1.20.1 可正常安装）并接上详细进度。模组页对齐 PCL：**单击搜索结果行直接进入模组详情页**（简介全文 / 图标 / 徽章 / 下载 / 打开网页，返回列表一键回），搜索时显示「正在搜索…」过渡层，空结果给出友好提示，平台 / 资源类型 / 加载器筛选会被记住（下次打开自动恢复）。**新增模组预设（一键装机单）**：勾选模组 →「保存预设」命名存档；换机器 / 换版本后点「预设安装」，按当前游戏版本与加载器自动匹配最新兼容版本批量下载并补齐依赖，预设存便携 config/mod-presets.json。账号体验对齐 PCL：主页三种登录入口（微软正版 / 第三方验证站 / 离线）合并为「账号管理」一个按钮的子页面；**头像全面对齐 PCL**——有皮肤显示皮肤脸部（像素风锐利缩放），无皮肤按 UUID 奇偶显示 Steve / Alex 默认脸（与游戏内一致），账号管理面板同步生效。354 项自动化测试全绿。

> **v0.1.4 更新说明**：紧急修复 v0.1.2 / v0.1.3 发布包在部分真机上启动即崩的问题（双击后闪退 / 弹异常窗口，日志为 XamlParseException → TypeLoadException @ FindMimeFromData）。根因是 v0.1.2 起对自包含发布启用了 .NET 裁剪（PublishTrimmed partial）以进一步减小体积——WPF 不支持裁剪（官方会报 NETSDK1168），裁剪破坏了 WPF 加载内嵌图标资源所依赖的互操作链路，导致主窗口 XAML 初始化阶段崩溃；沙盒中的 323 项测试运行在未裁剪的普通构建上，无法暴露该问题，特此记录并永久禁用裁剪。v0.1.4 撤销裁剪、仅保留单文件压缩：单文件版体积由 40.2MB 回升至约 70MB（仍较未压缩的 163.9MB 小 57%），自包含免安装 .NET 特性不变，便携版不受体积变化影响。其余功能与 v0.1.3 完全一致，v0.1.2 / v0.1.3 用户请务必更新。

> **v0.1.3 更新说明**：启动器内存优化（参考 PCL / HMCL 的内存管理思路）——版本安装 / 整合包导入 / 批量下载 / 游戏启动等重大操作结束后自动回收内存，空闲时段每 5 分钟定时清理（启动器忙碌时绝不干扰），显式声明工作站并发 GC；主页日志环形修剪（只保留最近 500 行，长会话不再撑爆内存）；设置页新增「启动器内存优化」开关（默认开）并显示当前内存占用。UI 对齐微软式 Fluent 风格：移除页面标题彩色强调条与卡片彩色图标芯片（更收敛的纯文字标题层级），卡片圆角统一为 Fluent 规范的 8px，任务中心状态徽章改用 Fluent 语义色（成功绿 / 失败红 / 强调蓝的 subtle 背景 + 彩色文字），快捷入口卡片新增悬停反馈，主页图标去彩色化。323 测试全绿。

> **v0.1.2 更新说明**：体积大幅瘦身——partial 裁剪 + 单文件压缩，自包含单文件 70.8MB → **40.2MB（-43%）**，免运行环境特性不变；修复下载断流重试后进度膨胀（服务器不支持断点续传时进度可超 100%）；修复 .hmc-part 满长时 Range 416 导致下载卡死；修复校验缓存戳文件名碰撞（极端路径/版本名下 A 的戳被 B 误用）；启动结束后切页回来不再丢失「已取消 / 启动失败 / 游戏运行中」终态文案；315 测试全绿。

> **v0.1.1 更新说明**：多线程下载（线程数可在设置页调整，1-256 默认 64，与 PCL 一致，启动补全 / 资源预下载全面提速）；校验缓存——每个版本完整校验一次后快速启动，不再每次启动都重新补全全部游戏文件（怀疑损坏可在设置中重新开启全量校验）；修复启动过程中切换标签页导致主页进度"丢失"（进度中枢跨页面回放，任何页面都能取消）；启动等待安装闸门期间也可取消；修正 README 中残留的历史版本号文案。

> **v0.1.0 更新说明**：下载页五分类对齐主流启动器（全部 / 正式版 / 快照版 / 愚人节版 / 远古版），收录 2013–2026 历届共 10 个愚人节版本（附年份与简介徽章）；全量文案清理；默认 JVM 优化参数、自定义 JVM / 游戏参数、全屏启动；进度双行显示、任务取消、Esc 返回。

![icon](src/HardmacroCraft/Assets/icon.png)

## ✨ 功能

| 功能 | 说明 |
|------|------|
| 🚀 一键启动 | 原版 / Forge / NeoForge / Fabric / Quilt 全支持，实时下载进度与游戏日志 |
| ☕ Java 自动管理 | 按 Minecraft 版本自动选择 / 下载 Java（Eclipse Temurin JRE，TUNA 镜像优先 + SHA-256 校验，安装到 exe 同目录 runtime/，便携可迁移）；1.17 → 16+、1.20.5+ → 21 自动匹配 |
| 📋 任务中心 | 所有下载 / 安装 / 补全文件任务的实时进度与历史 |
| 🧑‍🎨 皮肤头像 | 账号头像自动拉取皮肤头部贴图（正版 / 验证站），无皮肤时回退首字母 |
| 🔐 自动登录 | 第三方登录支持记住密码 + 自动登录：启动器打开后静默重登 |
| 🖥 分辨率设置 | 自定义游戏窗口分辨率（如 1280×720） |
| 📂 快捷目录 | 版本页一键打开存档 / mods / 资源包目录（尊重版本隔离）；Mod 页一键打开 mods 目录 |
| 🕹 最近启动 | 主页快捷启动区：最近玩过的版本一键重开 |
| 📃 版本浏览器 | 游戏下载页：分类筛选（全部/正式/快照/愚人节/远古）+ 搜索 + 类型徽章 + 发布日期 + 知名版本简介 + 已安装标识；草方块图标 |
| 👤 账号 | **微软正版登录（设备码流程）** + **第三方验证站（LittleSkin 等，authlib-injector，多角色选择 / 记住密码 / 自动登录）** + 离线模式；账号管理面板（多账号切换 / 删除 / 皮肤头像） |
| 📦 整合包 | Modrinth 整合包搜索安装 + 本地 `.mrpack` 导入，自动处理装载器依赖；结果行含分类 / 下载量 / 关注 / 更新时间 / 支持版本徽章 |
| 🌐 Mod 双平台 | **Modrinth + CurseForge** 双平台搜索下载；加载器 / 游戏版本过滤；数据包类型；CurseForge 免费 API Key 自助接入（console.curseforge.com，保存在本机 config/） |
| 🧲 批量安装 | Mod 页搜索结果可勾选，「下载已选」一键安装全部最新兼容版本；**CurseForge 必需依赖自动补齐**（与 Modrinth 同款 BFS 解析，relationType=3）；翻页加载不再丢勾选 |
| 🫥 启动行为 | 游戏启动后自动隐藏启动器（游戏退出时恢复）/ 游戏退出后自动关闭启动器（设置页可选） |
| 🚑 闪退修复 | 修复中文语言文件 10 个 key 重复定义导致的启动即崩（"Item has already been added"）；新增语言字典重复 key 自动检测测试防回归 |
| 🛟 启动防护 | 启动全程写 config/boot.log 逐步留痕（写不进时回退 %TEMP%\HardmacroCraft-boot.log）；config/ 与 .minecraft 目录创建失败不再静默闪退；非UI线程致命异常弹窗提示；settings.json 原子写入防损坏 |
| 🩹 崩溃报告定位 | 游戏非零退出时自动在日志中给出最新崩溃报告文件名；主页日志区一键打开日志目录 / crash-reports 目录 |
| 🧩 Mod 资源 | Modrinth / CurseForge 搜索下载 Mod / 资源包 / 光影包 / 数据包，自动放入对应版本目录；**版本选择对话框（release/beta/alpha 徽章）**；**必需依赖自动批量补齐**（Modrinth）；下载自动登记任务中心；本地管理列表跟随搜索类型切换（mods / 资源包 / 光影目录） |
| 🖱 滚轮滚动 | 全局滚轮兑底升级：最内层容器优先滚动（版本列表等内嵌列表可独立滚动），滚到边界自动接力外层页面 |
| 🌊 滚动动画 | 全局平滑滚动：逐帧指数逼近，连续滚动不断档，拖动滚动条自动让位 |
| 🗂 版本隔离 | 每个版本独立存档 / mods / 资源包，互不污染；assets 与 libraries 全局共享 |
| 🇨🇳 国内加速 | BMCLAPI 镜像重写全部 Mojang 下载域名，可一键开关；镜像失败自动回退官方源 |
| 🌗 深浅色 | 深色 / 浅色 / 跟随系统，切换立即生效；常显圆角滚动条 |
| 🌐 双语 | 简体中文 / English，运行时切换 |
| 💼 便携化 | **所有配置与账号保存在 exe 同目录 `config/`，不写 AppData、不写注册表**；另提供零外部写入的便携文件夹版 |
| 🪶 体积优化 | 自包含单文件约 40 MB（原 164 MB，-75%；partial 裁剪 + 压缩，自包含免运行环境特性不变） |

> **两种发行版**：
> - `HardmacroCraftLauncher.exe`（单文件）：.NET 运行时原生库首次运行会解压到系统临时目录 `%TEMP%\.net\`（.NET 官方行为，约 30MB，一次性）
> - `HardmacroCraftLauncher-Portable/`（文件夹版）：**完全零外部写入**，所有文件只在自身目录内，注重纯净选这个

## 📥 使用

1. 解压后运行 `HardmacroCraftLauncher.exe`（自包含运行时，无需安装 .NET）。
2. （可选）在 `设置` 页配置 Java 路径与内存；留空 Java 会自动检测。
3. 启动页选择或下载一个版本，点 **启动游戏**。

### 微软正版登录（需一次性配置 Azure Client ID）

登录采用微软官方设备码流程：启动器显示验证码 → 打开网页 → 粘贴验证码 → 自动完成。

1. 访问 [portal.azure.com](https://portal.azure.com) 并登录微软账号；
2. 搜索 **应用注册** → **新注册**：
   - 名称随意（如 `HardmacroCraft`）；
   - 受支持的账户类型选 **任何组织目录中的账户和个人 Microsoft 账户**；
   - 重定向 URI 平台选 **公共客户端/本机(移动和桌面)**，URI 填 `http://localhost`；
3. 注册后在 **概览** 页复制 **应用程序(客户端) ID**；
4. 在 **身份验证** 页面底部，将 **允许公共客户端流** 设为 **启用** 并保存；
5. 把 Client ID 粘贴到启动器 `设置 → Azure Client ID`，保存后即可使用微软登录。

> 登录凭据（refresh token）仅保存在本地 `config/accounts.json`，可在设置中删除。

### 第三方验证站登录（LittleSkin 等，无需 Azure 配置）

启动页点击 **第三方登录**：

1. 服务器地址填验证站域名（默认 `littleskin.cn`，启动器会自动发现其 Yggdrasil API Root）；
2. 输入你在该验证站注册的邮箱与密码，登录成功后选择版本即可启动；
3. 启动器会自动下载 `authlib-injector`（BMCLAPI 镜像优先，SHA-256 校验）并以 `-javaagent` 注入游戏，令牌失效时自动无感续期。

任意兼容 authlib-injector / Yggdrasil 协议的验证站均可使用。

## ✅ 核心功能验证（tests/HmCoreTest，121 项全部通过）

验证套件直接源码链接 `Services/`，在 CI 可真实运行：

- 离线 UUID v3 算法一致性、设置存取往返、版本隔离路径；
- BMCLAPI 真实清单（920 版本）、真实下载安装 b1.2_01 全链路、启动参数构造（隔离 / 会话注入）；
- `.mrpack` 解析（含 camelCase 字段）、坏包拒绝；
- LittleSkin API Root 自动发现、错误凭据拒绝、authlib-injector 下载 + SHA-256 校验 + 幂等；
- 微软设备码端点连通与错误处理、版本重命名 / 复制 / 删除；
- Java 版本映射（含 Forge 后缀名 / 损坏 json 兜底）、托管扫描与挑选、JRE 下载元数据；
- 任务中心（登记 / 进度夹紧 / 快照排序 / 50 条上限）；皮肤 textures base64 解析与 PNG 校验（真实网络）；
- CurseForge 接入（classId / 排序 / modLoaderType 映射、搜索与文件解析、API Key 请求头、CDN 回退地址规则）、Modrinth facets 过滤与必需依赖解析、兼容文件挑选（夹具回放）。

## 🏗 Java 自动管理

设置页可一键下载 Java 8 / 17 / 21（Eclipse Temurin JRE，Windows x64）：

- 下载地址自动从 Adoptium API 获取，**清华 TUNA 镜像优先、GitHub 官方兜底**，SHA-256 全文件校验；
- 安装到启动器同目录 `runtime/java-<主版本>/`，随目录迁移；
- 开启 **启动时自动选择** 后，按版本 json 的 `javaVersion.majorVersion`（缺失时按版本名启发式）自动挑选最合适的托管 Java；手动填写的 Java 路径始终优先。

## 🌐 Mod 双平台（Modrinth + CurseForge）

Mod 页左上角切换平台：

- **Modrinth**：无需任何 Key；支持加载器（Forge/Fabric/NeoForge/Quilt）与游戏版本联合过滤；
  下载时弹出版本选择对话框，必需依赖（如 Fabric API）**自动批量补齐**；
- **CurseForge**：全球最大 Mod 平台。需一次性配置免费 API Key——在
  [console.curseforge.com](https://console.curseforge.com) 注册后 **Apply for an API Key**，
  粘贴到 `设置 → CurseForge API Key`（仅保存在本机 config/settings.json）；
  官方 API 未返回直链时自动回退 forgecdn CDN 地址规则；
- 类型均支持 Mod / 资源包 / 光影包 / 数据包；版本选择行含 release/beta/alpha 徽章、
  游戏版本、加载器、日期与体积。

## 📱 手机端开发（准备中）

见 [docs/MOBILE.md](docs/MOBILE.md)：参考 FCL / PojavLauncher / Boat 的技术路线对比与选型
（**Android 原生 + Pojav 式 JVM 移植栈，自研实现，不复制任何 GPL 代码**）、
跨平台共享核心 `src/HmCore`（net8.0，桌面端同一份代码直接编译）、M0–M4 阶段计划与风险清单。
**M1 已起步**：新增 `src/HmCli` 无头验证工具，已在 Linux 真实环境跑通核心链路
（便携配置 / 离线 UUID / 兼容文件挑选 / BMCLAPI 清单拉取 921 版本），`dotnet run --project src/HmCli -- selftest`。

## 📁 目录结构（便携设计）

```
HardmacroCraft/
├─ HardmacroCraft.exe      ← 主程序
├─ config/                 ← 自动生成：settings.json / accounts.json
├─ runtime/                ← Java 自动管理（java-8 / java-17 / java-21）
└─ .minecraft/             ← 默认游戏目录（可在设置中更改）
   ├─ versions/            ← 版本隔离开启时，versions/<id>/ 内含该版本独立存档与 mods
   ├─ assets/  libraries/  ← 全局共享，节省磁盘
   └─ ...
```

整个文件夹拷贝到任意位置（如 U 盘）即可迁移，不留垃圾。

## 🛠 从源码构建

```bash
git clone <repo-url> && cd HardmacroCraft
dotnet publish src/HardmacroCraft -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# csproj 已内置 EnableCompressionInSingleFile（164MB → 70MB）
```

> 体积说明：WPF 不支持 `PublishTrimmed` 裁剪；R2R 与 ICU 在本配置下本就未启用/未内嵌，因此单文件压缩是安全且唯一有效的体积手段。

要求 .NET 10 SDK（`EnableWindowsTargeting` 已启用，Linux/macOS 亦可交叉编译 Windows 版）。

共享核心库（手机端 / 跨平台复用）：`dotnet build src/HmCore`（net8.0，无需 Windows）。
内核验证 CLI：`dotnet run --project src/HmCli -- selftest`（亦可在 Termux 运行）。

## ⚖️ 许可证

本项目以 **MIT License** 发布（见 LICENSE）。这是独立项目，未使用、未分发任何第三方限制性许可启动器的代码或资源。

### 第三方组件

- [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) — MIT
- [CmlLib.Core.Installer.Forge](https://github.com/CmlLib/CmlLib.Core.Installer.Forge) — MIT
- [authlib-injector](https://github.com/yushijinhun/authlib-injector) — 运行时按需下载（非再分发）
- [WPF-UI](https://wpfui.lepo.co/) — MIT
- [Modrinth API](https://docs.modrinth.com/) — 开放 API
- [CurseForge for Studios API](https://docs.curseforge.com/) — 需用户自有免费 API Key（启动器不内置、不再分发）
- [BMCLAPI](https://bmclapi2.bangbang93.com/) — 镜像服务（请合理使用）

*Minecraft 是 Mojang Studios 的商标，本项目与 Mojang / Microsoft 无关，请支持正版。*
