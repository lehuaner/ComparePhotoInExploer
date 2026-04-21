# 项目文件指引

AI 在制作或修改功能时，优先查看或修改以下文件：

## Form1 Partial Class 文件（按职责拆分）

- `Form1.cs`：主窗体核心——字段定义、构造函数、WndProc、主题管理、窗口管理（最大化/还原/圆角）、IPC 通信（AddImagesFromExternal）
- `Form1.Paint.cs`：绘制逻辑——Paint 事件调度、棋盘格背景、图片绘制（DrawImage）、网格分割线、窗口边框圆角
- `Form1.MouseInteraction.cs`：鼠标交互——MouseDown/Move/Up、标题栏按钮点击与悬停检测、历史记录栏悬停、拖动/Shift拖动/重置偏移交互
- `Form1.Zoom.cs`：缩放与滚轮——MouseWheel 事件、同步对齐/独立缩放/关闭同步缩放三种模式、IsSyncZoomDisabled/IsSyncMoveDisabled、GetEffectiveZoom
- `Form1.History.cs`：历史记录管理（控制器层）——SaveCurrentToHistory、OnHistoryGroupClicked、OnHistoryGroupDeleteRequested、ClearCurrentImages；协调 HistoryData（数据层）和 HistoryBarData（视图层）
- `Form1.ImageLayout.cs`：图片加载与布局——GetGridLayout、LoadNewGroup、LoadImages、CalculateFitZoom、GetCellRect、HitTest、UpdateBaseZoom
- `Form1.Keyboard.cs`：键盘处理——KeyDown、ProcessCmdKey（Ctrl+W）、IsAltPressed
- `Form1.TitleBar.cs`：自绘标题栏——标题栏按钮绘制（历史记录、操作说明、主题、同步缩放、同步移动、缩放说明、右键菜单、重置偏移、最小化/最大化/关闭）
- `Form1.Overlays.cs`：浮层面板与提示——DrawHelpPanel、DrawZoomHelpPanel、DrawEmptyHint、DrawDropOverlay
- `Form1.DragDrop.cs`：拖放支持——DragEnter/Over/Leave/Drop 事件处理
- `Form1.Designer.cs`：窗体设计器生成的代码（一般无需手动修改）

## 其他模块

- `Program.cs`：程序入口、IPC 单实例通信（Named Pipe）、命令行参数处理
- `ThemeColorSet.cs`：主题颜色定义——暗色/亮色/跟随系统的全部颜色常量
- `AppSettings.cs`：用户设置持久化——主题、右键菜单开关、窗口位置/大小的保存与加载
- `HistoryData.cs`：历史记录数据层——历史组的增删改查、缩略图路径、去重排序、JSON 持久化
- `HistoryBarData.cs`：历史记录栏 UI 数据——展开/折叠状态、分组列表、缩略图缓存、绘制逻辑
- `ImageProcessor.cs`：图片处理——缩略图生成、Base64 编解码
- `ZoomCalculator.cs`：缩放计算——屏幕坐标与归一化坐标转换、同步对齐/独立缩放偏移计算
- `ResetOverlayHelper.cs`：重置偏移覆盖层——显示/隐藏、绘制各图偏移信息与缩略图、确认重置
- `RightClickMenuHelper.cs`：右键菜单集成——注册/注销 Windows 资源管理器右键菜单项
- `NativeMethods.cs`：Win32 API 声明——圆角窗口、WM_NCHITTEST、窗口消息等
- `CustomMessageBox.cs`：自定义消息框——替代 MessageBox 的主题适配弹窗
- `FileDropHelper.cs`：拖放辅助——从 DragEventArgs 中提取图片文件路径

## 按功能快速定位

| 功能 | 优先查看 |
|------|---------|
| 标题栏按钮增删 | `Form1.TitleBar.cs` → `Form1.MouseInteraction.cs`（点击处理） |
| 快捷键/操作说明 | `Form1.Keyboard.cs` → `Form1.Overlays.cs` |
| 缩放逻辑 | `ZoomCalculator.cs` → `Form1.Zoom.cs`（MouseWheel） |
| 拖动/移动逻辑 | `Form1.MouseInteraction.cs` → `Form1.Zoom.cs` |
| 主题/配色 | `ThemeColorSet.cs` → `Form1.cs`（ApplyTheme） → `AppSettings.cs` |
| 历史记录 | `Form1.History.cs` → `HistoryData.cs` → `HistoryBarData.cs` |
| 右键菜单 | `RightClickMenuHelper.cs` → `AppSettings.cs` |
| 窗口边框/圆角 | `NativeMethods.cs` → `Form1.cs`（WndProc） |
| 图片加载/布局 | `Form1.ImageLayout.cs` |
| 拖放文件 | `Form1.DragDrop.cs` → `FileDropHelper.cs` |
| 绘制/渲染 | `Form1.Paint.cs` → `Form1.Overlays.cs` → `Form1.TitleBar.cs` |
