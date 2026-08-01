# ============================================================
# HopeFileLocker 一键发布 + 打包为绿色 zip
# 用法：在 VS2026 的 Developer PowerShell 中，进入工程目录后执行
#       .\publish-zip.ps1
# 生成的 HopeFileLocker-1.0.0-win-x64.zip 可上传到 GitHub Release 附件。
# ============================================================
$ErrorActionPreference = "Stop"

$version = "1.0.0"
$rid     = "win-x64"

# 1) 发布自包含程序（用户无需安装 .NET Runtime 即可运行）
Write-Host "[1/2] 发布自包含程序 ..." -ForegroundColor Cyan
dotnet publish HopeFileLocker.csproj -c Release -r $rid --self-contained true -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败。" }

# 2) 压缩为可携 zip（解压即运行，绿色便携版）
$zip = "HopeFileLocker-$version-$rid.zip"
Write-Host "[2/2] 压缩为 $zip ..." -ForegroundColor Cyan
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "publish\*" -DestinationPath $zip -CompressionLevel Optimal

Write-Host "完成：已生成 $zip，可上传到 GitHub Release 附件。" -ForegroundColor Green
