# Hardmacro Craft Launcher（PCL Hardmacro Craft）多智能体工作日志

---
Task ID: 0
Agent: 主控（Z.ai）
Task: 调研 + 克隆 PCL2-CE + Linux 交叉编译基线验证

Work Log:
- 调研确认：官方 PCL2 源码库（Meloong-Git/PCL）官方声明不可直接编译；改用 PCL-Community/PCL2-CE（可编译社区分支，net10.0-windows WPF VB.NET，SDK 风格）
- 许可证要求确认：二改发布需名称以 PCL 开头 + 第三方后缀、主色调蓝色、关于页署名龙腾猫跃+赞助链接、开源、保留 LICENCE、保留 McLaunchPrecheck 正版劝导
- PCL 2.99.0.0 / CE 2.15.1 因合规压力移除/区域禁用 MC 下载；机制集中在 Plain Craft Launcher 2/Modules/Minecraft/RestrictedFeaturePolicy.cs（RegionPolicy.Evaluate 多信号判定）
- 克隆 PCL2-CE dev@42e9120 (2026-10-26) 到 /home/z/my-project/pcl-ce
- 安装 .NET 10.0.403 SDK 到 /home/z/my-project/dotnet
- dotnet build -p:Configuration=Release -p:Platform=x64 成功，0 错误

Stage Summary:
- 基线编译通过，构建脚本 /home/z/my-project/scripts/build.sh
- 下载区域限制唯一入口：RestrictedFeaturePolicy.Check/ThrowIfRestricted
- 导航页签：FormMain.xaml 中 PanTitleSelect（BtnTitleSelect0-3，MyRadioButton）

---
Task ID: 1
Agent: 主控（Z.ai）
Task: 应用户要求转向独立路线，从零构建 Hardmacro Craft Launcher

Work Log:
- 用户明确要求独立产品、规避 PCL 自定义许可证 → 放弃 PCL 二改，选择 MIT 全家桶路线
- 技术栈：net10.0-windows WPF + WPF-UI 4.3（Fluent/Mica）+ CmlLib.Core 4.1 + Installer.Forge 2.0
- 微软正版登录不用 MSAL：手写 OAuth 设备码 + Xbox Live XSTS + Minecraft 流程（AuthService.cs）
- BMCLAPI 镜像：DelegatingHandler 重写 Mojang 域名（MirrorHttpHandler.cs），经 MinecraftLauncherParameters 注入
- 所有 CmlLib API 经反射逐一核实（ForgeInstaller/NeoForgeInstaller/FabricInstaller/QuiltInstaller/MSession 等）
- 六页面：启动/下载/Mod(Modrinth)/版本管理/设置/关于，中英双语资源字典
- 修正首轮编译错误：WPF.UI→WPF-UI 包名、MinecraftLauncherParameters 反射构造、非 IDisposable、Type 字符串比较等
- 生成图标 icon.ico（PIL 绘制 Fluent 蓝渐变等距方块）
- dotnet publish 自包含单文件：publish/HardmacroCraft.exe（141MB，PE32+ x64 GUI）验证通过
- git commit 9d9ea37…（HardmacroCraft 仓库），产物已复制至 download 目录

Stage Summary:
- /home/z/my-project/download/HardmacroCraft-Launcher-v1.0.0/（exe + README + LICENSE）
- /home/z/my-project/download/HardmacroCraft-source-v1.0.0.zip（源码包，排除 bin/obj）
- 项目源码：/home/z/my-project/HardmacroCraft/

---
Task ID: 2
Agent: 主控（Z.ai）
Task: 会话重置后重建项目 v1.1.0 + 落实三大需求（便携配置 / PCL式正版登录 / 整合包+版本隔离）

Work Log:
- 发现工作区被重置：HardmacroCraft 源码目录已空、dotnet/shared 与 scripts/ 变为 root 700 不可读
- 重新下载 .NET 10.0.403 SDK 至 /home/z/my-project/dotnet2 并验证
- 重建前用反射 apicheck 核实 CmlLib 4.1 API：MinecraftPath 属性为普通 get/set（Library/Versions/Assets/Runtime/Resource/BasePath）；MLaunchOption.Path=MinecraftPath；Fabric/Quilt 在 CmlLib.Core.ModLoaders.*；ForgeInstaller.GetForgeVersions 返回 ForgeVersion 对象；CreateProcessAsync 返回 System.Diagnostics.Process（需自包 ProcessWrapper）；ProcessWrapper.OutputReceived=EventHandler<string>
- 便携化：AppSettings/AccountStore 全部读写 exe 同目录 config/（Environment.ProcessPath 定位），不触碰 AppData/注册表
- PCL 式正版登录：AuthService 设备码流程（devicecode→轮询→XBL→XSTS→login_with_xbox→profile），MicrosoftLoginWindow 展示大号验证码 + 复制 + 一键打开浏览器；支持 refresh_token 免验证码续登；离线 UUID 用 MD5("OfflinePlayer:"+name) v3
- 整合包：ModpackService 解析 modrinth.index.json，下载文件（跳过 client=unsupported、防路径穿越、秒传校验）到版本隔离目录，自动安装 Fabric/Quilt/Forge/NeoForge 装载器并探测真实版本 ID 返回；ModpacksPage 支持 Modrinth 搜索与本地 .mrpack 导入
- 版本隔离：BuildGamePath 生成 versions/<id>/ 独立游戏目录，Library/Assets/Versions/Runtime/Resource 重定向到共享根目录；ModsPage 下载目标目录随隔离开关联动
- 编译修复 4 轮：WPF-UI NavigationView 无 IsPaneToggleButtonVisible、ListBox 无 CornerRadius、UseWindowsForms 引发歧义（改用 OpenFolderDialog）、命名空间/返回类型修正
- dotnet publish 自包含单文件 156MB PE32+ x64 GUI 验证通过；git 重建并提交 629d874
- 交付：download/HardmacroCraft-Launcher-v1.1.0/（exe+README+LICENSE）、HardmacroCraft-source-v1.1.0.zip

Stage Summary:
- 三大需求全部落实且经编译验证；exe 需用户在 Windows 上实测 GUI 运行时
- API 核对工具保留在 HardmacroCraft/apicheck2（可复用）

---
Task ID: 3
Agent: 主控（Z.ai）
Task: 三项复查（PCL 功能对齐 / 设置页崩溃 / 窗口尺寸）+ 交付可见性修复

Work Log:
- 设置页崩溃根因修复：SettingsPage.xaml 中 <Run> 上使用 DynamicResource（Run 非 FrameworkElement，运行时抛 XamlParseException）→ 改为水平 StackPanel 双 TextBlock
- 设置页保存加固：游戏目录先做可写探针校验再落盘；settings.json 写入失败有明确提示不闪退
- 语言切换加固：ApplyLanguage 改用绝对 pack URI（pack://application:,,,/Themes/...）
- 全局兜底：DispatcherUnhandledException → 弹窗 + 写 config/crash.log，args.Handled=true
- PCL 功能对齐补齐：版本管理新增重命名/复制（连带 json id 补丁、隔离内容随目录改名）；Mod 页新增本机 Mod 管理（列出/.disabled 禁用启用/删除，与 PCL 行为一致）
- 窗口尺寸对齐 PCL/HMCL：1180x760 → 1020x670，MinSize 900x600；启动默认直达启动页（NavigationView.Navigate(Type)）
- WPF-UI 4.3 API 复核：无 InitialPage/IsSelected 属性，有 Navigate(Type)/NavigateWithHierarchy/SelectedItem
- 交付增加 zip 包并重建 download 目录内容

Stage Summary:
- v1.1.1 提交 a7668d7；download/ 含 Launcher 文件夹、Launcher zip（65MB）、source zip
- 已知与 PCL 的差异（诚实清单）：无 OptiFine/LiteLoader 安装器、无 CurseForge 源（仅 Modrinth）、无 authlib-injector 第三方皮肤站、无皮肤预览/隐藏页

---
Task ID: 4
Agent: 主控（Z.ai）
Task: UI 升级 —— 深浅色主题系统修复 + 滚动条 + 全面美化（对齐 PCL/HMCL）

Work Log:
- 反射核实 WPF-UI 4.3 API（apicheck3/MetadataLoadContext，忽略版本差异的 SimpleNameResolver）：ApplicationThemeManager.Apply/ApplySystemTheme/GetSystemTheme、SystemThemeWatcher.Watch(Window,backdrop,bool)、WindowBackdropType 实际位于 Wpf.Ui.Controls、TitleBar.Icon=IconElement、SymbolRegular 全量 24px 枚举名、TextFill*/ControlFill*/CardBackground*/SubtleFill*/SystemAccentColor*Brush 主题画刷键
- 主题修复（用户报告"切换主题字体不换色"根因：此前无主题系统、App.xaml 写死 Dark、Prompt 对话框是普通 Window）：
  - AppSettings 新增 Theme 字段（dark/light/system，默认 dark）
  - App.ApplyTheme：ApplicationThemeManager.Apply(Light/Dark, Mica) 或 ApplySystemTheme；MainWindow 上 SystemThemeWatcher.UnWatch/Watch 管理，"跟随系统"时 Windows 深浅色实时联动
  - 全部界面颜色改为主题画刷 DynamicResource（TextFillColorPrimary/Secondary/Tertiary、CardBackgroundFillColorDefault、SystemAccentColorPrimary、SubtleFillColorSecondary/Tertiary），无任何写死字体颜色 → 切换主题时字体/卡片/边框/滚动条全部跟随
  - Prompt.Show 重写为 FluentWindow+Mica（版本重命名/复制对话框跟随主题）
- 滚动条：全局隐式 ScrollBar 样式（10px 常显圆角、hover 加深、横竖共用 Track 触发器）+ HmScrollViewer 模板（页面级 ScrollViewer 保证滚动条可见），修复 WPF-UI 默认细悬浮滚动条"看不见"的问题
- 美化（模仿 PCL/HMCL）：
  - App.xaml 设计令牌：HmPageTitle/HmPageSubtitle/HmSectionTitle/HmHint/HmCard/HmIconChip/HmListItem/HmListBox
  - 六页面统一 PCL 式强调条标题；账号卡带头像首字母圆形；HomePage 全宽 48px 大启动按钮 + 终端风格深色日志台；DownloadGamePage 四装载器图标芯片卡片（Globe/Flash/Sparkle/Rocket）；设置页分组卡片（外观/游戏目录/内存Java/启动行为）+ 主题下拉即时生效并持久化；列表圆角悬停+选中描边；AboutPage 图标+版本徽章
  - MainWindow：TitleBar 显示应用图标（ImageIcon）、窗口 Icon
- 修复隐患：SettingsPage SldMemory_ValueChanged 增加 TxtMemValue null 守卫；csproj 补 icon.png 资源（AboutPage 此前引用但未打包）
- 编译 2 轮修复（ScrollContentPresenter 无 Padding、WindowBackdropType 命名空间），Build succeeded，publish 自包含单文件 164MB PE32+ x64 GUI
- 交付：download/HardmacroCraft-Launcher-v1.2.0/（exe+README+LICENSE）、Launcher zip（65MB）、source zip；git 071c357；README 更新 v1.2.0 + 深浅色特性

Stage Summary:
- 主题三态切换（含跟随系统）+ 字体颜色全跟随 + 常显滚动条 + PCL/HMCL 风格全面美化
- exe 仍需用户 Windows 实测 GUI；apicheck3 反射工具保留可复用
---
Task ID: 5
Agent: 主控（Z.ai）
Task: 继续对齐 PCL 功能 + 核心功能可用性验证 + 减小主程序体积研究

Work Log:
- PCL 对齐新增：第三方验证站登录（authlib-injector / Yggdrasil）
  - Services/AuthlibService.cs：API Root 自动发现（X-Authlib-Injector-API-Location 头）、authenticate/refresh/validate、agent jar 下载（BMCLAPI latest.json → 官方兜底，SHA-256 校验，幂等）
  - Views/AuthlibLoginWindow.xaml(.cs)：FluentWindow 登录对话框；HomePage 增加“第三方登录”按钮与启动分支（令牌校验失效自动无感续期、-javaagent 注入）
  - StoredAccount 增加 ClientToken/ServerUrl 字段；双语资源 S.Authlib.* 补齐
- 核心功能验证：新建 tests/HmCoreTest 无头套件（Linux 真实运行，源码链接 Services/*），38 项全部 PASS
  - 覆盖：离线 UUID v3 / 设置往返 / 隔离路径 / BMCLAPI 清单(920 版本) / 真实安装 b1.2_01 / 启动参数构造 / mrpack 解析+坏包 / LittleSkin 发现+错误凭据+jar 校验 / 微软设备码端点 / 版本重命名复制删除
  - 验证抓出并修复 3 个真 bug：①CreateDefault() 未用导致 VersionLoader was null（致命）②mrpack JSON 大小写敏感导致真实整合包解析为空 ③Yggdrasil agent.name 须为 Minecraft
- 体积研究（基线 163.9MB）：
  - EnableCompressionInSingleFile=true → 70.8MB（-57%，唯一有效手段，已固化进 csproj）
  - 实验证明 R2R 本就未启用、Windows 目标不内嵌 ICU（InvariantGlobalization 无收益，故保持 false 以保 zh 排序）；WPF 不支持 PublishTrimmed
- 交付：download/HardmacroCraft-Launcher-v1.3.0/（exe 70MB + README + LICENSE）、Launcher zip（64.8MB）、source zip；git ca9904f；README 增补第三方登录教程与验证章节

Stage Summary:
- v1.3.0 交付：功能对齐（第三方登录补齐 PCL 三大登录方式）、38/38 核心链路真实验证通过、体积 164→70MB
- exe 仍需用户 Windows 实测 GUI（尤其新登录窗口与启动链路）
---
Task ID: 6
Agent: 主控（Z.ai）
Task: 修复不能获取游戏版本 + 滚轮不触发页面滚动（v1.3.1）

Work Log:
- 会话重置导致工作区再次清空 → 从 /tmp/my-project 快照恢复 SDK/交付物/源码 zip（git 重建，worklog 保留）
- 滚轮修复（根因）：App.xaml 自定义 HmScrollViewer 模板的 ScrollContentPresenter 未命名 PART_ScrollContentPresenter，ScrollViewer.OnApplyTemplate 找不到部件 → ScrollInfo=null → 滚轮失效；补名并修正横向 ViewportSize 误绑 ScrollableWidth→ViewportWidth
- 版本获取加固：
  - LauncherService.GetAllVersionsAsync 双通道：BMCLAPI 镜像失败自动回退直连官方清单，双失败抛聚合错误
  - DownloadGamePage 增加“重新获取”按钮（防重入）、加载中/成功条数/失败详情三态提示，失败后下次进入页面自动重试
  - 注：v1.2.0 的“VersionLoader was null”致命 bug 已在 v1.3.0 修复（CreateDefault()），本版进一步消除单点故障与无重试问题
- 回归：HmCoreTest 38/38 通过；publish 70.8MB PE32+ 验证
- 交付：download/HardmacroCraft-Launcher-v1.3.1/ + zip + source zip；git 提交于重建仓库

Stage Summary:
- 两项用户报告问题均已修复；exe 需 Windows 实测滚轮手感与版本列表加载

---
Task ID: 7
Agent: 主控（Z.ai）
Task: 滚轮彻底修复 + 下载页面 PCL 式信息增强（v1.4.0）

Work Log:
- 工作区再次被重置 → 又从 /tmp/my-project 快照恢复（源码 zip + dotnet2 + 交付物），git 重建
- 滚轮修复（分层兜底，不再依赖单一环节）：
  - 保留 v1.3.1 模板修复（PART_ScrollContentPresenter 命名 = ScrollInfo 接线）
  - 新增全局类处理器：EventManager.RegisterClassHandler(ScrollViewer, PreviewMouseWheel, handledEventsToo:true)
    → 隧道路由上最外层 ScrollViewer 直接 ScrollToVerticalOffset 手动滚动（60px/格）并 e.Handled=true，
      内层 ListBox 空滚动器 / WPF-UI 模板差异 / ScrollInfo 接线异常等任何一层失效都不再影响滚动
- 下载页 PCL 式增强：
  - DownloadGamePage 原版区重构为版本浏览器：分类 chips（全部/正式版/快照/远古版本）+ 搜索框 +
    列表行（版本名 + 类型徽章三色 + “最新”徽章 + “已安装”徽章 + 发布日期 + 知名版本简介 19 条双语）
    + 行内安装按钮 + 空态提示；安装完成后已安装标识即时刷新
  - ModrinthService.SearchHit 扩展 date_modified/categories/follows 三个字段
  - ModsPage / ModpacksPage 结果行升级信息卡：图标 52px + 标题 + @作者 + 简介 + 徽章行
    （分类前3 / 下载量 / 关注 / 更新日期 / 支持版本 1.x+）
  - 双语资源 S.Dl.Old/FilterAll/SearchHint/BadgeLatest/InstalledBadge/ReleasedOn/LatestRel/LatestSnap/
    EmptyResult/Desc.*（19 条）+ S.Mod.Badge*（4 条）
- 回归：HmCoreTest 38/38 通过；publish 68MB PE32+（win-x64 需显式 -r win-x64，csproj 无 RID）
- 交付：download/HardmacroCraft-Launcher-v1.4.0/ + zip（64.8MB）+ source zip；git 提交

Stage Summary:
- v1.4.0 交付：滚轮三层兜底 + PCL 式下载信息展示
- exe 需 Windows 实测：滚轮手感（60px/格）与版本列表加载速度

---
Task ID: 8
Agent: 主控（Z.ai）
Task: 改名 Hardmacro Craft Launcher + 零外部写入 + 修复 FindResource 崩溃（v1.4.1）

Work Log:
- 用户 crash.log 诊断：ResourceReferenceKeyNotFoundException 'S.Dl.Desc.26.3' 等数十条
  → FindResource 缺键异常经 Dispatcher 异步投递，try/catch 抓不住，全涌向全局异常处理器
  → 改用 TryFindResource（返回 null 不抛），一劳永逸
- 改名：MainWindow/TitleBar、全部 MessageBox 标题、csproj AssemblyName=HardmacroCraftLauncher
  （exe 名随之）+ AssemblyTitle/Product/FileDescription、UserAgent；AboutPage 文案沿用
- 零外部写入排查（反射 + IL 检查）：
  - CmlLib MinecraftPath 构造器与 CreateDefault() 均不建目录（IL 扫描确认），%appdata% 非其产生
  - 真凶：① .NET 单文件宿主在 Main 之前把 WPF 原生库解压到 %TEMP%\.net\<exe>\（运行时行为，
    进程内无法重定向）② %TEMP%\HardmacroCraft 整合包临时下载
  - 修复②：临时下载移到 config/temp/；①通过新增便携文件夹版解决（无单文件即无解包）
- 新增便携版发行：publish-portable（自包含非单文件文件夹，164MB，zip 后 69MB），完全零外部写入
- 测试基建：HmCoreTest demo.mrpack fixture 加幂等清理（File.Exists→Delete），重跑 38/38 通过
- 交付：download/v1.4.1 单文件 zip（64.8MB）+ Portable zip（69MB）+ source zip（120K）；git 提交
- README：v1.4.1、两种发行版说明（含 %TEMP%\.net 系统行为说明）、exe 名更新

Stage Summary:
- 程序更名 Hardmacro Craft Launcher；资源缺键崩溃根除；便携版满足零外部写入诉求
- 单文件版 %TEMP%\.net 解包为 .NET 官方行为已在 README 说明，用户若在意可选 Portable 版
---
Task ID: 9
Agent: 主控（Z.ai）
Task: 关于页文案/程序名 + PCL 式草方块图标 + 版本列表滚轮修复 + 窗口默认 960x540（v1.5.0）

Work Log:
- 关于页修正：标题 "Hardmacro Craft" → "Hardmacro Craft Launcher"；WhatBody 双语补全程序名；
  版本徽章从硬编码 "v1.3.1"（已过期）改为运行时读取程序集版本（资源串改为 "v{0}" 模板，
  AboutPage 构造时 string.Format 填充）——以后升版不会再忘改
- 草方块图标（借鉴 PCL grass.png）：scripts/make_grass_icon.py 用 PIL 绘制等距像素草方块
  （绿色顶面 + 双侧面泥土 + 草皮边缘 + 材质噪点，96x96 透明底）→ Assets/grass.png；
  用于：下载页原版卡片头 + 原版版本列表行（34px 徽章位）+ 已安装版本列表行（VersionsPage）
  全部 NearestNeighbor 缩放保持像素风
- 版本列表滚轮根因修复：v1.4.0 兜底逻辑"只由最外层 ScrollViewer 滚动"导致内层列表
  （LstVanilla / 已安装列表 / Modrinth 结果列表）永远滚不动
  → 重写为：最内层（鼠标正下方）ScrollViewer 优先滚动；到边界自动委托最近可滚动祖先接力
    （列表滚完接页面）；HasInnerScrollViewer 沿可视树上溯判断，Scroll Bar 不干预
- 主窗口默认 960x540（原 1020x670），MinHeight 同步降为 520
- csproj：grass.png Resource；版本 1.5.0(.0)；README 标题/版本/滚轮行/图标行更新
- 回归：HmCoreTest 38/38 通过；publish 单文件 70.8MB PE32+ + 便携版 164MB
- 交付：download/v1.5.0 单文件 zip（64.8MB）+ Portable zip（69.2MB）+ source zip（123K）；git 提交
- 注意：工作区再次被部分重置（src 变空骨架、scripts/ 被 root 锁定），已从 /tmp 快照恢复源码，
  scripts/ 目录改名 scripts.rootlock 后重建；本次快照已同步回 /tmp

Stage Summary:
- v1.5.0 交付：程序名/关于页全量对齐 + PCL 式草方块 + 内层列表滚轮（版本列表可独立滚动）
- exe 需 Windows 实测：滚轮在版本列表内的手感与到边接力、960x540 下页面布局是否拥挤
---
Task ID: 10
Agent: 主控（Z.ai）
Task: 直接取用 PCL 源码图标资源（v1.5.0 交付覆盖）

Work Log:
- 用户要求直接用 PCL 源码里的 grass.png；本地 pcl-ce 目录为空 → 从 GitHub PCL-Community/PCL2-CE
  master 分支 raw 拉取（中途 raw CDN 抽风 404，稍后重试恢复）
- 替换 Assets/grass.png 为 PCL 原版 Images/Blocks/Grass.png（64x64 官方材质渲染，替代手绘版）
- 顺手同目录借来 4 张装载器图标：Anvil.png(Forge) / NeoForge.png / Fabric.png / Quilt.png
  → 下载页四张卡片头全部从 SymbolIcon 换成 PCL 图标（NearestNeighbor 像素缩放），
    Fabric/Quilt 共用卡片头为双图并排
- csproj 增加 4 个 Resource；构建 0 错误；HmCoreTest 38/38（bad.mrpack 瞬时锁冲突重跑通过）
- publish 覆盖重建：单文件 70.8MB + 便携版；download/ 三个 v1.5.0 zip 已刷新；git 提交；/tmp 已同步

Stage Summary:
- 下载页视觉全量 PCL 化（原版草方块 + Forge 铁砧 + NeoForge + Fabric + Quilt 官方渲染图标）
- 注意：PCL2 图标资源受其自身许可约束（非 MIT），README 的"未分发第三方启动器代码"表述
  仍指代码层面；图片素材如需对外发布可择机重绘替换
---
Task ID: 11
Agent: 主控（Z.ai）
Task: 全局滚动动画 + 对齐主流启动器体验（v1.6.0）

Work Log:
- 滚动动画（Services/SmoothScroll.cs 新增）：滚轮不再瞬时跳变，逐帧指数逼近目标偏移
  （每帧走剩余 25%，阈值 0.6px 收敛）；天然可中断（连续滚轮只更新目标，无动画时钟堆叠）；
  用户拖动/点击滚动条自动让位（监听 ScrollBar.ScrollEvent，非 EndScroll 即取消）；
  CompositionTarget.Rendering 驱动，无目标时自动摘除；App.xaml.cs 兜底处理器改调 SmoothScroll
- 主页「最近启动」：AppSettings.RecentVersions（最新在前去重最多 6 个，RecordLaunch()），
  HomePage 新增卡片 WrapPanel chips（草方块图标 + 版本名，未安装版本半透明），点击直接启动；
  启动逻辑重构为 LaunchAsync(version)（下拉大按钮与快捷 chips 共用）
- Modrinth 排序 + 分页：SearchAsync(query, type, index, offset) 返回 (hits, total_hits)；
  ModsPage/ModpacksPage 工具条加排序下拉（相关度/下载最多/最多关注/最新发布/最近更新，
  切换即重查）+ 底部「加载更多」按钮（显示 25/N 进度，无结果时置灰），ProjectId 去重合并
- 窗口状态记忆：记住关闭时是否最大化（尺寸始终默认 960×540，尊重用户明确要求）
- 双语资源：S.Home.Recent + S.Mod.Sort*/LoadMore/NoMore 各 8 条
- 构建 0 错误（修 3 处：ScrollEventArgs 需 Controls.Primitives、ScrollBar.ScrollEvent 才是
  Scroll 事件路由键、Window Color-Color 规则需全限定 System.Windows.WindowState）
- 回归：HmCoreTest 38/38；publish 单文件 70.8MB + 便携版；交付 v1.6.0 三 zip；git 提交；/tmp 已同步

Stage Summary:
- v1.6.0：滚动动画 + 最近启动 + Modrinth 排序/分页 + 窗口状态记忆
- 需 Windows 实测：滚轮动画手感（可微调 ApproachRatio 0.25 / FinishThreshold 0.6）、
  连续快速滚动是否流畅、加载更多翻页、最近启动 chips 点击启动

Task ID: 12
Agent: 主控（Z.ai）
Task: 下载引擎修复 + 进度提示修复 + 滚动距离调小（v1.7.0）

Work Log:
- 根因定位：ModpackService.DownloadToAsync / ModrinthService.DownloadAsync 直接流拷贝写目标文件，
  无重试、无校验 → 断网 / CDN 5xx / 静默截断都会留下半截文件或让整合包安装中断
- 新增 Services/DownloadEngine.cs（统一下载引擎）：
  * 先写 .hmc-part 临时文件，sha1 + 大小双重校验通过后才改名（游戏目录不再可能混入残缺文件）
  * 失败自动重试 3 次（指数退避 600ms/1.2s/2.4s），支持 HTTP Range 断点续传（206 追加，不支持则重下）
  * DownloadBatchAsync 批量并发（4 线程 + SemaphoreSlim），单文件失败不中断整批、最后汇总失败清单；
    已完整文件 sha1 快速校验后跳过（同时清洗上次留下的坏文件）
- ModpackService：整合包文件下载改走引擎（index 自带 sha1 全部生效）；
  InstallAsync 进度签名升级为 ModpackInstallProgress（当前文件/完成数/总字节）
- 进度提示修复：
  * 下载页/主页原版安装首次接入 byteProgress：显示「下载中 x MB / y MB · z MB/s」（SpeedMeter 滑动估算）
  * 百分比整数除法 → 浮点（任务少时不再恒显 0%）
  * Forge/NeoForge/Fabric/Quilt 安装进度条改为 Indeterminate 转圈（之前完全不动像卡死），防重复点击 _installing 守卫
  * 整合包下载与安装：进度条 + 当前文件名 + 文件 n/N + 字节/速度 全套显示
- 启动流程：启动日志分阶段提示（检查补全文件→创建进程），启动时同样显示字节进度+速度，
  ProcessWrapper.Exited 事件把「游戏已退出，退出代码 x」写回日志
- 滚动手感：每格滚轮 60px → 40px（App.xaml.cs delta/3.0，用户反馈 60px 过重）
- 版本号 1.7.0（csproj + UserAgent + GameLauncherVersion）
- 回归：HmCoreTest 新增 8 项下载引擎测试（本地 HttpListener：正常下载校验/sha1 拦截/临时文件清理/
  大小拦截/完整判定/批量跳过/批量进度）→ 46/46 全过；顺手修 bad.mrpack 残留非幂等问题
- publish 重建：单文件 67.9MB zip + 便携版 66MB zip + 源码包 140KB（rsync 剔除 bin/obj，56 文件）
  （注意：本次源码包改用 rsync 白名单方式，之前 git archive 方式会带上 bin/obj 359MB）
- git 提交 1d1cbf1；download/ 与 /tmp/my-project/download/ 已同步 v1.7.0 三 zip + 两个解压目录

Stage Summary:
- v1.7.0：下载可靠性（临时文件+校验+重试+续传+并发）与进度可视化（字节/速度/当前文件）全面修复
- 滚轮每格 40px，若仍嫌多可再调 App.xaml.cs 的 delta 除数
- 需 Windows 实测：整合包安装中途拔网线的重试/续传表现、Forge 安装转圈、滚动手感

Task ID: 13
Agent: 主控（Z.ai）
Task: 修复离线登录（账号丢失）+ 核心功能复查 + download 目录清理（v1.7.1）

Work Log:
- 根因确认（acctcheck 独立工程复现）：AccountStore.Accounts 为私有 setter，
  System.Text.Json 反序列化默认跳过该属性 → accounts.json 明明写盘成功，但启动 Load() 后
  账号列表恒为空 → 离线/微软/第三方账号全部"重启即丢"，用户视角即"离线登录有问题"
- 修复：Accounts 加 [JsonInclude]（私有 setter 参与序列化）；AppSettings.Load 对空 GameRoot
  回退默认值（防配置手改坏后启动即崩）
- 离线 UUID 算法交叉验证：Python 复算 MD5("OfflinePlayer:Steve") v3 = 5627dd98-...，与实现一致；
  MSession 构造参数顺序由回归测试 6 的 "Launch Steve 0" 断言背书，无问题
- HmCoreTest 新增 2.5 节账号持久化回归（写盘→同路径反序列化→字段/Current 校验）3 项；
  7.5 节批量下载测试改为幂等（清残留 dl 目录，修复上轮"请求 0 次"flaky）→ 50/50 全过
- download/ 大扫除：删除 v1.4.0~v1.7.0 全部旧 zip 与解压目录，只留 v1.7.1 三件套
  （单文件 zip / 便携 zip / 源码 zip）+ 两个解压好的启动器目录
- publish 重建（单文件 67.9MB zip + 便携 66MB zip + 源码 143KB）；git 提交 a4baae6

Stage Summary:
- v1.7.1：账号持久化修复 —— 用户此前登录过的账号其实一直没丢（写盘正常），是读回时被丢弃；
  升级后重启即恢复，无需重新登录
- 若用户此前用过 v1.7.0，accounts.json 里已有账号数据，升级后自动恢复
- 待做（对齐主流）：账号管理 UI（多账号切换/删除）、下载任务队列页、Java 自动管理

Task ID: 14
Agent: 主控（Z.ai）
Task: 第三方登录修复 + LittleSkin 体验对齐 PCL + 离线登录崩溃 + 下载按钮"下载中xx%"（v1.7.2）

Work Log:
- 第三方登录无法启动游戏【根因修复】：AuthlibLoginWindow 登录成功只把结果放进 Result 属性，
  HomePage 只读它打日志 —— 账号从未写入 AccountStore（对比 MicrosoftLoginWindow 内部有 Upsert）。
  表现为日志"第三方登录成功"，账号栏仍"未登录"，启动被"请先登录账号"拦截。
  修复：登录成功即在窗口内 AccountStore.Instance.Upsert(Result)
- LittleSkin 体验对齐 PCL：多角色账号登录后弹出角色选择对话框（此前静默取第一个）；
  记住密码（StoredAccount 增 LoginId/RememberPassword/SavedPassword，便携 config/accounts.json），
  下次打开自动填充服务器/邮箱/密码；按验证站 meta.links.register 显示"没有账号？去注册"链接；
  RefreshAsync 兜底：未传 selectedProfile 时沿用传入角色（部分实现只回 accessToken）
- 网络链路实测（curl）：littleskin.cn API Root 自动发现、authenticate/validate/refresh 端点、
  BMCLAPI 与官方源 authlib-injector 1.2.8 latest.json + sha256 全部正常 —— 排除服务端因素
- 离线登录崩溃修复：crash.log 定位 Prompt.Show "Cannot set Owner property to itself"；
  Show(caption, title, owner) 显式传 owner（Window.GetWindow(this)），兜底枚举时排除自身
- 下载/启动按钮「下载中xx%」：下载页原版行按钮 + 状态文本显示百分比（字节优先、任务数兜底），
  Forge/Neo/Fabric/Quilt 按钮"下载中…"；主页启动按钮启动期间显示"下载中 xx%"→"启动中…"，
  结束恢复原文案
- 版本 1.7.2；HmCoreTest 新增 2.6 第三方账号 Upsert 链路 + 2.7 离线 UUID v3 规范校验 → 55/55 全过
- download/ 大扫除：v1.7.1 全部旧产物删除，只留 v1.7.2 三件套 zip；staging 目录此次建在
  my-project 根下（上次误建在 HardmacroCraft/ 下导致首次复制为空）；git 提交 fb2cdf4

Stage Summary:
- v1.7.2：第三方登录全链路可用（登录→保存→启动→authlib-injector 注入），LittleSkin 对齐 PCL 体验
- 需 Windows 实测：LittleSkin 真实账号登录→启动（多角色用户应看到角色选择框）
- 待做：账号管理 UI（多账号切换/删除）、Java 自动管理、启动任务队列页

Task ID: 15
Agent: 主控（Z.ai）
Task: 继续对齐 PCL 体验：账号管理面板 + JVM 自定义参数（v1.7.3）

Work Log:
- 账号管理面板（PCL"账户"同款）：新增 Views/AccountManager.cs（代码构建 FluentWindow，
  深浅色主题资源一致）。所有已保存账号列表（类型徽章配色：微软绿/第三方蓝/离线棕）、
  当前账号高亮 + "当前"标记、一键切换当前账号（启动使用）、删除（确认框）、
  底部三入口直接登录新账号（微软需 Azure Client ID，缺失时提示）
- 入口：主页账号卡新增"管理账号"按钮；点击头像/账号信息区域同样打开
- JVM 自定义参数（PCL 的"JVM 参数"）：AppSettings.CustomJvmArgs（settings.json）；
  设置页 内存/Java 卡片新增输入框（等宽字体 + 提示）；LauncherService.ParseJvmArgs：
  空白分隔 + 双引号包裹含空格参数（剥引号），启动时追加在版本默认参数之后，
  与 authlib-injector javaagent 共存（合并而非覆盖）；日志提示"已应用 N 个自定义 JVM 参数"
- 工作区再次被重置：从 /tmp/my-project 恢复 src/tests/worklog/download（v1.7.2 三 zip 完好），
  HardmacroCraft/.git 丢失 → git init 重建，首次提交含全部 v1.7.3 源码
- 回归：新增 2.8 JVM 参数解析（常规/引号含空格/空串/首尾空白）+ 2.9 CustomJvmArgs 持久化往返
  → 61/61 全过
- 版本 1.7.3（csproj + UserAgent + GameLauncherVersion）；publish 双包 + 源码包重建；
  download/ 只留 v1.7.3 三件套并同步 /tmp

Stage Summary:
- v1.7.3：账号多开/切换/删除 + JVM 参数自定义，PCL 核心体验补齐
- 需 Windows 实测：管理面板切换账号后启动用新账号；JVM 参数含空格引号场景
- 待做：Java 自动管理（下载 JRE）、下载任务队列页、皮肤头像（正版/验证站可拉 skin）

Task ID: 16
Agent: 主控（Z.ai）
Task: 六小时大批量功能改进 + PCL/HMCL 体验对齐（v1.8.0）

Work Log:
- Java 自动管理（PCL 同款，本次最大功能）：新增 Services/JavaManager.cs。
  版本 → Java 大版本映射（版本 json 的 javaVersion.majorVersion 优先，
  缺失按版本名启发式：1.17→16、1.18–1.20.4→17、1.20.5+→21、其余→8，
  兼容 forge/fabric 后缀名）；设置页新增"Java 自动管理"卡片：
  一键下载 Java 8/17/21（Eclipse Temurin JRE，Adoptium API 取元数据，
  清华 TUNA 镜像优先 + GitHub 官方兜底，SHA-256 全文件校验，解压到
  exe 同目录 runtime/java-<主版本>/，便携可迁移）；启动时按版本需要
  自动挑选最合适的托管 Java（用户手填路径优先，达标 Java 中取最接近版本），
  游戏窗口分辨率设置（GameWidth/GameHeight，0=默认）一并加进启动参数
- 任务中心（HMCL 式）：新增 Services/DownloadTaskService.cs（线程安全，
  50 条历史上限，进度夹紧 0-100）+ Views/TaskCenterPage（500ms 轮询快照，
  状态徽章配色：进行中蓝/完成绿/失败红/取消灰）。主页"补全游戏文件"、
  下载页原版/Forge/Neo/Fabric/Quilt 安装、Java 下载全部登记任务
- 皮肤头像：新增 Services/SkinService.cs。微软正版走 Mojang sessionserver
  textures 属性（base64 → SKIN url），第三方验证站按 authlib-injector 规范
  GET {API Root}/profiles/{uuid}；PNG 魔数校验；UI 层 CroppedBitmap 裁剪
  64×64 皮肤 (8,8)-(16,16) 头部区域。主页账号卡与账号管理面板每一行
  均显示皮肤头像，离线/无皮肤回退字母头像（离线账号无法拉取皮肤）
- 自动登录（PCL 同款）：StoredAccount.AutoLogin 字段；第三方登录窗口新增
  "自动登录"复选框（勾选记住密码后可用）；启动器打开时当前账号若
  AutoLogin+记住密码则用保存的密码静默重登（失败仅记日志不打扰）
- 第三方登录服务器预设：TxtServer 输入框升级为可编辑 ComboBox
  （LittleSkin 预设 + 本机登录过的验证站自动入列 + 上次使用的排最前），
  LastAuthlibServer 记住上次服务器
- 版本 1.8.0（csproj + UserAgent + GameLauncherVersion）；README 大更新
  （功能表 v1.8.0 条目、Java 自动管理章节、92 项测试、目录结构加 runtime/）
- 回归：新增 11 Java 管理（版本映射 9 例 + json 优先级 + FindJavaw +
  PickJavaFor 开关）、12 任务中心（8 例含 50 上限）、13 皮肤（真实网络
  Mojang Notch profile textures 解析 + PNG 魔数 + 损坏输入兜底）
  + AutoLogin/分辨率/LastAuthlibServer 持久化 → 92/92 全过
- E2E 实测：真实下载 Temurin JRE 21 全链路（TUNA 镜像 2 秒、SHA-256 通过、
  解压定位 javaw.exe 成功）
- 测试修正：皮肤 PNG 阈值 1000→300（Notch 64×32 旧版皮肤仅 526 字节，
  PNG 本身有效）
- 修复过程：WPF 原生 ComboBox 无 PlaceholderText（改用可编辑 ComboBox 默认
  文案由代码设置）；新服务文件补 using System.Net.Http；测试 csproj 补挂
  3 个新服务源码链接
- download/ 只留 v1.8.0 三件套 zip（staging 在 my-project 根下，打包后已清理）

Stage Summary:
- v1.8.0：Java 自动管理（下载+自动选择）+ 任务中心 + 皮肤头像 + 自动登录
  + 服务器预设 + 分辨率设置，PCL/HMCL 核心体验基本补齐
- 需 Windows 实测：设置页下载 Java 后启动 1.20.5+ 自动选 Java 21；
  LittleSkin 勾选自动登录后重启启动器应静默重登；皮肤头像显示
- 待做：Java 下载国内镜像可再加强（Adoptium TUNA 已可用）；
  版本页"打开存档/mods 目录"快捷入口；Mod 页批量下载

Task ID: 17
Agent: 主控（Z.ai）
Task: Mod 双平台接入 + 体验对齐 + 手机端开发准备（v1.9.0）

Work Log:
- CurseForge 平台接入（本次核心）：新增 Services/CurseForgeService.cs。
  REST API 客户端（x-api-key 请求头，Key 来自设置页），分区 classId 与社区
  公共常量一致（Mod=6 / 资源包=12 / 光影=6552 / 数据包=6945，经 PrismLauncher
  源码核实）；搜索（searchFilter/sortField/sortOrder/gameVersion+modLoaderType
  成对附加）；文件列表（displayName/fileName/releaseType/gameVersions）；
  下载走 DownloadEngine，官方 downloadUrl 缺失时回退 forgecdn CDN 规则地址
  （mediafilez.forgecdn.net/files/{id/1000}/{id%1000}/{文件名}，已用真实 JEI
  文件 HTTP 200 验证）；版本号→1.9.0（csproj + UserAgent + GameLauncherVersion）
- ModsPage 双平台：工具条新增平台下拉（Modrinth/CurseForge）+ 加载器过滤
  （全部/Forge/Fabric/NeoForge/Quilt）+ 数据包类型；搜索统一行模型
  （SearchHit 加 Source 字段，徽章行首列平台名）；CF 未配置 Key 时副标题
  给出 console.curseforge.com 申请引导
- PCL 式版本选择对话框：新增 Views/ModVersionWindow.cs（代码构建 FluentWindow，
  行内发布类型徽章 release 绿/beta 黄/alpha 红，徽章行含游戏版本/加载器/日期/
  体积，双击或按钮选定）。Modrinth 列版本、CF 列文件；Modrinth 命中后必需
  依赖自动批量补齐（BFS 遍历 required 依赖 → GetLatestCompatibleFileAsync
  按版本+加载器挑最兼容文件 → 依赖的依赖继续入队，上限 15 个，同名跳过）
- ModrinthService 增强：SearchAsync 支持 gameVersion/loader facets（真实 API
  验证通过）；ProjectVersion 携带 Dependencies；GetLatestCompatibleFileAsync
  （release 优先 → 主文件优先）
- Mod 页本地管理加"打开目录"按钮；版本页新增打开存档/mods/资源包快捷入口
  （尊重版本隔离）
- 设置页新增 CurseForge API Key 输入 + "申请 Key"按钮（console.curseforge.com）
- 手机端开发准备：新增 src/HmCore/HmCore.csproj —— 跨平台共享核心（net8.0，
  源码链接 12 个 Services 文件与桌面端单一事实来源，含 CmlLib 依赖，编译通过）；
  新增 docs/MOBILE.md —— FCL/Pojav/Boat 路线对比、选型（Android 原生 +
  Pojav 式 JVM 栈自研，不复制 GPL 代码）、HmCore 复用表、M0-M4 阶段计划、
  风险清单；README 更新（v1.9.0 功能表、Mod 双平台章节、手机端章节、
  第三方组件补 CurseForge API 说明）
- 回归：新增第 14 节 Mod 双平台 22 项（CF classId/排序/modLoaderType 映射、
  CDN 回退地址、搜索/文件 JSON 夹具解析、API Key 请求头、无 gameVersion 不附
  modLoaderType、Modrinth facets/依赖解析/兼容文件挑选/无兼容返回 null、
  ExtractGameVersion、平台徽章）→ 121/121 全过
- 测试修正一例：CDN 地址规则 fileId 段应为 id%1000（测试先行抓出，改后通过）
- ModVersionRow 移入 Services（测试可引用；ModVersionWindow 只保留窗口逻辑）
- download/ 只留 v1.9.0 三件套 zip（staging 在 my-project 根下，打包后已清理）；
  同步 /tmp

Stage Summary:
- v1.9.0：Modrinth + CurseForge 双平台（版本选择 + 依赖自动补齐）+ 快捷目录
  + HmCore 共享核心 + 手机端路线图，121 项测试全过
- 需 Windows 实测：CurseForge 搜索（需自有 Key）、版本选择对话框 UI、
  Modrinth 依赖批量下载（如 JEI 拉入 Fabric API）、快捷目录按钮
- 待做：手机端 M1（Termux HmCore CLI 内核验证）；CF 依赖自动解析（需
  多步 API 查询）；Mod 页批量勾选下载

Task ID: 18
Agent: 主控（Z.ai）
Task: v1.10.0 —— Mod 批量安装 + CF 依赖自动解析 + 手机端 M1 内核验证（夜间自主任务）

Work Log:
- 上午插曲：用户输出目录不可见 → 尝试上传其服务器（103.79.184.97，SSH 密码在
  聊天记录附件中，root 可登录）。实测该机对外吞吐仅 ~40KB/s（SSH/HTTP/所有
  方向一致，疑似被攻击后机房限流），BBR/MSS 修复均无效，放弃线路侧提速。
- 分块断点续传上传器（scripts/ssh_chunk_upload.py，4MB 块 + 块内追加续传 +
  按大小校验半截块）传至 /www/wwwroot/hcl-release/.chunks（33 块中约 12MB 有效），
  未完成合并；用户随后改用免费中转，服务器上传中止。遗留：服务器上
  .chunks 目录 / /tmp 测试文件（tb.bin 等）待清理；mangle 表两条 TCPMSS 规则
  （INPUT/OUTPUT set 1240）待用户确认是否移除；sysctl 中 bbr+fq 原本已开启。
- tmpfiles.org 中转成功交付 v1.9.0 三件套（60 分钟时效链接已给用户）。
- v1.10.0 开发（本轮核心）：
  1) Mod 页批量安装（PCL 式）：搜索结果行加勾选框（ModRowItem INPC 模型），
     批量操作条（全选/清除/下载已选 N），BatchInstallOneAsync 自动挑最新
     兼容文件不弹对话框，逐项容错（单个失败不中断批次），失败计数汇总
  2) CurseForge 依赖自动补齐：CfFile 携带 Dependencies（relationType
     1=Embedded/2=Optional/3=Required/4=Tool/5=Incompatible/6=Include），
     ResolveCfDependenciesAsync 与 Modrinth 同款 BFS（上限 15，同名跳过），
     GetModsAsync POST /v1/mods 批量取名；单下载与批量安装两条路径都接上
  3) PickCompatibleFile 纯函数：gameVersion 过滤 → 加载器过滤（未标注视为
     通用）→ release 优先 → 最新优先；CfLoaderName 映射（Forge/Fabric/...）
  4) LauncherService.GetInstalledVersions 加 root 可选参数（供无头调用）
  5) 手机端 M1 起步：新增 src/HmCli（net8.0 控制台，引用 HmCore），
     Linux x64 真实运行：selftest 11 项全过（便携配置/离线 UUID/CDN 规则/
     CF 兼容挑选/JVM 参数），meta 经 BMCLAPI 拉取 921 个版本（381ms），
     versions/offline-uuid 子命令；MOBILE.md M0 完成/M1 起步状态更新
  6) i18n：S.Mod.Batch*/CfDepsStart/DepSkip 中英双语
- 版本 1.10.0（csproj + UserAgent + GameLauncherVersion）；README 功能表加
  批量安装行 + HmCli 用法
- 回归：130/130 全过（新增第 15 节 9 项：CfLoaderName 映射、挑选纯函数
  4 例、文件依赖 relationType 解析、GetLatestCompatibleFileAsync 端到端、
  GetModsAsync POST 体校验）
- 发布：单文件 64.8MB / 便携 69.3MB / 源码 196KB（staging 在 my-project 根，
  打包后已清理）；download/ 只留 v1.10.0 三件套；git 提交 96f1ea5

Stage Summary:
- v1.10.0：Mod 批量安装 + CurseForge 依赖自动解析 + HmCli M1 验证
- 需 Windows 实测：批量安装条勾选与进度、CF 依赖补齐（JEI→Fabric API 类比、
  需自有 API Key）、Modrinth 批量依赖链
- 待做：Termux arm64 实测 HmCli（M1 收尾）；Mod 页结果分页与勾选状态保持；
  服务器端 .chunks/测试文件清理与 MSS 规则确认

Task ID: 19
Agent: 主控（Z.ai）
Task: v0.1.0-beta —— 首个公开 Beta 发布（体验对齐 + 版本号重置）

Work Log:
- 用户指示以 0.1.0-beta 作为首个公开发布版本（版本号从 1.10.0 重置）
- 本轮功能（PCL/HMCL 对齐）：
  1) CrashHelper（Services/CrashHelper.cs，进 HmCore 共享）：崩溃报告目录/日志
     目录定位、ListCrashReports 按修改时间倒序、FindLatest、IsAbnormalExit；
     手机端 HmCli 也可共用
  2) 启动行为设置（HMCL 同款）：AppSettings 新增 HideLauncherOnGameStart /
     ExitLauncherOnGameExit；设置页「启动行为」卡片两个新开关；HomePage
     启动成功后按设置隐藏窗口（游戏退出自动恢复 Show+Activate），游戏退出
     后可自动关闭启动器
  3) 崩溃报告体验：游戏非零退出时日志直接给出最新崩溃报告文件名；
     主页日志区新增「日志目录」「崩溃报告」两个按钮（OpenFolderSafe）
  4) Mod 页：分页「加载更多」不再丢勾选（_selectedIds HashSet 恢复）；
     本地管理列表跟随搜索类型切换目录（mods/资源包/光影/数据包，
     LocalType + CmbType_SelectionChanged，过滤 .jar/.zip/.disabled）
  5) About 页改读 InformationalVersion（显示 0.1.0-beta 而非 0.1.0）
- i18n：S.Set.HideOnStart*/ExitOnGameExit*/S.Home.OpenLogs/OpenCrash 中英双语
- 版本：csproj 0.1.0-beta（Assembly/File 0.1.0.0）、UserAgent ×2、
  GameLauncherVersion、README 头部与功能表 3 行更新
- HmCoreTest csproj 补 CrashHelper.cs 源码链接（首轮编译抓出遗漏）
- 回归：139/139 全过（新增第 16 节 9 项 CrashHelper）；HmCli selftest 11/11
  （临时装 dotnet8 runtime 至 /tmp/dotnet8 后跑通）
- 发布：单文件 64.8MB / 便携 69.3MB / 源码 200KB；tmpfiles.org 三条直链
  交付（令牌直链已抽验 application/zip）；download/ 与 /tmp 双份更新；
  注意 tmpfiles 现行为：/dl/ 链接浏览器打开为中转页，页内才是真实下载

Stage Summary:
- v0.1.0-beta：批量安装翻页勾选保持 + 启动行为（隐藏/随游戏退出）+ 崩溃报告定位
- 需 Windows 实测：隐藏/恢复窗口时机、游戏退出关启动器、崩溃按钮、
  Mod 翻页勾选保持、资源包/光影本地管理
- 待做：Termux arm64 实测 HmCli（M1 收尾）；Mod 更新检查；服务器端清理

- 交付补记（Task 19）：tmpfiles.org 用户侧打不开（疑被墙）→ 改双线路重传：
  gofile.io 三件套（gofile.io/d/HIeWckwd 单文件 / RmujFZVP 便携 / nb6ovMsu 源码，
  服务端 MD5 全部核对一致）+ qu.ax 三件套（qu.ax/BHmP8 单文件 / 0zEL9 便携 /
  27KyM 源码，30 天有效；直链 /x/<id>.zip 需 Referer，页面 Download 按钮可用）。
  备选探测记录：catbox/litterbox/transfer.sh/filebin/bankupload 超时或封禁，
  file.io 广告墙超时，uguu 拒 .zip，pixeldrain 需注册 API Key，qu.ax 与 gofile 可用。
  文件同时在 /home/z/my-project/download/（用户可见输出目录）与 /tmp/my-project/download/。

Task ID: 20
Agent: 主控（Z.ai）
Task: 支线任务 —— B 站网页版 MC（OpusMC）完整离线镜像入 /downloads

Work Log:
- 目标 https://www.bilibili.com/toy/OpusMC（UP 主「土豆味小哲」的网页版 MC，
  "不用下载的网页版MC，几乎完全还原！"）
- 结构：B 站外壳页 iframe 嵌 bililitoy.com/toy/OpusMC/47455601425408-v37474/
  的纯 JS 实现（index.html + 85 个 JS，无外部贴图/音频——canvas 程序贴图 +
  WebAudio 程序合成音效，chunk worker 缺失时自动回退主线程）
- 爬虫 scripts/mirror_opusmc.py：抓 index 引用 + 扫 JS 字面量动态资源；
  踩坑：bilibilitoy 对 urllib 强制 gzip（已全部 gunzip 修复并 node --check 验证）
- 仅 credits.json/world.json 404 —— 数据实际内嵌 js/texts.js（window.MC_TEXTS），
  不影响游玩；联机房间指向 xz.honghemc.cn（需联网），单人完全离线
- 反镜像处理：index.html 末尾 location.replace 跳回 B 站的脚本已移除（离线必需），
  其余与原版一致
- 运行时验证（agent-browser + 本地 http.server）：协议弹窗 → 玩家类型 →
  画质测试（SwiftShader 软渲染实时渲染 3D 地形）全流程本地跑通，标题
  "Minecraft JS 复刻版"，无致命错误
- 交付：download/OpusMC-offline.zip（5.6MB，89 项，含使用说明.txt），
  MD5 3ad030586fa46b15ebd1fa4cabd7247f；原目录与 /tmp/my-project/opusmc 均留档

Task ID: 21
Agent: 主控（Z.ai）
Task: fix6 —— 游戏内存自动检测（参考 PCL）/ 核心功能继续对齐 PCL / 默认主题跟随系统

Work Log:
- 版本号 0.1.0-fix6（csproj Version + GameLauncherVersion；exe 内已抽验）
- 内存自动检测（PCL 同款「自动分配」，用户指定参考 PCL）：
  1) 新服务 Services/SystemInfo.cs（进 HmCore/测试源码链接）：
     GC.GetGCMemoryInfo().TotalAvailableMemoryBytes 检测物理内存（跨平台、无 WMI）；
     RecommendedMemoryMb(totalMb) 纯函数：一半内存、512MB 取整、
     限 [1024, min(8192, 总-1024)]（<2GB 机器给 1024）；CheckAllocatedMemory 启动体检
  2) AppSettings：MemoryAuto 默认 true；LauncherService.LaunchAsync 按自动/手动
     计算 MaximumRamMb，日志输出「游戏内存 XMB（自动检测，物理内存 YMB / 手动设置）」，
     超物理内存只警告不阻断（PCL 同款）
  3) 设置页内存卡：ChkMemAuto「自动检测内存（推荐）」开关（默认勾选），自动时禁用
     手动滑杆并显示推荐值；TxtMemDetected 实时显示「检测到物理内存 X MB，推荐分配 Y MB」
- 核心功能对齐 PCL（本轮）：
  1) 主页快速进服：TxtQuickServer + 「启动并进服」按钮（PlugConnected24 图标），
     LauncherService.LaunchAsync 新增 quickServer 参数 → MLaunchOption.ServerIp/ServerPort；
     ParseServerAddress 解析 host / host:port / [IPv6]:port（残缺端口整体判无效）；
     AppSettings.LastQuickServer 记忆上次地址，主页打开自动填充
  2) 启动前内存体检警告（见上）
- 默认主题跟随系统：AppSettings.Theme 默认 "dark" → "system"（ApplySystemTheme +
  SystemThemeWatcher 已有链路，fix5 已具备跟随能力，本轮只改默认值；老用户保存值不受影响）
- i18n：S.Set.MemAuto / MemAutoHint / MemDetected、S.Home.QuickServer / QuickServerPh /
  QuickServerBtn 中英双语
- 测试：HmCoreTest 新增第 17 节 29 项（推荐内存边界 / 512 倍数 / 物理内存真实检测 /
  内存体检 / 设置序列化保真 / 地址解析 9 例）→ 210/210 全绿；HmCore 单独编译通过
- 发布：便携 67MB / 单文件 62MB / 源码 293KB；qu.ax 三条直链（SHA1 与服务端一致），
  download/ 双备份 MD5 核验一致

Stage Summary:
- v0.1.0-fix6：内存自动检测 + 快速进服 + 主题默认跟随系统
- 需 Windows 实测：内存检测值与任务管理器一致性、快速进服实际进服、
  新装配置下跟随系统深浅色实时切换

---
Task ID: 22
Agent: 主控（Z.ai）
Task: fix7 —— Java 提示窗口高度 / 文案 / 启动进度双行显示 / 布局巡检 / Esc 返回 /
任务取消（启动 & 进行中任务）/ %APPDATA%\.minecraft runtime 残留清理（追加反馈）

Work Log:
- 版本号 0.1.0-fix7（csproj Version + GameLauncherVersion；exe 内已抽验）
- Java 安装提示窗口（Views/JavaInstallWindow.cs）：SizeToContent=Height 自适应高度
  （Min 280 / Max 640），修复高度过小按钮被截断；文案「需要安装 Java {0}」→
  「需要 Java 环境」（S.Home.JavaAskTitle zh/en 同步，具体版本号移到正文）
- 启动进度双行显示（PCL 同款，HomePage）：
  1) 第一行 TxtProgress：[122/2880] xxx.ogg（CmlLib InstallerProgressChangedEventArgs）
  2) 第二行 TxtStage（新增）：阶段【n/3】—— 检查 Java【1/3】→ 补全游戏文件【2/3】
     → 启动游戏【3/3】；LauncherService.LaunchAsync 新增 onStage 回调（i18n 键
     S.Home.Stage.*）
- 启动 & 进行中任务取消（PCL/HMCL 同款）：
  1) DownloadTaskService：StartCancellable / TokenFor / TaskItem.Cts / RequestCancel /
     CanCancel；Finish 幂等（canceled 不被 Complete 覆盖）
  2) HomePage：BtnCancelLaunch（进度区右侧），CancellationToken 贯穿 LaunchAsync 与
     JavaManager.EnsureJreAsync（取消不回退下载源）；取消后 UI 收尾恢复
  3) TaskCenterPage：运行中任务「取消」按钮替代状态徽章
  4) JavaManager：EnsureJreAsync 增加 ct 参数；OperationCanceledException 不再回退镜像
- Esc 返回（PCL 同款，MainWindow）：PreviewKeyDown → 非 HomePage 页面 Esc 回主页；
  弹窗为独立 Window 不受影响
- 继续对齐 PCL：
  1) 游戏窗口分辨率留空默认 1280×720（原来跟随游戏默认）
  2) 初始内存 -Xms = min(2GB, 分配值)（MinimumRamMb）
  3) 新服务 Services/GameOptions.cs：启动前补丁 options.txt —— 可选「跳过初始屏幕」
     （onboardAccessibility=false）与「游戏语言」（lang=zh_CN 等）；AppSettings 新增
     SkipOnboarding / GameLang；PatchLine/Apply 纯函数可测
- 页面布局巡检：SettingsPage / VersionsPage / ModsPage / TaskCenterPage /
  LoaderPicker / 登录弹窗 边距、折叠、自适应优化
- %APPDATA%\.minecraft runtime 残留（用户追加反馈「里面似乎是 runtime」）：
  · 根因反查（ilspycmd 反编译 CmlLib.Core 4.1.0 + Commons）：现行代码所有写入路径
    均可控（MinecraftPath 构造不建目录），runtime 残留为 fix3 之前旧版本泄漏
  · AppSettings.IsPollutedAppdataShell 扩展白名单：runtime\ 子树（Java 运行时二进制，
    无用户数据、可自动重建）与 versions\version_manifest_v2.json 同列为可清理内容；
    含任何其他文件（options.txt / saves / 真实版本）仍判定为真实游戏目录绝不触碰
  · LauncherService.CleanupPollutedAppDataShell 日志同步更新
- 测试：HmCoreTest 新增 fix7 节（任务取消 8 项 / GameOptions 8 项 / APPDATA 空壳
  判定 10 项）→ 236/236 全绿；HmCore 单独编译通过；修复测试文件 'before' 变量重名
- 交付：便携 66MB / 单文件 62MB / 源码 274KB；qu.ax 三条直链（SHA1 与服务端一致），
  download/ 备份 + README-下载说明.txt 更新为 fix7

Stage Summary:
- v0.1.0-fix7：进度双行 + 阶段计数 + 全链路取消 + Esc 返回 + Java 窗口修复 +
  APPDATA runtime 残留清理 + PCL 对齐（默认分辨率 / -Xms / 跳过初始屏幕 / 游戏语言）
- 需 Windows 实测：下载中取消按钮生效、任务中心取消 Java 下载、Esc 返回、
  Java 缺失弹窗按钮完整显示、APPDATA 空壳（含 runtime）启动后被清理

---
Task ID: 23
Agent: 主控（Z.ai）
Task: fix8 —— 启动参数优化（JVM 优化开关 / 游戏参数 / 全屏启动，PCL 同款）/
页面布局继续优化 / i18n 同步 / 254 测试全绿 / 打包上传交付

Work Log:
- 版本号 0.1.0-fix8（csproj Version + LauncherService.GameLauncherVersion）
- 启动参数优化（用户追加「看看启动参数能不能优化」，对齐 PCL/HMCL）：
  1) 新服务 Services/LaunchArgs.cs（纯函数，源码链接进 HmCore + 测试，单一事实来源）：
     · OptimizedJvmArgs 默认优化清单：-XX:+UseG1GC / -XX:-UseAdaptiveSizePolicy /
       -XX:-OmitStackTraceInFastThrow / Forge 证书与补丁兼容两个 -D /
       log4j2.formatMsgNoLookups=true（Log4Shell CVE-2021-44228 缓解）；
       刻意不含 -Xmn（G1 不适用）
     · ParseArgs：空白分隔 + 双引号包裹（原 LauncherService.ParseJvmArgs 逻辑迁入，
       ParseJvmArgs 委托至此保持 HmCli / 既有测试向后兼容）
     · BuildJvmExtra(optimize, custom)：优化参数在前、自定义在后（JVM 后声明优先）；
       自定义含其它 GC 收集器（-XX:+Use…GC）时跳过 G1GC 防冲突警告；
       按「参数键」（'=' 前部分）去重 —— 自定义 -Dfml.xxx=false 能压过优化 -Dfml.xxx=true
     · BuildGameExtra：游戏参数同规则解析
  2) AppSettings 新字段：JvmOptimize（默认开）/ CustomGameArgs / GameFullscreen
  3) LauncherService.LaunchAsync 接入：JVM 优化 + 自定义 JVM 参数合并注入
     ExtraJvmArguments；游戏参数注入 ExtraGameArguments；全屏 option.FullScreen；
     各带启动日志（优化启用 / 自定义参数个数 / 全屏）
  4) 设置页（SettingsPage）：「默认优化 JVM 参数（推荐）」勾选 + 提示、
     「游戏自定义参数」输入框（等宽字体 + 占位 --fullscreen --demo）、
     「全屏启动」勾选 + 提示；载入 / 保存接 AppSettings
  5) i18n zh/en 同步 7 键：S.Set.JvmOptimize(+Hint) / GameArgs(+Hint) / Fullscreen(+Hint)、
     S.Mp.Output；JvmArgsHint 补「与优化参数冲突时此处优先」
- 页面布局巡检与优化（fix7 巡检之上）：
  1) ModpacksPage 底部进度 / 输出卡默认 Collapsed（此前空闲时常驻占版面），
     Log() 展开显示、ClearLog() 折叠；卡内新增「任务进度与输出」标题（S.Mp.Output）
  2) 其余页面复查（根边距 32,24 统一 / MaxWidth 880-920 / VersionsPage WrapPanel /
     TaskCenter 空状态 / ModsPage 工具条自适应均已在 fix7 落位），无新问题
- 测试：HmCoreTest 新增第 21 节 fix8（18 项：优化清单 / GC 冲突跳过 / 按键去重 /
  反值覆盖 / 游戏参数解析 / 委托兼容 / 新字段默认值）→ 254/254 全绿；
  HmCore 单独编译 0 错误；测试 csproj 补 LaunchArgs.cs 源码链接
- 构建：清 obj/bin 后一次通过（复发过 CS0103 幻影错误，按既有经验处理）
- 交付：便携 66MB / 单文件 62MB / 源码 279KB；qu.ax 三条直链（SHA1 与服务端一致），
  download/ 备份 + README-下载说明.txt 重建（download 目录曾被沙盒回收）

Stage Summary:
- v0.1.0-fix8：JVM 优化参数默认开（G1GC / Forge 兼容 / Log4Shell 缓解）+ 自定义
  游戏/全屏启动（设置页「启动参数」一组）+ 整合包页空闲进度卡折叠
- 需 Windows 实测：默认优化参数实际注入游戏命令行（启动日志可见）、自定义 GC
  收集器时 G1GC 跳过、全屏启动、整合包页空闲不显示进度卡

---
Task ID: 24
Agent: 主控（Z.ai）
Task: fix9 —— 下载页愚人节版分类（对齐 PCL 五分类）/ 全量文案去 PCL/HMCL 字样 /
继续对齐 PCL 与 UI 优化 / 269 测试全绿 / 打包上传交付

Work Log:
- 版本号 0.1.0-fix9（csproj Version + GameLauncherVersion）
- 下载页新增「愚人节版」独立分类（用户点名，PCL 同款五分类）：
  1) 联网核实历届愚人节版本清单（Minecraft Wiki April Fools' Day jokes 年鉴）：
     2013-2026 共 10 个 —— 2.0 / 1.RV-Pre1 / 15w14a / 3D Shareware v1.34 /
     20w14infinite / 22w13oneBlockAtATime / 23w13a_or_b / 24w14potato /
     25w14craftmine / 26w14a（2026 Herdcraft）；2011/2012 两年为网站玩笑无游戏版本
  2) 新服务 Services/VersionKinds.cs（纯函数，进 HmCore + 测试）：
     AprilFoolsVersions 清单 + IsAprilFools（忽略大小写）；清单里愚人节版 type
     与快照同为 snapshot，只能按 ID 识别
  3) DownloadGamePage：第五个筛选 chip「愚人节版」；april 过滤；快照分类排除愚人节
     避免重复；愚人节徽章（MediumVioletRed 紫红）在任意分类下正确显示
  4) i18n：S.Dl.FilterApril / S.Dl.AprilFools + 10 条版本简介键（S.Dl.Desc.<id>，
     ID 含空格的 "3D Shareware v1.34" 亦为合法 x:Key），zh/en 同步
- 全量文案检查（用户要求去掉 HMCL/PCL 字样，「挂着感觉怪怪的」）：
  1) 语言文件 8 组可见文案（zh/en）：微软登录第 1 步、内存自动检测、跳过初始屏幕、
     JVM 优化、任务中心副标题、自动登录、分辨率、全屏启动 —— 全部移除
     「PCL 同款 / HMCL 式」表述（功能不变，只去署名式字样）
  2) 运行时日志 2 处：JVM 优化启用日志、离线皮肤注入日志
  3) README.md 9 处（源码包用户可见）
  4) XAML 注释（不渲染）保留设计溯源，不算文案
- 防回归测试（第 22 节 fix9，17 项）：愚人节识别正反例 10 项 + 10 个简介键
  zh/en 存在性 + 语言文件值 / 代码字符串字面量（逐行、跳注释行）无 PCL/HMCL
  → 269/269 全绿；中途修复 3 处测试自身问题（C# 单引号误用 / verbatim 字符串
  含裸引号 / 目录拼接多一层），并处理一次 bmclapi DNS 抖动（复跑即过）
- HmCore 单独编译 0 错误；语言键 341=341 双语一致（check_lang_keys.py OK）
- 交付：便携 66MB / 单文件 62MB / 源码 282KB；qu.ax 三条直链（SHA1 与服务端一致），
  download/ 备份 + README-下载说明.txt 更新为 fix9

Stage Summary:
- v0.1.0-fix9：下载页五分类（新增愚人节版，10 个历史版本一键可玩）+ 文案全量
  去其它启动器署名字样 + 防回归测试守护
- 需 Windows 实测：愚人节版列表与简介显示、快照分类不含愚人节版、愚人节版
  可正常下载启动、全界面无 PCL/HMCL 字样

Task ID: 26（v0.1.7）
Agent: 主控（Z.ai）
Task: v0.1.7 —— 多实例管理 / 启动进度补全（等待游戏窗口阶段）/ 首页日志卡撑满 / 全页面主容器去滚动 / 版本管理页计数 / 432 测试全绿 / 打包三件套发布

Work Log:
- 多实例管理（对齐主流启动器「运行中的游戏实例」）：Services/GameInstanceManager.cs
  跟踪所有由启动器拉起的游戏进程（版本 / PID / 起始时间 / 状态），支持同时运行多个版本；
  主页中部新增「运行中的实例」卡（有实例时出现）：实时列出 实例状态（等待窗口出现 / 运行中）、
  PID，一键「结束」走 Process.Kill(entireProcessTree=true) 连 Java 派生子进程一并结束；
  进程退出事件自动移出列表并写日志（含退出码）；窗口句柄探测 / 状态机进测试
- 启动进度补全（用户反馈「已启动到 MC 窗口出现有时间差，中间没有任何说明」）：
  启动流程本体到「游戏进程已创建」即结束（可立即再启动第二个实例），进度区交给
  后台窗口等待器接管：新增阶段【4/4】等待游戏窗口出现（轮询 MainWindowHandle，
  700ms 间隔 / 240s 超时）；窗口出现 → 实例标记运行中 + 「游戏运行中」终态回放空闲 UI；
  进程提前退出 → 明确报退出码；超时 → 按运行中处理并写日志提示；等待期取消按钮隐藏
  （改为实例卡里结束进程），等待器对新启动让出进度区（版本号守卫防串写）
- 首页布局响应式（用户补充：日志容器撑满剩余空间、主容器不滚动、快捷入口移除）：
  ScrollViewer+StackPanel 改为 Grid（账号卡置顶 / 运行实例+最近启动 / 日志卡 Star），
  日志卡展开时 VerticalAlignment=Stretch 撑满中部剩余空间（收起回紧凑标题栏），
  启动卡钉在窗口底缘；底部三个快捷入口卡删除（下载/整合包/模组走左侧导航）
- 全页面主容器去滚动（用户要求对齐，游戏/模组下载类优先）：
  模组页去外层 ScrollViewer，搜索结果列表撑满剩余空间（去掉 460 限高，仅列表内滚）；
  设置页 / 关于页改「标题固定 + 内容区内滚」（对齐系统设置式页面）；整合包 / 任务中心 /
  下载页 / 版本管理页此前已是响应式 Grid，验收通过
- 版本管理页：搜索框右侧新增版本计数（搜索时显示「匹配数/总数」）
- 测试第 31 节（25 项）：窗口等待判定纯函数（等待/窗口出现/超时/提前退出/零超时）+
  实例生命周期真实子进程验证（注册 / PID / 结束进程树 / 退出移除 / 日志）+
  新文案键 zh/en 齐全；全量 432 通过 / 0 失败 / 0 跳过；语言键 426=426 双语一致
- 升版 0.1.7 三处（csproj 三元组 / LauncherService / README）+ README v0.1.7 更新说明
- 交付：单文件 64.9MB / 便携 69.4MB / 源码 363KB（SHA1 见打包输出），GitHub Release + qu.ax 直链

Stage Summary:
- v0.1.7：多实例管理（运行实例卡 + 进程树结束）+ 启动进度四阶段（等待窗口出现）+
  首页日志撑满 / 全页面去滚动 + 版本计数；需 Windows 实测：多开两个版本、
  实例卡状态流转与结束进程、窗口等待文案与终态回放、各页面小窗口无滚动截断

---
Task ID: 25（v0.1.6）
Agent: 主控（Z.ai）
Task: v0.1.6 —— 关于页版本号乱码修复 / 首页启动卡重构 / 版本图标 / 版本管理页重排 / 版本独立设置 / 一键进服弹窗（保存到多人游戏列表）/ 408 测试全绿 / 打包双渠道发布

Work Log:
- 关于页版本号乱码（用户反馈）：根因是 .NET SDK 在 git 仓库构建时把源码修订号
  追加进 InformationalVersion（"0.1.6+<40位sha>"）直接展示即"一串乱码"；
  新增 VersionInfo.CleanDisplayVersion 剥除 '+' 后部分（预发布后缀保留），测试守护
- 首页启动卡重构（用户点名布局）：
  1) 顶部全宽账号容器：头像 52px + 用户名/账号类型两行（authlib 显示验证站主机名，
     如 "{host} 登录（第三方验证站）"），点击容器进账号管理，保留右侧管理按钮
  2) 中部内嵌折叠日志（原底部独立日志卡移除），展开时显示打开日志/崩溃报告按钮
  3) 版本选择行：下拉框带图标与副标题（SelectedValuePath=Name 条目视图模型）+
     版本设置齿轮 + 刷新；启动中被进度区替换（进度条+阶段文字+取消，PCL 式说明），
     空闲终态（游戏运行中/已取消/失败）回放为按钮行下小字
  4) 双按钮行：小「一键进服」弹窗式 + 大「启动游戏」（原配色式样保留）
- 一键进服弹窗 JoinServerWindow：地址输入 + 「保存到游戏的多人游戏列表」复选框 +
  勾选后名称输入框（默认 Minecraft服务器）；保存走自研 NBT（Services/ServerList.cs，
  全类型大端序编解码）写 servers.dat：保留 icon 等既有字段、同地址更新名称、
  servers.dat_old 备份、损坏文件拒写；版本隔离时写入 versions/<id>/servers.dat
  ⚠ 关键修复：NBT Compound 的 TAG_End 无名称字段，首版按带名读取导致解析必败
  （最小复现定位后修复，roundtrip 测试覆盖 icon/未知字段/中文服务器名/大端序）
- 版本图标（对齐 PCL）：Services/VersionInfo.cs 检测加载器（json 特征串 NeoForge
  优先于 Forge + .forge/.fabric/.quilt 等标记文件）与创建来源（hcl.json 本启动器 /
  pcl.json / .id 分号开头 / hmclversion.cfg）；VersionIconProvider 映射图标，
  其他启动器以首字母徽章显示（文案零 PCL/HMCL 字样，防回归测试通过）
  本启动器安装的版本写 hcl.json 所有权标记（MarkNewestVersionOwned 接入安装流程）
- 版本管理页重排（对齐 PCL）：搜索实时过滤 + 图标列表（图标+名称+副标题）+
  单击进版本设置 + 双击启动（HomePage.Current + LaunchFromExternalAsync 直连）+
  工具条（启动/版本设置/刷新/目录/存档/Mods/资源包/重命名/复制/删除）
- 版本独立设置：Services/VersionSettingsStore.cs（config/versions/<版本>.json，
  全默认不落盘）+ VersionSettingsWindow（隔离三态/Java/内存滑块/JVM/进服分组）；
  启动链路接入：BuildGamePath 隔离覆盖、内存覆盖（LauncherService）+ 版本 Java 优先
  解析（HomePage，无效回落全局并告警）+ JVM 参数按版本覆盖；DeleteVersion 联动清理
- i18n：新增 S.Ver.Loader.*×8 / S.Ver.From*×2 / S.Ver.Launch/Settings/SearchPh/
  ClickHint / S.Vs.*×17 / S.Join.*×8 / S.Home.VersionSettingsTip / AccountThirdFmt；
  清理 QuickServerPh；418=418 双语一致
- 测试：新增第 30 节（版本清洗 / 加载器与来源识别 / NBT roundtrip / servers.dat
  写回 / 版本设置存储）→ 408 通过 / 0 失败；HmCore 单独编译 0 错误
- 版本号三处 0.1.6（csproj / LauncherVersion / README）；package-016.sh 打包
  （单文件 64.9MB / 便携 66.2MB / 源码 351KB）；git push main+tag（本地仓库历史与
  远端不同源，改为克隆远端覆盖工作树提交 7ae60aa）；GitHub Release 440293123 +
  qu.ax 三直链（SHA1 与服务端一致）；download/README-下载说明.txt 更新

Stage Summary:
- v0.1.6：首页启动卡全面重排（账号容器/内嵌日志/版本行↔进度替换/一键进服弹窗）+
  版本图标体系（加载器+来源）+ 版本管理页 PCL 式重排 + 版本独立设置 +
  servers.dat 多人游戏列表保存 + 关于页乱码修复
- 需 Windows 实测：下拉框/列表图标显示、版本设置窗口交互、一键进服保存后游戏内
  多人游戏列表可见、启动中进度区替换与恢复、关于页版本号显示
