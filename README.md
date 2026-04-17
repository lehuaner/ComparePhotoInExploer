# ComparePhotoInExploer - 图片对比工具

一款轻量级的 Windows 图片对比工具，支持多图网格布局、同步拖动、同步缩放和同步滚动，方便用户快速对比多张图片的差异。

## 功能特性

### 核心功能
- **多图网格对比**：支持 1-9 张图片同时对比，自动计算最优网格布局
  - 1 张：1×1 单图显示
  - 2 张：1×2 左右分屏
  - 3-4 张：2×2 网格
  - 5-6 张：2×3 网格
  - 7-9 张：3×3 网格
- **支持不同比例图片**：每张图片独立计算缩放比例，竖图、横图、不同尺寸图片均可正确对比
- **透明背景显示**：采用棋盘格背景展示透明区域
- **自适应缩放**：自动缩放图片以适应各自区域大小（保持宽高比）
- **图片缓存**：图片加载至内存缓存，避免重复读取磁盘

### 同步操作
- **同步拖动**：鼠标左键拖动时所有图片同步移动
- **同步缩放**：Alt + 滚轮以鼠标位置为中心缩放，所有图片同步变化
- **同步滚动**：滚轮上下移动、Ctrl + 滚轮左右移动

### 系统集成
- 支持 Windows 资源管理器右键菜单集成
- 可直接从资源管理器选中多张图片启动对比

## 系统要求

- Windows 10/11
- .NET 10.0 Runtime
- 支持 1-9 个图片文件路径作为命令行参数（路径之间用空格分隔）

## 安装部署

### 方式一：编译运行

```bash
# 克隆项目后，在项目目录下执行
dotnet build -c Release
dotnet run -c Release
```

### 方式二：注册右键菜单

1. 先编译项目：
```bash
dotnet build -c Release
```

2. 以管理员权限运行注册脚本：
```powershell
.\InstallRegistry.ps1
```

3. 在资源管理器中选中多张图片 → 右键 → 选择「对比图片」

### 卸载右键菜单

```powershell
# 删除注册表项
Remove-Item -Path "HKCU:\Software\Classes\SystemFileAssociations\image\shell\ComparePhotos" -Recurse -Force
```

## 使用方法

### 命令行启动

```bash
ComparePhotoInExploer.exe "图片路径1" "图片路径2" ...
```

示例：
```bash
# 对比两张图片
ComparePhotoInExploer.exe "D:\Photos\before.png" "D:\Photos\after.png"

# 对比多张图片
ComparePhotoInExploer.exe "D:\Photos\img1.png" "D:\Photos\img2.png" "D:\Photos\img3.png"
```

### 右键菜单启动

1. 在资源管理器中选中多张图片
2. 右键 → 选择「对比图片」

### 操作指南

| 操作 | 功能 |
|------|------|
| 鼠标左键拖动 | 同步移动所有图片 |
| 滚轮 | 上下滚动图片 |
| Ctrl + 滚轮 | 左右滚动图片 |
| Alt + 滚轮 | 以鼠标位置为中心缩放所有图片 |
| 点击左上角 ▶ 按钮 | 显示/隐藏操作说明 |

### 显示状态说明

- 默认显示「▶」按钮，点击后显示「▼」按钮及操作说明
- 图片以 fit-to-window 方式在各自分割区域内自适应显示
- 每张图片独立计算缩放比例，确保不同尺寸图片显示效果一致
- 多图模式自动绘制网格分割线

## 项目结构

```
ComparePhotoInExploer/
├── Form1.cs              # 主窗口逻辑
├── Form1.Designer.cs    # 窗体设计器代码
├── Program.cs           # 程序入口
├── ComparePhotoInExploer.csproj   # 项目配置
└── InstallRegistry.ps1  # 注册表安装脚本
```

## 技术实现

### 核心技术点

- **WinForms** - 图形用户界面
- **双缓冲** - 减少绘制闪烁
- **棋盘格纹理** - TextureBrush 绘制透明背景
- **归一化坐标变换** - 实现以鼠标为中心的精准缩放
- **内存缓存** - Image 对象缓存避免重复加载

### 关键算法

1. **自适应缩放**：`Math.Min(区域宽度/图片宽度, 区域高度/图片高度)`
2. **同步缩放**：基于归一化坐标（0~1比例位置）计算缩放前后偏移量，以鼠标为中心保持缩放位置不变
3. **可见区域裁剪**：DrawImage 时计算交叉矩形并映射源图片区域
4. **网格布局计算**：根据图片数量自动计算最优行列数（1→1×1, 2→1×2, 3-4→2×2, 5-6→2×3, 7-9→3×3）

## 开发说明

### 环境要求
- .NET 10.0 SDK
- Windows 10/11

### 编译命令

```bash
# Debug 编译
dotnet build

# Release 编译
dotnet build -c Release

# 运行
dotnet run
```

### 运行测试

项目包含测试图片 `test1.png` 和 `test2.png`，可通过以下方式测试：

```bash
# 测试两张图片
dotnet run -c Release -- "..\test1.png" "..\test2.png"

# 测试单张图片
dotnet run -c Release -- "..\test1.png"
```

## 许可证

本项目仅供学习交流使用。
