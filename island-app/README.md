# Dynamic Island for Windows — 第二阶段（Tauri）

采用 Vue 3 + TypeScript 承载界面与动画，Tauri 2 作为 IPC 边界，Rust + Windows API 负责窗口、媒体会话、系统数据与通知聚合。

## 目录结构

- `src/` — 前端界面、动画系统、状态与 IPC 封装
- `src-tauri/` — Rust 宿主、窗口管理与 Windows API 集成

## 开发

浏览器 mock 模式不需要 Tauri，可直接做视觉 QA：

```powershell
cd D:\Code\Dynamic_Island_for_Windows\island-app
npm install
npm run dev
```

打开 `http://127.0.0.1:1420/`。

## 构建与启动

本机使用 `D:\VS根` 的 MSVC 与 `D:\Windows Kits` 的 Windows SDK。直接运行构建脚本即可：

```powershell
cd D:\Code\Dynamic_Island_for_Windows\island-app
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

构建产物：

```
D:\Code\Dynamic_Island_for_Windows\island-app\src-tauri\target\release\dynamic-island-windows.exe
```

开发模式（带热更新，终端需保持打开）：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 dev
```

## 后台开销策略

- 收起态不轮询媒体与系统数据，Rust 侧线程仅以 500ms/1000ms 间隔检查窗口状态。
- 展开态每 2s 推送一次有变化的媒体与系统快照，媒体控制按需调用系统会话。
- 前端在 Tauri 模式下只依赖事件推送，浏览器 mock 模式才启用轮询。
