@echo off
chcp 65001 > nul
echo ========================================
echo   P2P 系统 - 快速启动指南
echo ========================================
echo.
echo 📋 准备工作：
echo    确保已安装 .NET SDK 8.0 或更高版本
echo.
echo 🚀 测试步骤（按顺序打开3个窗口）：
echo.
echo ========================================
echo 窗口1 - 服务器：
echo ========================================
echo   cd d:\Ksa_p2p直联\TestDeploy\Server
echo   .\启动服务器.bat
echo.
echo   预期日志：
echo   ✅ 服务器启动在端口 8000
echo   支持的组: 测试组1
echo.
echo ========================================
echo 窗口2 - 服务提供端：
echo ========================================
echo   cd d:\Ksa_p2p直联\TestDeploy\ServiceProvider
echo   .\启动服务提供端.bat
echo.
echo   预期日志：
echo   ✅ 注册成功！组: 测试组1
echo   💓 心跳 [时间]
echo.
echo ========================================
echo 窗口3 - 访问客户端：
echo ========================================
echo   cd d:\Ksa_p2p直联\TestDeploy\AccessClient
echo   .\启动访问客户端.bat
echo.
echo   关键日志（修复后应该看到）：
echo   📥 [RAW] 收到 ... HEARTBEAT_OK:PEER_INFO:...
echo   ✅ 通过心跳获取节点信息: 服务提供端
echo   🎯 步骤2: 尝试 P2P 打洞...
echo.
echo ========================================
echo ⚠️ 重要说明：
echo ========================================
echo.
echo ✅ 启动方式：使用 dotnet 命令
echo    - 运行的是 .dll 文件（包含最新修复）
echo    - 需要系统安装 .NET Runtime
echo.
echo 🔧 如果遇到 "dotnet 不是内部或外部命令"：
echo    1. 下载安装 .NET SDK 8.0
echo    2. 网址: https://dotnet.microsoft.com/download
echo    3. 重启命令行窗口
echo.
echo 📊 配置信息（已匹配）：
echo    组ID: 测试组1
echo    密钥: test123
echo    日志级别: DEBUG
echo.
echo ========================================
pause
