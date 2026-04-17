# 图片对比工具注册表安装脚本

# 获取脚本所在目录
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$exePath = Join-Path $scriptPath "ComparePhotoInExploer\bin\Release\net10.0-windows\ComparePhotoInExploer.exe"

# 检查可执行文件是否存在
if (-not (Test-Path $exePath)) {
    Write-Host "错误: 可执行文件不存在，请先编译项目。" -ForegroundColor Red
    exit 1
}

# 定义注册表路径
$regPath = "HKCU:\Software\Classes\SystemFileAssociations\image\shell\ComparePhotos"

# 创建注册表项
New-Item -Path $regPath -Force | Out-Null
New-Item -Path "$regPath\command" -Force | Out-Null

# 设置注册表值
Set-ItemProperty -Path $regPath -Name "MUIVerb" -Value "对比图片" -Force
Set-ItemProperty -Path $regPath -Name "Icon" -Value "$exePath" -Force
Set-ItemProperty -Path $regPath -Name "MultiSelectModel" -Value "Player" -Force
Set-ItemProperty -Path "$regPath\command" -Name "" -Value "\"$exePath\" \"%1\" \"%2\"" -Force

Write-Host "注册表安装成功！" -ForegroundColor Green
Write-Host "现在可以在资源管理器中选择两个图片文件，右键点击选择'对比图片'进行比较。" -ForegroundColor Yellow