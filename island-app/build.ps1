# Dynamic Island 二阶段构建脚本
$ErrorActionPreference = "Stop"

$vcvars = "D:\VS根\VC\Auxiliary\Build\vcvars64.bat"
if (Test-Path $vcvars) {
    $lines = & cmd.exe /c ('call "' + $vcvars + '" >nul && set')
    foreach ($line in $lines) {
        $index = $line.IndexOf('=')
        if ($index -gt 0) {
            $name = $line.Substring(0, $index)
            Set-Item -Path "Env:$name" -Value $line.Substring($index + 1)
        }
    }
} else {
    throw "未找到 vcvars64.bat：$vcvars"
}

Set-Location $PSScriptRoot

if ($args[0] -eq "dev") {
    npm run tauri dev
} else {
    npm run tauri build

    $version = (Get-Content (Join-Path $PSScriptRoot "package.json") -Raw | ConvertFrom-Json).version
    $bundle = Join-Path $PSScriptRoot "src-tauri\target\release\bundle\nsis"
    $installer = Get-ChildItem $bundle -Filter "*-setup.exe" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($installer) {
        $destination = Join-Path (Split-Path $PSScriptRoot -Parent) "Dynamic-Island-$version-Setup.exe"
        Copy-Item $installer.FullName $destination -Force
        Write-Host "Installer: $destination"
    }
}
