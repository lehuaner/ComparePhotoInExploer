#Requires -RunAsAdministrator
# 图片对比工具 - 注册右键菜单脚本

# 设置控制台编码为UTF-8，防止中文乱码
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# 获取脚本所在目录 - 使用 PWD 避免中文路径乱码
$scriptPath = $PWD.Path
$projectDir = Join-Path $scriptPath "ComparePhotoInExploer"
$exePath = Join-Path $scriptPath "ComparePhotoInExploer\bin\Release\net10.0-windows\ComparePhotoInExploer.exe"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  图片对比工具 - 右键菜单安装程序" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 第一步：编译项目
Write-Host "[1/3] 正在编译项目..." -ForegroundColor Yellow
$buildResult = & dotnet build $projectDir -c Release 2>&1
$buildExit = $LASTEXITCODE

if ($buildExit -ne 0) {
    Write-Host "编译失败！请检查项目是否正确。" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}

Write-Host "编译成功！" -ForegroundColor Green

# 第二步：检查可执行文件
Write-Host ""
Write-Host "[2/3] 检查可执行文件..." -ForegroundColor Yellow

if (-not (Test-Path $exePath)) {
    Write-Host "错误: 编译后未找到可执行文件: $exePath" -ForegroundColor Red
    exit 1
}

Write-Host "可执行文件路径: $exePath" -ForegroundColor Gray
Write-Host "检查通过！" -ForegroundColor Green

# 第三步：注册右键菜单
Write-Host ""
Write-Host "[3/3] 注册右键菜单..." -ForegroundColor Yellow

# 注册到图片文件的右键菜单
$regPath = "HKCU:\Software\Classes\SystemFileAssociations\image\shell\ComparePhotos"

# 删除旧项（如果存在）
if (Test-Path $regPath) {
    Remove-Item -Path $regPath -Recurse -Force
}

# 创建注册表项
New-Item -Path $regPath -Force | Out-Null
New-Item -Path "$regPath\command" -Force | Out-Null

# 设置注册表值
Set-ItemProperty -Path $regPath -Name "MUIVerb" -Value "对比图片" -Force
# 图标直接使用项目目录中的 app.ico（csproj 已配置复制到输出目录）
$icoPath = Join-Path $scriptPath "ComparePhotoInExploer\app.ico"
if (Test-Path $icoPath) {
    Set-ItemProperty -Path $regPath -Name "Icon" -Value $icoPath -Force
} else {
    # 回退到exe图标
    Set-ItemProperty -Path $regPath -Name "Icon" -Value "$exePath,0" -Force
}
Set-ItemProperty -Path $regPath -Name "MultiSelectModel" -Value "Player" -Force

# 命令行：只传 %1，多选时 Windows 会为每个文件启动一个实例，
# 程序通过 Mutex + Named Pipe 实现单实例，将多个文件合并到同一窗口
Set-ItemProperty -Path "$regPath\command" -Name "(Default)" -Value "`"$exePath`" `"%1`"" -Force

# 验证注册结果
$verifyVerb = (Get-ItemProperty -Path $regPath -Name "MUIVerb" -ErrorAction SilentlyContinue).MUIVerb
$verifyCmd = (Get-ItemProperty -Path "$regPath\command" -Name "(Default)" -ErrorAction SilentlyContinue).'(Default)'

# 刷新 Windows 图标缓存，使右键菜单图标立即生效
Write-Host ""
Write-Host "正在刷新图标缓存..." -ForegroundColor Yellow
try {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class ShellNotify {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
"@
    # SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0x0000
    [ShellNotify]::SHChangeNotify(0x08000000, 0x1000, [IntPtr]::Zero, [IntPtr]::Zero)
    Write-Host "图标缓存已刷新" -ForegroundColor Green
} catch {
    Write-Host "图标缓存刷新失败（不影响功能，重启资源管理器后生效）" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  安装成功！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "注册表验证:" -ForegroundColor Cyan
Write-Host "  菜单名称: $verifyVerb" -ForegroundColor White
Write-Host "  注册路径: $regPath" -ForegroundColor Gray
Write-Host "  命令行:   $verifyCmd" -ForegroundColor Gray
Write-Host ""
Write-Host "使用方法: 在资源管理器中选择多个图片文件(最多9个)，右键点击选择'对比图片'进行比较。" -ForegroundColor Yellow
Write-Host ""
Write-Host "按回车键退出..." -ForegroundColor Gray -NoNewline
[void][Console]::ReadLine()
