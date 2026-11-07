# Hardmacro Craft Launcher —— 手机端开发准备（Roadmap）

> 状态：**M1 内核验证进行中**（v1.10.0 起 HmCli 已在 Linux 真机环境跑通核心链路）。本文档是手机端的选型分析、合规边界、
> 架构规划与阶段计划，作为后续开发的蓝本。桌面端（WPF）仍是当前唯一交付形态。

---

## 1. 目标

在 Android 设备上运行 **Minecraft: Java Edition** 并提供与桌面版一致的启动器体验：
账号（微软正版 / 第三方验证站 / 离线）、版本管理、Mod / 整合包下载（Modrinth / CurseForge）、
Java 自动管理、便携化配置。

## 2. 参考项目与技术路线对比

| 项目 | 路线 | 核心思路 | 许可证 | 对我们的启示 |
| --- | --- | --- | --- | --- |
| **PojavLauncher** | Android 原生 | 移植 JVM（OpenJDK 8/17/21 Android 构建）+ 轻量启动逻辑 + gl4es/ANGLE 把 OpenGL 桥接到 GLES；LWJGL 用 Android 移植版 | GPL-3.0 | 架构思想可参考；**代码不可复制**（见 §3） |
| **FCL (Fold Craft Launcher)** | Pojav 二次开发 | 在 Pojav 基础上完善 Forge/Fabric/优化器安装链、控制虚拟鼠标、多 Java | GPL-3.0 | 交互细节（虚拟键位、控制中心）值得体验对标 |
| **Boat / AppContainer 系** | 容器 / chroot | 在 Android 上跑完整 Linux 用户态（glibc），按桌面 Linux 方式安装正版 JVM 与显卡桥 | LGPL-2.1 及混合 | 兼容性最好、栈最重；离线包体积大 |

### 选型结论

**主路线：Android 原生（Kotlin）+ Pojav 式 JVM 移植栈（自研整合，不复制 GPL 代码）。**

理由：
1. JVM 运行时是现成的开源资产（OpenJDK 官方已有 aarch64 Android 移植 / Termux OpenJDK 构建），启动器只需要组织 classpath 与参数——这正是我们桌面端已有的能力（`LauncherService` 的参数拼装逻辑可复用思路）；
2. 渲染桥（gl4es / ANGLE）与 LWJGL Android 移植均为独立开源组件，按其许可证（BSD/Apache 系）链接使用；
3. 容器方案（Boat 系）作为**备选**保留：若自研桥接投入过大，可退到"内置 Linux 根文件系统 + 正版 JVM"路线，兼容性风险最低。

## 3. 合规边界（重要）

- 本项目 **MIT 协议、从零手搓**，与 PCL 的约定一致地延伸到手机端：
  **禁止复制 Pojav/FCL/HMCL/PCL 的任何代码**。可以阅读其公开源码理解协议流程
  （JVM 启动参数、OpenGL 桥接方式），产出必须是自己的实现。
- Pojav / FCL 为 GPL-3.0：仅"参考架构思想"是安全的；复制源码会污染本项目许可证。
- 渲染桥组件按各自许可证使用（gl4es: MIT-like/zlib；ANGLE: BSD；LWJGL: BSD）。
- 微软正版登录必须走公开的 OAuth device flow / MSA 协议，不逆向任何私有接口。

## 4. 共享核心：HmCore（v1.9.0 已落地）

手机端**不重写**业务内核。`src/HmCore/HmCore.csproj`（net8.0）以源码链接方式
与桌面端共用同一份代码，已验证可在非 Windows 环境编译：

| 模块 | 文件 | 手机端复用方式 |
| --- | --- | --- |
| 下载引擎（重试/校验/并发） | `DownloadEngine.cs` | 直接复用 |
| Modrinth / CurseForge 客户端 | `ModrinthService.cs` `CurseForgeService.cs` | 直接复用 |
| 整合包解析 | `ModpackService.cs` | 直接复用 |
| 版本 / 库 / 资源清单（BMCLAPI 镜像） | `LauncherService.cs` `MirrorHttpHandler.cs` | 参数拼装复用；进程创建层换成 Android JVM 启动 |
| Java 管理（版本映射/下载/SHA-256） | `JavaManager.cs` | 下载逻辑复用；路径扫描按 Android 调整 |
| 账号 / 配置（便携 config/） | `AppSettings.cs` `AuthService.cs` `AuthlibService.cs` | 直接复用 |
| 任务中心模型 | `DownloadTaskService.cs` | 直接复用（UI 层换 Compose） |

桌面端 UI（WPF Views）**不在** HmCore 中，手机端用 Jetpack Compose 重建展示层。

## 5. 阶段计划

- **M0 准备（已完成，v1.9.0 / v1.10.0）**
  - [x] HmCore 共享核心库（net8.0 编译通过）
  - [x] 本文档与选型结论
  - [x] 与桌面回归共用测试夹具（130 项无头测试全过）
- **M1 内核验证（CLI，v1.10.0 起步）**
  - [x] 新增 `src/HmCli`：HmCore 无头验证工具（Linux 上真实运行）
    - `selftest`：便携配置 / 离线 UUID / CDN 规则 / CF 兼容文件挑选 / JVM 参数解析（11 项）
    - `meta`：经 BMCLAPI 镜像拉取完整版本清单（实测 921 个版本 / 381ms，Linux x64）
    - `versions` / `offline-uuid`：版本列举与离线 UUID
  - [ ] Termux（linux-aarch64）实测：`dotnet publish -r linux-arm64` 包体与 AOT 可行性
  - [ ] "资源补全 → 拼装 JVM 参数"全链路（不启动游戏）
- **M2 Android 壳**
  - Kotlin + Compose 工程，引用 HmCore（`net8.0-android` 目标）；
  - 集成 OpenJDK Android 构建（JRE 8/17/21 按需下载到应用私有目录）；
  - 账号：离线 + 微软 OAuth device flow + 第三方验证站（复用 AuthlibService）。
- **M3 游戏启动**
  - JVM 子进程启动（Android 上以应用内 Java VM 或独立进程方式）；
  - 渲染：集成 gl4es / ANGLE + LWJGL Android 移植；
  - 触控：虚拟鼠标 / 键位（体验对标 FCL）。
- **M4 生态完善**
  - Mod / 整合包页（复用 Modrinth/CurseForge 客户端）；
  - 任务中心、皮肤头像、版本隔离等桌面端特性平移。

## 6. 已知风险

| 风险 | 等级 | 缓解 |
| --- | --- | --- |
| 渲染桥兼容性（部分设备 GLES 支持差） | 高 | M3 先锁定 aarch64 + Adreno/Mali 主流机型；备选 Boat 式容器路线 |
| Android 存储限制（SAF / 私有目录） | 中 | 游戏目录默认应用私有目录，可选 SAF 导出；便携化语义在 Android 上以"可导出 zip"实现 |
| .NET Android 运行时包体 | 中 | AOT + 裁剪（PublishTrimmed）；内核必要时拆分为更小颗粒 |
| JVM 子进程策略（Android 限制后台进程） | 中 | 前台服务承载游戏进程；遵循 Android 14+ 前台服务规范 |
| GPL 污染 | 高（合规） | 坚持自研 + 只链接 BSD/Apache/MIT 组件；CI 加许可证审查 |

## 7. 桌面端不受影响

手机端所有工作都在独立目录（`src/HmCore`、未来 `mobile/`），不引入任何
Android 依赖到 WPF 工程；HmCore 仅以源码链接方式存在，桌面发布产物不变。
