# ComparePhotoInExploer

一款 Windows 资源管理器右键扩展，用于快速对比两张图片的差异。


## 功能特性

- **上下文菜单集成** - 在 Windows 资源管理器中选中 2 个图片文件后，右键菜单会显示"对比图片"选项
- **并排对比窗口** - 专用对比窗口，将两张图片并排显示进行比较
- **同步移动** - 拖动其中一张图片时，另一张会保持相对位置同步移动
- **智能缩放** - 支持以鼠标指针为中心缩放图片

## 快捷键

| 按键 | 功能 |
|------|------|
| 鼠标左键拖动 | 同步移动两张图片 |
| 滚轮 | 上下移动图片 |
| Ctrl + 滚轮 | 左右移动图片 |
| Alt + 滚轮 | 以鼠标指针为中心缩放图片 |

## 技术栈

- C# + WinForms (.NET 8)
- Windows Shell 扩展
- 高效图片渲染算法

## 安装

### 前置要求

- Windows 10 或更高版本
- .NET 8 Runtime

### 编译项目

```bash
git clone https://github.com/yourusername/ComparePhotoInExploer.git
cd ComparePhotoInExploer
dotnet build -c Release
```

### 注册右键菜单

以管理员身份运行 PowerShell 脚本：

```powershell
.\InstallRegistry.ps1
```

## 使用方法

1. 在 Windows 资源管理器中选择两个图片文件
2. 右键点击，选择 **对比图片**
3. 在对比窗口中使用快捷键调整图片位置和大小

## 项目结构

```
ComparePhotoInExploer/
├── ComparePhotoInExploer/
│   ├── Form1.cs                 # 主窗口
│   ├── Program.cs               # 程序入口
│   └── ComparePhotoInExploer.csproj
├── InstallRegistry.ps1          # 注册表安装脚本
├── .gitignore
└── README.md
```

## License

MIT License
