@echo off
chcp 65001 > nul
echo ========================================
echo 更新部署文件和配置
echo ========================================
echo.

echo [1/5] 编译项目...
dotnet build P2P.sln --configuration Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo ❌ 编译失败！
    pause
    exit /b 1
)
echo ✅ 编译成功
echo.

echo [2/5] 创建 TestDeploy 目录...
if not exist TestDeploy\Server mkdir TestDeploy\Server
if not exist TestDeploy\ServiceProvider mkdir TestDeploy\ServiceProvider
if not exist TestDeploy\AccessClient mkdir TestDeploy\AccessClient
echo ✅ 目录创建完成
echo.

echo [3/5] 复制最新编译文件...
rem 复制服务器
copy /Y bin\Release\net8.0\P2PServer.exe TestDeploy\Server\
copy /Y bin\Release\net8.0\P2PServer.dll TestDeploy\Server\
copy /Y bin\Release\net8.0\P2PServer.pdb TestDeploy\Server\
copy /Y bin\Release\net8.0\P2PServer.runtimeconfig.json TestDeploy\Server\
copy /Y bin\Release\net8.0\P2PServer.deps.json TestDeploy\Server\

rem 复制客户端到服务提供端
copy /Y bin\Release\net8.0\P2PClient.exe TestDeploy\ServiceProvider\
copy /Y bin\Release\net8.0\P2PClient.dll TestDeploy\ServiceProvider\
copy /Y bin\Release\net8.0\P2PClient.pdb TestDeploy\ServiceProvider\
copy /Y bin\Release\net8.0\P2PClient.runtimeconfig.json TestDeploy\ServiceProvider\
copy /Y bin\Release\net8.0\P2PClient.deps.json TestDeploy\ServiceProvider\

rem 复制客户端到访问客户端
copy /Y bin\Release\net8.0\P2PClient.exe TestDeploy\AccessClient\
copy /Y bin\Release\net8.0\P2PClient.dll TestDeploy\AccessClient\
copy /Y bin\Release\net8.0\P2PClient.pdb TestDeploy\AccessClient\
copy /Y bin\Release\net8.0\P2PClient.runtimeconfig.json TestDeploy\AccessClient\
copy /Y bin\Release\net8.0\P2PClient.deps.json TestDeploy\AccessClient\
echo ✅ 文件复制完成
echo.

echo [4/5] 复制配置文件...
copy /Y server_config_DEBUG.json TestDeploy\Server\server_config.json
copy /Y client_config_服务端_DEBUG.json TestDeploy\ServiceProvider\client_config.json
copy /Y client_config_访问端_DEBUG.json TestDeploy\AccessClient\client_config.json
echo ✅ 配置文件复制完成
echo.

echo [5/5] 创建启动脚本...
echo @echo off > TestDeploy\Server\启动服务器.bat
echo chcp 65001 ^>nul >> TestDeploy\Server\启动服务器.bat
echo title P2P 服务器端 >> TestDeploy\Server\启动服务器.bat
echo cls >> TestDeploy\Server\启动服务器.bat
echo echo ======================================== >> TestDeploy\Server\启动服务器.bat
echo echo   P2P 服务器端 - DEBUG 模式 >> TestDeploy\Server\启动服务器.bat
echo echo ======================================== >> TestDeploy\Server\启动服务器.bat
echo echo. >> TestDeploy\Server\启动服务器.bat
echo P2PServer.exe >> TestDeploy\Server\启动服务器.bat
echo pause >> TestDeploy\Server\启动服务器.bat

echo @echo off > TestDeploy\ServiceProvider\启动服务提供端.bat
echo chcp 65001 ^>nul >> TestDeploy\ServiceProvider\启动服务提供端.bat
echo title 服务提供端 >> TestDeploy\ServiceProvider\启动服务提供端.bat
echo cls >> TestDeploy\ServiceProvider\启动服务提供端.bat
echo echo ======================================== >> TestDeploy\ServiceProvider\启动服务提供端.bat
echo echo   服务提供端 - DEBUG 模式 >> TestDeploy\ServiceProvider\启动服务提供端.bat
echo echo ======================================== >> TestDeploy\ServiceProvider\启动服务提供端.bat
echo echo. >> TestDeploy\ServiceProvider\启动服务提供端.bat
echo P2PClient.exe >> TestDeploy\ServiceProvider\启动服务提供端.bat
echo pause >> TestDeploy\ServiceProvider\启动服务提供端.bat

echo @echo off > TestDeploy\AccessClient\启动访问客户端.bat
echo chcp 65001 ^>nul >> TestDeploy\AccessClient\启动访问客户端.bat
echo title 访问客户端 >> TestDeploy\AccessClient\启动访问客户端.bat
echo cls >> TestDeploy\AccessClient\启动访问客户端.bat
echo echo ======================================== >> TestDeploy\AccessClient\启动访问客户端.bat
echo echo   访问客户端 - DEBUG 模式 >> TestDeploy\AccessClient\启动访问客户端.bat
echo echo ======================================== >> TestDeploy\AccessClient\启动访问客户端.bat
echo echo. >> TestDeploy\AccessClient\启动访问客户端.bat
echo P2PClient.exe >> TestDeploy\AccessClient\启动访问客户端.bat
echo pause >> TestDeploy\AccessClient\启动访问客户端.bat
echo ✅ 启动脚本创建完成
echo.

echo ========================================
echo ✅ 所有文件更新完成！
echo ========================================
echo.
echo 测试目录：TestDeploy\
echo   ├── Server\              [服务器端 - 修复后]
echo   ├── ServiceProvider\     [服务提供端 - 修复后]
echo   └── AccessClient\        [访问客户端 - 修复后]
echo.
echo ========================================
echo 下一步测试：
echo ========================================
echo.
echo 打开 3 个命令行窗口：
echo.
echo 窗口1:
echo   cd TestDeploy\Server
echo   .\启动服务器.bat
echo.
echo 窗口2:
echo   cd TestDeploy\ServiceProvider
echo   .\启动服务提供端.bat
echo.
echo 窗口3:
echo   cd TestDeploy\AccessClient
echo   .\启动访问客户端.bat
echo.
echo ========================================
echo 配置信息（已匹配）：
echo ========================================
echo 组ID: 测试组1
echo 密钥: test123
echo 服务器: 127.0.0.1:8000
echo 日志级别: DEBUG
echo ========================================
pause
