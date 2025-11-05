@echo off
chcp 65001 >nul
echo ========================================
echo   编译 P2P 系统
echo ========================================
echo.
echo 请使用以下方式之一编译：
echo.
echo ═══════════════════════════════════════
echo 方式1：Visual Studio（推荐）
echo ═══════════════════════════════════════
echo.
echo 1. 打开 Visual Studio 2019/2022
echo 2. 文件 → 新建 → 项目
echo 3. 选择 "控制台应用(.NET Framework)"
echo 4. 目标框架选择 ".NET Framework 4.7.2" 或更高
echo 5. 右键项目 → 添加 → 现有项
echo 6. 添加以下文件：
echo.
echo    服务器项目：
echo      • P2PServerMain.cs
echo      • P2PServer.cs
echo      • P2PConfig.cs
echo.
echo    客户端项目：
echo      • P2PClient.cs
echo      • P2PPuncher.cs
echo      • P2PConfig.cs
echo      • PortForwarder.cs
echo.
echo 7. 生成 → 生成解决方案
echo.
echo ═══════════════════════════════════════
echo 方式2：使用在线编译器
echo ═══════════════════════════════════════
echo.
echo 访问: https://dotnetfiddle.net/
echo 粘贴代码并运行
echo.
echo ═══════════════════════════════════════
echo 方式3：安装 .NET SDK（一次性设置）
echo ═══════════════════════════════════════
echo.
echo 1. 下载: https://dotnet.microsoft.com/download
echo 2. 安装 .NET 6.0 SDK 或更高版本
echo 3. 重新运行此脚本
echo.
echo ═══════════════════════════════════════
echo.
pause

echo ========================================
echo   编译完成！
echo ========================================
echo.
echo 生成的文件：
echo   • P2PServer.exe - 服务器程序
echo   • P2PClient.exe - 客户端程序
echo.
echo 下一步：
echo   1. 编辑 server_config.json 配置服务器
echo   2. 编辑 client_config.json 配置客户端
echo   3. 运行 P2PServer.exe 启动服务器
echo   4. 运行 P2PClient.exe 启动客户端
echo.
pause
