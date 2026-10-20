# Dynamic Island for Windows

一个面向 Windows 桌面的轻量「灵动岛」。第一阶段以原生 WPF 验证视觉质感与动画；第二阶段迁移到 Vue 3 + TypeScript + Tauri 2 + Rust，扩展系统监控与快捷控制能力，同时保持收起态接近零后台开销。

## 当前版本（第二阶段 · Tauri）

<img src="docs/island-collapsed.png" width="440" alt="收起状态">

<img src="docs/island-expand-620.gif" width="620" alt="展开动画">

<img src="docs/island-expanded.png" width="620" alt="展开状态">

### 能力

- `176 x 48` 媒体态胶囊 / `104 x 48` 无媒体胶囊，无媒体时只显示时间。
- `380 x 220` 媒体展开态 / `380 x 112` 无媒体紧凑展开态。
- 无媒体时不渲染默认封面，展开态只保留 CPU、RAM、GPU、亮度与音量。
- 系统状态区：CPU（占用 + 温度）、RAM（占用）、GPU（占用 + 温度）。
- 快捷控制：亮度滑块与音量滑块，直接读写系统真实值。
- 曲率一致的 squircle 圆角（`CornerSmoothing = 0.6`），收起与展开使用同一套几何。
- 媒体会话：封面、标题、歌手与播放进度，支持上一首 / 播放暂停 / 下一首。
- 图钉保持展开、减少动态效果与通知聚合。

### 交互

| 操作 | 行为 |
| --- | --- |
| 鼠标进入 | 仅在可见胶囊/面板范围内触发展开 |
| 窗口命中区域 | 随胶囊/面板尺寸同步收缩，透明区域不会挡住下方窗口 |
| 鼠标离开 | 延迟约 140 ms 后收起 |
| 点击图钉 | 保持展开，忽略自动收起 |
| 拖动滑块 | 立即回写本地状态，并调用系统接口同步 |

### 动画系统

所有结构动画由 `src/core/animator.ts` 统一调度，尺寸、圆角与内容透明度沿同一条时间线推进，内容按「先让出空间、再出现」的顺序错峰，不做无意义的弹跳。

| 动画 | 时长 |
| --- | --- |
| 展开尺寸与圆角 | 360 ms，无过冲 S 形缓动 |
| 收起触发 | 鼠标离开 140 ms 后开始 |
| 收起尺寸与圆角 | 280 ms，无过冲 S 形缓动 |
| 内容淡入 | 180 ms |
| 收起内容退场 | 100 ms 淡出，再收缩表面 |

### 技术栈与目录

```text
island-app/
  src/                    Vue 3 + TypeScript 界面、动画、状态与 IPC
    components/           灵动岛表面与交互控件
    core/                 动画器、缓动、squircle 几何、指标与 IPC
    store/                全局状态与事件订阅
  src-tauri/              Tauri 2 + Rust 宿主
    src/lib.rs            窗口管理、媒体会话、系统数据、亮度与音量
    tauri.conf.json       窗口与安全配置
```

- Vue 3 + TypeScript 承载界面、设置和动画系统。
- Tauri 2 IPC 建立明确的事件与权限边界。
- Rust + Windows API 实现窗口管理、媒体会话、系统数据与快捷控制。

### 后台开销策略

- 收起态每 3s 轻量检查一次媒体会话，停止播放后封面会自动消失；系统数据仍不轮询。
- 展开态每 2s 检查媒体与系统数据，仅有变化时推送。
- 媒体信息优先依赖系统媒体会话事件，而不是持续查询播放器。
- UI 只更新发生变化的数据，避免整块界面反复重绘。

### 启动方式（推荐）

普通用户不需要 Node.js、Rust 或 Visual Studio，也不需要打开终端。

1. 双击项目根目录下的 `Dynamic Island 2.0.0 Setup.exe`，按提示完成安装（当前用户安装，不需要管理员权限）。
2. 安装结束时会自动启动，灵动岛直接出现在屏幕顶部。
3. 之后每次登录 Windows 都会自动运行；桌面与开始菜单均有快捷方式。
4. 需要手动唤出时，双击桌面图标、开始菜单快捷方式，或左键单击系统托盘的灵动岛图标。
5. 退出：右键托盘图标，选择「退出」。

托盘菜单中的「开机自启」带有勾选状态，可随时关闭或重新开启。

### 构建与运行

环境要求：

- Windows 10 版本 2004（Build 19041）或更高。
- Node.js 18+、Rust 工具链、Visual Studio Build Tools 与 Windows SDK。

```powershell
cd D:\Code\Dynamic_Island_for_Windows\island-app
npm install
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

构建产物：

```text
island-app\src-tauri\target\release\dynamic-island-windows.exe
island-app\src-tauri\target\release\bundle\nsis\Dynamic Island_2.0.0_x64-setup.exe
```

其中 `setup.exe` 是可直接分发的安装包，安装后常驻托盘并默认开机自启。

开发模式（带热更新，终端需保持打开）：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 dev
```

浏览器 mock 模式不需要 Tauri，可直接做视觉 QA：

```powershell
npm run dev
```

## 第一阶段（WPF · 保留在仓库根目录）

第一阶段以原生 WPF 实现三态交互、媒体控制与轻量系统状态，用于验证视觉质感与动画基线。
```powershell
dotnet build .\DynamicIsland.csproj -c Release
dotnet run --project .\DynamicIsland.csproj -c Release
```

## 可靠性

- 单实例互斥，重复启动只会唤出已有窗口。
- 安装版常驻系统托盘，并默认随 Windows 登录自动启动。
- 原生窗口尺寸跟随可见表面收缩，透明区域不再拦截鼠标点击。
- 全屏应用期间隐藏窗口并取消置顶，退出全屏后重新定位。
- 处理显示器切换、DPI 变化与休眠唤醒。
- 媒体会话失效后自动重启监听，不阻塞 UI 线程。
- GPU 数据依赖 NVML；未检测到 NVIDIA GPU 时回退为 0%，温度不展示。

## 已知边界

- 媒体控制依赖 Windows 全局媒体会话，应用未暴露会话时不可用。
- CPU / GPU 温度为轻量展示数据，不是完整硬件监控面板。
- 亮度控制依赖 `WmiMonitorBrightness` 接口，台式机外接显示器可能不可用。
- 通知聚合目前为内置演示，尚未接入完整插件系统。