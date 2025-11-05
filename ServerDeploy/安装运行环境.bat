@echo off
chcp 65001 >nul
echo ========================================
echo   P2P 服务器运行环境检查
echo ========================================
echo.

echo 正在检测系统环境...
echo.

:: 检查是否已安装 .NET 8.0 Runtime
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [×] 未检测到 .NET Runtime
    echo.
    echo 请按以下步骤手动安装：
    echo.
    echo 1. 使用 winget 安装（推荐）：
    echo    winget install Microsoft.DotNet.Runtime.8
    echo.
    echo 2. 或手动下载安装包：
    echo    https://dotnet.microsoft.com/download/dotnet/8.0
    echo    选择：.NET Runtime 8.0 - Windows x64
    echo.
) else (
    echo [✓] .NET Runtime 已安装
    dotnet --version
)

echo.
echo ========================================
echo 还需要安装 Visual C++ Redistributable
echo ========================================
echo.
echo 请手动下载并安装以下文件：
echo.
echo 下载地址：
echo https://aka.ms/vs/17/release/vc_redist.x64.exe
echo.
echo 或者在本机下载后复制到服务器安装
echo.
echo ========================================
echo 安装完成后，重启计算机即可运行 P2PServer.exe
echo ========================================
echo.
pause
