# 图片对比工具 - 发布脚本
# 职责：升级版本号 → 提交全部改动 → 打 tag → 推送到远端。
# 实际打包由 GitHub Actions (release.yml) 完成，产出两个 zip：
#   - 框架依赖版  ComparePhotoInExploer-vX.Y.Z-win-x64.zip
#   - 自包含免安装 ComparePhotoInExploer-vX.Y.Z-win-x64-selfcontained.zip
param(
    [string]$NewVersion = "",
    [switch]$Yes
)

$ErrorActionPreference = "Stop"
$CSPROJ = "ComparePhotoInExploer\ComparePhotoInExploer.csproj"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  图片对比工具 - 发布（tag 触发 CI 构建）" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 从 csproj 读取当前版本号
[xml]$xml = [System.IO.File]::ReadAllText((Resolve-Path $CSPROJ).Path)
$currentVer = $xml.Project.PropertyGroup.Version
if (-not $currentVer) {
    Write-Host "[错误] 无法从 $CSPROJ 读取版本号！" -ForegroundColor Red
    exit 1
}

# 确定新版本号（支持 "1.2" 或 "1.2.0"，自动递增末段）
if ($NewVersion -eq "") {
    $parts = $currentVer.Split(".")
    if ($parts.Length -eq 2) {
        $newVer = "$($parts[0]).$([int]$parts[1] + 1)"
    } else {
        $newVer = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
    }
} else {
    $newVer = $NewVersion
}

$tagName = "v$newVer"

Write-Host "当前版本: $currentVer" -ForegroundColor White
Write-Host "新版本:   $newVer  (tag: $tagName)" -ForegroundColor Green
Write-Host ""

if (-not $Yes) {
    $confirm = Read-Host "确认发布 $tagName 并推送触发 CI? (y/N)"
    if ($confirm -ne "y" -and $confirm -ne "Y") {
        Write-Host "已取消。" -ForegroundColor Yellow
        exit 0
    }
}
Write-Host ""

# 校验 tag 是否已存在
$existing = git tag -l $tagName
if ($existing) {
    Write-Host "[错误] tag $tagName 已存在，请换版本号。" -ForegroundColor Red
    exit 1
}

# Step 1: 更新 csproj 版本号
Write-Host "[1/4] 更新版本号 -> $newVer" -ForegroundColor Yellow
$content = [System.IO.File]::ReadAllText((Resolve-Path $CSPROJ).Path)
$content = $content -replace '<Version>.*?</Version>', "<Version>$newVer</Version>"
[System.IO.File]::WriteAllText((Resolve-Path $CSPROJ).Path, $content)

# Step 2: 提交全部改动
Write-Host "[2/4] 提交改动..." -ForegroundColor Yellow
git add -A
$staged = git status --porcelain
if (-not $staged) {
    Write-Host "      无改动可提交（跳过 commit）。" -ForegroundColor DarkYellow
} else {
    git commit -m "release: $tagName"
}

# Step 3: 打 tag
Write-Host "[3/4] 打 tag $tagName ..." -ForegroundColor Yellow
git tag $tagName

# Step 4: 推送 commit + tag（tag 推送将触发 GitHub Actions 构建）
Write-Host "[4/4] 推送到远端..." -ForegroundColor Yellow
git push
git push origin $tagName

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  已推送 $tagName ，GitHub Actions 正在构建双包" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "前往 Actions 页面查看构建进度；完成后 Release 页会自动出现两个 zip。" -ForegroundColor Yellow
# 图片对比工具 - Release 打包脚本
param(
    [string]$NewVersion = ""
)

$ErrorActionPreference = "Stop"
$CSPROJ = "ComparePhotoInExploer\ComparePhotoInExploer.csproj"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  图片对比工具 - Release 打包脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 从 csproj 读取当前版本号
[xml]$xml = [System.IO.File]::ReadAllText((Resolve-Path $CSPROJ).Path)
$currentVer = $xml.Project.PropertyGroup.Version

if (-not $currentVer) {
    Write-Host "[错误] 无法从 $CSPROJ 读取版本号！" -ForegroundColor Red
    exit 1
}

# 确定新版本号
if ($NewVersion -eq "") {
    # 支持 "1.2" 或 "1.2.0" 格式，自动递增最后一段
    $parts = $currentVer.Split(".")
    if ($parts.Length -eq 2) {
        $newVer = "$($parts[0]).$([int]$parts[1] + 1)"
    } else {
        $patch = [int]$parts[2] + 1
        $newVer = "$($parts[0]).$($parts[1]).$patch"
    }
} else {
    $newVer = $NewVersion
}

Write-Host "当前版本: $currentVer" -ForegroundColor White
Write-Host "新版本:   $newVer" -ForegroundColor Green
Write-Host ""

# 确认
$confirm = Read-Host "确认发布 v$newVer? (y/N)"
if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "已取消。" -ForegroundColor Yellow
    exit 0
}
Write-Host ""

# Step 1: 更新 csproj 版本号
Write-Host "[1/3] 更新版本号..." -ForegroundColor Yellow
$content = [System.IO.File]::ReadAllText((Resolve-Path $CSPROJ).Path)
$content = $content -replace '<Version>.*?</Version>', "<Version>$newVer</Version>"
[System.IO.File]::WriteAllText((Resolve-Path $CSPROJ).Path, $content)
Write-Host "版本号已更新为 $newVer" -ForegroundColor Green
Write-Host ""

# Step 2: Publish
Write-Host "[2/3] 正在发布程序..." -ForegroundColor Yellow
if (Test-Path "publish") { Remove-Item -Recurse -Force "publish" }
dotnet publish $CSPROJ -c Release -r win-x64 --self-contained false -p:DebugType=none -p:DebugSymbols=false -o publish 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host "[错误] 发布失败！" -ForegroundColor Red
    exit 1
}
Write-Host "发布成功！" -ForegroundColor Green
Write-Host ""

# Step 3: 打包成 zip 压缩包
Write-Host "[3/3] 打包压缩包..." -ForegroundColor Yellow
$archiveName = "ComparePhotoInExploer-v$newVer.zip"
if (Test-Path $archiveName) { Remove-Item -Force $archiveName }

$publishDir = (Resolve-Path "publish").Path
Compress-Archive -Path "$publishDir\*" -DestinationPath "$((Resolve-Path ".").Path)\$archiveName" -CompressionLevel Optimal

Write-Host "打包成功！" -ForegroundColor Green
Write-Host ""

$sizeMB = [math]::Round((Get-Item $archiveName).Length / 1MB, 2)

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  打包完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "版本:     v$newVer"
Write-Host "输出文件: $archiveName (${sizeMB}MB)"
Write-Host ""
Write-Host "后续操作:" -ForegroundColor Yellow
Write-Host "  git push && git push --tags" -ForegroundColor White
Write-Host ""
