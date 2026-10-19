# Dynamic Island for Windows

一个面向 Windows 桌面的轻量「灵动岛」。第一阶段以原生 WPF 验证视觉质感与动画；第二阶段迁移到 Vue 3 + TypeScript + Tauri 2 + Rust，扩展系统监控与快捷控制能力，同时保持收起态接近零后台开销。

## 当前版本（第二阶段 · Tauri）

### 能力

- `176 x 48` 胶囊收起态，显示时间与当前媒体封面。
- `380 x 220` 展开态，包含媒体控制、播放进度与系统状态区。
- 系统状态区：CPU（占用 + 温度）、RAM（占用）、GPU（占用 + 温度）。
- 快捷控制：亮度滑块与音量滑块，直接读写系统真实值。
- 曲率一致的 squircle 圆角（`CornerSmoothing = 0.6`），收起与展开使用同一套几何。
- 媒体会话：封面、标题、歌手与播放进度，支持上一首 / 播放暂停 / 下一首。
- 图钉保持展开、减少动态效果与通知聚合。

### 交互

| 操作 | 行为 |
| --- | --- |
| 鼠标进入 | 从收起态平滑展开 |
| 鼠标离开 | 延迟约 250 ms 后收起 |
| 点击图钉 | 保持展开，忽略自动收起 |
| 拖动滑块 | 立即回写本地状态，并调用系统接口同步 |

### 动画系统

所有结构动画由 `src/core/animator.ts` 统一调度，尺寸、圆角与内容透明度沿同一条时间线推进，内容按「先让出空间、再出现」的顺序错峰，不做无意义的弹跳。

| 动画 | 时长 |
| --- | --- |
| 展开尺寸与圆角 | 360 ms，无过冲 S 形缓动 |
| 收起尺寸与圆角 | 340 ms，无过冲 S 形缓动 |
| 内容淡入 | 180 ms |
| 收起内容退场 | 先淡出，再收缩表面 |

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

- 收起态不轮询媒体与系统数据，Rust 线程仅以 500ms / 1000ms 间隔检查窗口状态。
- 展开态每 2s 推送一次有变化的媒体与系统快照。
- 媒体信息优先依赖系统媒体会话事件，而不是持续查询播放器。
- UI 只更新发生变化的数据，避免整块界面反复重绘。

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
```

开发模式（带热更新，终端需保持打开）：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 dev
```

浏览器 mock 模式不需要 Tauri，可直接做视觉 QA：

```powershell
npm run dev
```

## 第一阶段（WPF · 保留在仓库根目录）

第一阶段以原生 WPF 实现三态交互、媒体控制与轻量系统状态，用于验证视觉质感与动画基线。以下为第一阶段效果：

<img src="docs/island-collapsed.png" width="440" alt="收起状态">

<img src="docs/island-expand-620.gif" width="620" alt="展开动画">

<img src="docs/island-expanded.png" width="620" alt="展开状态">

```powershell
dotnet build .\DynamicIsland.csproj -c Release
dotnet run --project .\DynamicIsland.csproj -c Release
```

## 可靠性

- 单实例互斥，避免重复创建置顶窗口。
- 全屏应用期间隐藏窗口并取消置顶，退出全屏后重新定位。
- 处理显示器切换、DPI 变化与休眠唤醒。
- 媒体会话失效后自动重启监听，不阻塞 UI 线程。
- GPU 数据依赖 NVML；未检测到 NVIDIA GPU 时回退为 0%，温度不展示。

## 已知边界

- 媒体控制依赖 Windows 全局媒体会话，应用未暴露会话时不可用。
- CPU / GPU 温度为轻量展示数据，不是完整硬件监控面板。
- 亮度控制依赖 `WmiMonitorBrightness` 接口，台式机外接显示器可能不可用。
- 通知聚合目前为内置演示，尚未接入完整插件系统。