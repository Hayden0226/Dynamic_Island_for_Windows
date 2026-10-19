# Dynamic Island for Windows

一个面向 Windows 桌面的轻量「灵动岛」。第一阶段聚焦视觉质感、连续动画和低资源占用，采用原生 WPF 实现；第二阶段规划迁移到 Vue 3 + TypeScript + Tauri 2 + Rust，扩展更完整的系统控制能力。

<img src="docs/island-collapsed.png" width="440" alt="收起状态">

<img src="docs/island-expand-620.gif" width="620" alt="展开动画">

<img src="docs/island-expanded.png" width="620" alt="展开状态">

## 当前能力

- `240 x 48` 胶囊收起态，显示时间与当前媒体封面。
- `380 x 150` Peek 态，切歌时短暂展示歌曲信息后自动回落。
- `380 x 220` 展开态，包含媒体控制、CPU / RAM / 电池状态和 25 分钟专注计时。
- 三态共享同一套尺寸、圆角、透明度和内容位移动画，不叠加独立弹跳效果。
- 支持保持展开、减少动态效果、开机启动与右键菜单。
- 检测全屏应用后隐藏置顶窗口，退出全屏后恢复。
- 处理 DPI 变化、显示器工作区变化、休眠唤醒和媒体会话重启。

## 交互

| 操作 | 行为 |
| --- | --- |
| 鼠标进入 | 从收起或 Peek 平滑展开 |
| 鼠标离开 | 延迟约 900 ms 后收起 |
| 切歌 | 短暂进入 Peek，3.2 s 后自动收起 |
| 点击图钉 | 保持展开，忽略自动收起 |
| 右键 | 打开设置菜单 |

## 动画系统

所有结构动画由 `Core/IslandAnimator.cs` 统一调度。动画期间才订阅 `CompositionTarget.Rendering`，并用该帧的 `RenderingTime` 计算进度，结束后立即解除订阅，闲置时不保留动画刷新链路。

窗口本身固定为 `432 x 272`，动画只改变内部 squircle 的 `SurfaceWidth`、`SurfaceHeight` 和圆角。这样不会逐帧调用 `SetWindowPos`，也不会让 DWM、WPF 布局和透明窗口表面互相等待。内容层使用固定尺寸的 squircle 裁剪并提前完成布局，动画期间只更新透明度和位移动画，不再逐帧重建裁剪区域。

| 动画 | 时长与节奏 |
| --- | --- |
| 展开尺寸与圆角 | 360 ms，无过冲 S 形缓动 |
| 收起尺寸与圆角 | 340 ms，无过冲 S 形缓动 |
| Peek 展开 | 340 ms，同一套结构缓动 |
| 展开内容淡入 | 延迟 160 ms，持续 180 ms，同时上移 3 px |
| 展开播放控制 | 延迟 230 ms，持续 150 ms |
| 底部状态区淡入 | 延迟 260 ms，持续 150 ms |
| 收起内容退场 | 120 ms，先淡出再继续收缩 |
| 收起时钟淡入 | 延迟 180 ms，持续 150 ms |
| 收起播放控制与底部 | 90 ms，快速退场 |
| 换歌交叉淡入 | 120 ms，文字位移仅 1.5-2.5 px |
| 按钮悬停反馈 | 80 ms |
| 进度条 | 100 ms 平滑追随，播放时每 100 ms 更新 |
| 设置菜单进入 | 110 ms 淡入并上移 3 px |

设计约束：

- 所有状态使用同一套 squircle 几何，`CornerSmoothing` 固定为 `0.6`。
- 结构尺寸、圆角和内容透明度由同一条时间线计算，但内容按「先让出空间、再出现」的顺序错峰。
- 结构曲线采用 `Easing.Balanced`（`cubic-bezier(0.32, 0, 0.24, 1)`），起止连续、速度分布均匀，不做回弹或过冲。
- 展开时先完成大部分形变，再让媒体信息、控制按钮和底部状态依次淡入；收起时内容先退出，表面再收缩，避免文字被逐帧裁切。
- 阴影分两层并直接跟随当前 squircle 曲面：大半径、低透明度的环境投影负责“浮起”，小半径、浅偏移的接触投影保留收起态的细腻描边感；两层都关闭 `ClipContent`，避免投影被曲面裁掉后退化成硬灰边。
- 开启「减少动态效果」后取消空间位移，并使用约 100 ms 的快速淡变。

## 架构

```text
App.xaml / App.xaml.cs              应用入口、单实例与异常日志
MainWindow.xaml                     视觉层与交互控件
MainWindow.xaml.cs                  状态机、媒体 UI 与定时器编排
Core/IslandAnimator.cs              三态结构与内容动画调度
Core/IslandMetrics.cs               尺寸、时长与间距基准
Controls/SquircleBorder.cs          曲率一致的 squircle 容器
Controls/SquircleProgressBar.cs     按几何宽度绘制的圆角进度条
Services/MediaSessionService.cs     Windows 媒体会话与事件通知
Services/SystemStatusService.cs     展开态 CPU / RAM / 电池采样
Services/FullscreenWatcher.cs       全屏应用检测与降级
Services/WindowPlacementService.cs  DPI、工作区与窗口定位
```

第一阶段刻意不将硬件采样、媒体查询和 UI 动画放入同一条高频更新链路：

- 收起态不启动系统状态轮询。
- 状态区仅在展开时每 2 s 更新一次。
- 播放进度仅在展开且正在播放时每秒推进。
- 时钟按下一分钟对齐，不进行秒级重绘。
- 媒体信息优先依赖系统媒体会话事件，而不是持续查询播放器。

## 性能基线

以下为发布版在本机 20 逻辑处理器环境下的实测数据，用于后续回归对比，不作为跨设备绝对指标：

| 状态 | 平均 CPU（占整机） | 平均 CPU（占单核） | 工作集 | 私有内存 |
| --- | --- | --- | --- | --- |
| 收起静置 30 s | 约 0.004% | 约 0.10% | 约 57 MB | 约 140 MB |
| 展开静置 20 s | 约 0.042% | 约 1.01% | 约 69 MB | 约 116 MB |

测试时媒体正在播放。工作集会随系统内存压力和 DWM 回收策略变化，后续优化应继续记录空闲 CPU、唤醒频率、内存和动画帧率，而不是只关注单一数字。

## 构建与运行

环境要求：

- Windows 10 版本 2004（Build 19041）或更高。
- .NET 10 SDK，或运行已发布版本时安装 .NET 10 Desktop Runtime。

```powershell
dotnet build .\DynamicIsland.csproj -c Release
dotnet run --project .\DynamicIsland.csproj -c Release
```

发布为框架依赖版本：

```powershell
dotnet publish .\DynamicIsland.csproj -c Release -r win-x64 --self-contained false -o .\publish
```

发布后运行：

```powershell
.\publish\DynamicIsland.exe
```

## 可靠性

- 单实例互斥，避免重复创建置顶窗口。
- 未处理异常写入 `%APPDATA%\DynamicIsland\logs\crash.log`。
- 全屏应用期间隐藏窗口并取消置顶，退出全屏后重新定位。
- 显示器切换、DPI 变化和休眠唤醒后重新计算工作区与窗口位置。
- 媒体会话失效后自动重启监听，不阻塞 UI 线程。

## 已知边界

- 第一阶段基于 WPF，尚未拆分为 Tauri 前后端。
- 媒体控制依赖 Windows 全局媒体会话，应用未暴露会话时不可用。
- CPU / RAM 为轻量展示数据，不是完整性能监控面板。
- 专注计时目前不保存跨进程历史。
- 全屏检测面向常规桌面与游戏窗口，特殊渲染模式仍可能需要单独适配。

## 第二阶段路线

- 使用 Vue 3 + TypeScript 承载界面、设置和动画系统。
- 使用 Tauri 2 IPC 建立明确的事件与权限边界。
- 使用 Rust + Windows API 实现窗口管理、媒体会话、快捷键与系统数据。
- 增加插件与通知聚合能力，同时保持收起态接近零后台开销。
