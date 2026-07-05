param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$Project = "src/Azw3Reader.App"
$OutputDir = "dist/Azw3Reader-$Version"

Write-Host "=== 构建 $Version 版本 ===" -ForegroundColor Cyan

# 清理旧的 dist
if (Test-Path dist) { Remove-Item -Recurse -Force dist }

# 发布 win-x64（非单文件，避免首次启动解压/校验开销）
Write-Host "[1/3] 发布 win-x64 ..." -ForegroundColor Yellow
dotnet publish $Project -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -o "$OutputDir/win-x64"

# 清理无用文件（pdb、xml 文档）
Write-Host "[2/3] 清理无用文件 ..." -ForegroundColor Yellow
Get-ChildItem "$OutputDir/win-x64" -Recurse -Include *.pdb,*.xml | Remove-Item -Force

# 打包
Write-Host "[3/3] 打包 ..." -ForegroundColor Yellow
Compress-Archive -Path "$OutputDir/win-x64/*" -DestinationPath "$OutputDir-win-x64.zip"

Write-Host "=== 完成 ===" -ForegroundColor Green
Write-Host ""
Write-Host "输出:"
Write-Host "  目录: $OutputDir/win-x64/"
Write-Host "  ZIP : $OutputDir-win-x64.zip"

# 提示 gh release
Write-Host ""
Write-Host "上传到 GitHub Releases:" -ForegroundColor Cyan
Write-Host "  git tag v$Version"
Write-Host "  git push origin v$Version"
Write-Host "  gh release create v$Version $OutputDir-win-x64.zip --title ""v$Version"" --notes ""..."""
