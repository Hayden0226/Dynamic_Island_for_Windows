# Dynamic Island 二阶段构建脚本
# 使用 D:\VS根 的 MSVC 与 D:\Windows Kits 的 Windows SDK
$ErrorActionPreference = "Stop"

$msvc = "D:\VS根\VC\Tools\MSVC\14.41.34120"
$sdk = "D:\Windows Kits\10"

$env:PATH = "$msvc\bin\Hostx64\x64;$sdk\bin\10.0.22621.0\x64;$env:USERPROFILE\.cargo\bin;$env:PATH"
$env:LIB = "$msvc\lib\x64;$sdk\Lib\10.0.22621.0\ucrt\x64;$sdk\Lib\10.0.22621.0\um\x64"
$env:INCLUDE = "$msvc\include;$sdk\Include\10.0.22621.0\ucrt;$sdk\Include\10.0.22621.0\um;$sdk\Include\10.0.22621.0\shared"

Set-Location (Join-Path $PSScriptRoot "")

if ($args[0] -eq "dev") {
    npm run tauri dev
} else {
    npm run tauri build
}
