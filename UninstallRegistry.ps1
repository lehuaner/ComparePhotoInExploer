#Requires -RunAsAdministrator
# 图片对比工具 - 卸载右键菜单脚本

# 设置控制台编码为UTF-8，防止中文乱码
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  图片对比工具 - 右键菜单卸载程序" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 定义注册表路径
$regPath = "HKCU:\Software\Classes\SystemFileAssociations\image\shell\ComparePhotos"

# 检查注册表项是否存在
if (Test-Path $regPath) {
    # 验证是否是"对比图片"
    $verb = (Get-ItemProperty -Path $regPath -Name "MUIVerb" -ErrorAction SilentlyContinue).MUIVerb
    Write-Host "找到右键菜单项: $verb" -ForegroundColor Yellow

    # 删除注册表项
    Remove-Item -Path $regPath -Recurse -Force

    # 刷新 Windows 图标缓存，使右键菜单变更立即生效
    try {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class ShellNotifyUninstall {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
"@
        [ShellNotifyUninstall]::SHChangeNotify(0x08000000, 0x1000, [IntPtr]::Zero, [IntPtr]::Zero)
    } catch {}

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  卸载成功！" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "右键菜单中的'对比图片'已被移除。" -ForegroundColor Yellow
} else {
    Write-Host "未找到'对比图片'右键菜单项，无需卸载。" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "按回车键退出..." -ForegroundColor Gray -NoNewline
[void][Console]::ReadLine()
