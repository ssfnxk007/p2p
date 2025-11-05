@echo off
chcp 65001 > nul
echo ========================================
echo P2P 系统 DEBUG 模式测试
echo ========================================
echo.
echo 请按以下步骤操作：
echo.
echo 1. 打开 3 个命令行窗口
echo.
echo 2. 窗口1 - 启动服务器（DEBUG模式）：
echo    cd ServerDeploy_Standalone
echo    copy ..\server_config_DEBUG.json server_config.json
echo    .\P2PServer.exe
echo.
echo 3. 窗口2 - 启动服务提供端（DEBUG模式）：
echo    cd ClientDeploy_Standalone
echo    copy ..\client_config_服务端_DEBUG.json client_config.json
echo    .\P2PClient.exe
echo.
echo 4. 窗口3 - 启动访问客户端（DEBUG模式）：
echo    cd ClientDeploy_Standalone
echo    copy ..\client_config_访问端_DEBUG.json client_config.json
echo    .\P2PClient.exe
echo.
echo ========================================
echo DEBUG 日志将保存在 logs 目录下
echo ========================================
pause
