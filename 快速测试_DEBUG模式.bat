@echo off
chcp 65001 > nul
echo ========================================
echo 快速编译并测试 DEBUG 模式
echo ========================================
echo.

echo [1/4] 编译项目...
dotnet build --configuration Release
if %ERRORLEVEL% NEQ 0 (
    echo ❌ 编译失败！
    pause
    exit /b 1
)
echo ✅ 编译成功
echo.

echo [2/4] 复制 DEBUG 配置文件...
copy server_config_DEBUG.json ServerDeploy_Standalone\server_config.json /Y
copy client_config_访问端_DEBUG.json ClientDeploy_Standalone\client_config_访问端.json /Y
copy client_config_服务端_DEBUG.json ClientDeploy_Standalone\client_config_服务端.json /Y
echo ✅ 配置文件已复制
echo.

echo [3/4] 创建日志目录...
if not exist logs mkdir logs
if not exist ServerDeploy_Standalone\logs mkdir ServerDeploy_Standalone\logs
if not exist ClientDeploy_Standalone\logs mkdir ClientDeploy_Standalone\logs
echo ✅ 日志目录已创建
echo.

echo [4/4] 测试说明
echo ========================================
echo 请打开 3 个新的命令行窗口，分别运行：
echo.
echo 窗口1 - 服务器：
echo   cd ServerDeploy_Standalone
echo   .\P2PServer.exe
echo.
echo 窗口2 - 服务提供端：
echo   cd ClientDeploy_Standalone
echo   copy client_config_服务端.json client_config.json
echo   .\P2PClient.exe
echo.
echo 窗口3 - 访问客户端：
echo   cd ClientDeploy_Standalone
echo   copy client_config_访问端.json client_config.json
echo   .\P2PClient.exe
echo.
echo ========================================
echo 关键日志检查：
echo.
echo ✅ 应该看到：
echo   [INFO] 📥 [RAW] 收到 xx 字节 from ...
echo   [INFO] ✅ 通过心跳获取节点信息...
echo.
echo ❌ 如果看到：
echo   [ERROR] ⚠️ 接收异常: ...
echo   请将完整日志发送给开发者
echo.
echo ========================================
echo 日志文件位置：
echo   服务器: ServerDeploy_Standalone\logs\server_DEBUG_yyyyMMdd.log
echo   客户端: ClientDeploy_Standalone\logs\*_DEBUG_yyyyMMdd.log
echo ========================================
pause
