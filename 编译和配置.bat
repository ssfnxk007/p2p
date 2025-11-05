@echo off
chcp 65001 >nul
echo ========================================
echo   P2P 系统 - 编译和配置工具
echo ========================================
echo.

:: 检查 .NET SDK
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo ❌ 未检测到 .NET SDK
    echo.
    echo 请先安装 .NET SDK:
    echo https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

echo ✅ 检测到 .NET SDK
dotnet --version
echo.

:: 清理旧文件
echo ========================================
echo 步骤1: 清理旧编译文件
echo ========================================
if exist "TestDeploy" (
    echo 删除旧的测试目录...
    rmdir /s /q TestDeploy
)
echo ✅ 清理完成
echo.

:: 创建测试目录结构
echo ========================================
echo 步骤2: 创建测试目录
echo ========================================
mkdir TestDeploy
mkdir TestDeploy\Server
mkdir TestDeploy\ServiceProvider
mkdir TestDeploy\AccessClient
echo ✅ 目录创建完成
echo.

:: 编译服务器
echo ========================================
echo 步骤3: 编译服务器端
echo ========================================
dotnet build P2PServer.csproj -c Release -o TestDeploy\Server
if %errorlevel% neq 0 (
    echo ❌ 服务器端编译失败
    pause
    exit /b 1
)
echo ✅ 服务器端编译成功
echo.

:: 编译客户端
echo ========================================
echo 步骤4: 编译客户端
echo ========================================
dotnet build P2PClient.csproj -c Release -o TestDeploy\ServiceProvider
if %errorlevel% neq 0 (
    echo ❌ 客户端编译失败
    pause
    exit /b 1
)
echo ✅ 客户端编译成功
echo.

:: 复制客户端到访问端
echo ========================================
echo 步骤5: 复制客户端到访问端
echo ========================================
xcopy /Y /S TestDeploy\ServiceProvider\*.* TestDeploy\AccessClient\
echo ✅ 复制完成
echo.

:: 创建配置文件
echo ========================================
echo 步骤6: 创建配置文件
echo ========================================
echo 正在生成配置文件...

:: 服务器配置
(
echo {
echo   "ServerPort": 8000,
echo   "MaxClients": 1000,
echo   "Groups": [
echo     {
echo       "GroupID": "测试组1",
echo       "GroupKey": "test123",
echo       "Description": "测试用组"
echo     }
echo   ],
echo   "Logging": {
echo     "Level": "INFO",
echo     "LogToFile": true,
echo     "LogFilePath": "logs/server_{date}.log"
echo   },
echo   "Advanced": {
echo     "ClientTimeout": 30,
echo     "CleanupInterval": 10,
echo     "EnablePortForward": true
echo   }
echo }
) > TestDeploy\Server\server_config.json

:: 服务提供端配置
(
echo {
echo   "PeerID": "服务提供端",
echo   "GroupID": "测试组1",
echo   "GroupKey": "test123",
echo   "Servers": [
echo     "127.0.0.1"
echo   ],
echo   "ServerPort": 8000,
echo   "PortForwards": [],
echo   "Logging": {
echo     "Level": "INFO",
echo     "LogToFile": true,
echo     "LogFilePath": "logs/service_provider_{date}.log"
echo   },
echo   "Advanced": {
echo     "HeartbeatInterval": 1000,
echo     "PunchRetryCount": 30,
echo     "EnableP2P": true,
echo     "EnableRelay": true
echo   }
echo }
) > TestDeploy\ServiceProvider\client_config.json

:: 访问客户端配置
(
echo {
echo   "PeerID": "访问客户端",
echo   "GroupID": "测试组1",
echo   "GroupKey": "test123",
echo   "Servers": [
echo     "127.0.0.1"
echo   ],
echo   "ServerPort": 8000,
echo   "PortForwards": [
echo     {
echo       "Name": "测试连接",
echo       "LocalPort": 9999,
echo       "TargetPeerID": "服务提供端",
echo       "TargetPort": 9999,
echo       "Protocol": "TCP"
echo     }
echo   ],
echo   "Logging": {
echo     "Level": "INFO",
echo     "LogToFile": true,
echo     "LogFilePath": "logs/access_client_{date}.log"
echo   },
echo   "Advanced": {
echo     "HeartbeatInterval": 1000,
echo     "PunchRetryCount": 30,
echo     "EnableP2P": true,
echo     "EnableRelay": true
echo   }
echo }
) > TestDeploy\AccessClient\client_config.json

echo ✅ 配置文件创建完成
echo.

:: 创建启动脚本
echo ========================================
echo 步骤7: 创建启动脚本
echo ========================================

:: 服务器启动脚本
(
echo @echo off
echo chcp 65001 ^>nul
echo title P2P 服务器端
echo echo ========================================
echo echo   P2P 服务器端 - 监听端口 8000
echo echo ========================================
echo echo.
echo P2PServer.exe
echo pause
) > TestDeploy\Server\启动服务器.bat

:: 服务提供端启动脚本
(
echo @echo off
echo chcp 65001 ^>nul
echo title 服务提供端
echo echo ========================================
echo echo   服务提供端 - 提供服务
echo echo ========================================
echo echo.
echo P2PClient.exe
echo pause
) > TestDeploy\ServiceProvider\启动服务提供端.bat

:: 访问客户端启动脚本
(
echo @echo off
echo chcp 65001 ^>nul
echo title 访问客户端
echo echo ========================================
echo echo   访问客户端 - 访问服务
echo echo ========================================
echo echo.
echo P2PClient.exe
echo pause
) > TestDeploy\AccessClient\启动访问客户端.bat

echo ✅ 启动脚本创建完成
echo.

:: 创建说明文档
(
echo ========================================
echo   P2P 系统测试指南
echo ========================================
echo.
echo 目录结构：
echo   TestDeploy\
echo   ├── Server\              [服务器端]
echo   │   ├── P2PServer.exe
echo   │   ├── server_config.json
echo   │   └── 启动服务器.bat
echo   │
echo   ├── ServiceProvider\     [服务提供端]
echo   │   ├── P2PClient.exe
echo   │   ├── client_config.json
echo   │   └── 启动服务提供端.bat
echo   │
echo   └── AccessClient\        [访问客户端]
echo       ├── P2PClient.exe
echo       ├── client_config.json
echo       └── 启动访问客户端.bat
echo.
echo ========================================
echo 测试步骤（按顺序）：
echo ========================================
echo.
echo 1. 启动服务器端
echo    cd TestDeploy\Server
echo    .\启动服务器.bat
echo    
echo    预期日志：
echo    ✅ 服务器启动在端口 8000
echo    支持的组: 测试组1
echo.
echo 2. 启动服务提供端
echo    cd TestDeploy\ServiceProvider
echo    .\启动服务提供端.bat
echo    
echo    预期日志：
echo    ✅ 注册成功！组: 测试组1
echo    💓 心跳 [时间]
echo.
echo 3. 启动访问客户端
echo    cd TestDeploy\AccessClient
echo    .\启动访问客户端.bat
echo    
echo    关键日志观察：
echo    📡 步骤1: 查询目标节点公网地址...
echo    💓 心跳+查询 [时间] 查询目标: 服务提供端
echo    ✅ 通过心跳获取节点信息: 服务提供端 -^> ...
echo    🎯 步骤2: 尝试 P2P 打洞...
echo    
echo    成功结果1：
echo    ✅ P2P 直连成功！
echo    类型: ⚡ P2P 直连
echo    
echo    成功结果2：
echo    ✅ 服务器中转模式已启用！
echo    类型: 🔄 服务器中转
echo.
echo ========================================
echo 验证 P2P 改进效果：
echo ========================================
echo.
echo 改进前：
echo   - 服务器收不到 QUERY 请求 ❌
echo   - P2P 打洞从未触发 ❌
echo   - 100%% 服务器中转
echo.
echo 改进后（预期）：
echo   - 服务器收到心跳携带的查询 ✅
echo   - P2P 打洞正常触发 ✅
echo   - 30-40%% P2P 直连，60-70%% 中转
echo.
echo ========================================
echo 配置说明：
echo ========================================
echo.
echo 如需修改配置，编辑各目录下的配置文件：
echo   - Server\server_config.json
echo   - ServiceProvider\client_config.json
echo   - AccessClient\client_config.json
echo.
echo 重要参数：
echo   - GroupID: 必须相同才能互相通信
echo   - GroupKey: 密钥，必须匹配
echo   - Servers: 服务器地址（本地测试用 127.0.0.1）
echo   - TargetPeerID: 目标节点的 PeerID
echo.
echo ========================================
echo 日志位置：
echo ========================================
echo.
echo 各程序启动后会在 logs\ 目录生成日志文件
echo   - Server\logs\server_*.log
echo   - ServiceProvider\logs\service_provider_*.log
echo   - AccessClient\logs\access_client_*.log
echo.
echo ========================================
) > TestDeploy\测试指南.txt

echo ✅ 说明文档创建完成
echo.

:: 完成
echo ========================================
echo   ✅ 编译和配置完成！
echo ========================================
echo.
echo 目录结构：
echo   TestDeploy\
echo   ├── Server\              [服务器端]
echo   ├── ServiceProvider\     [服务提供端]
echo   └── AccessClient\        [访问客户端]
echo.
echo 下一步测试：
echo.
echo 1️⃣ 打开第一个命令行窗口
echo    cd TestDeploy\Server
echo    .\启动服务器.bat
echo.
echo 2️⃣ 打开第二个命令行窗口
echo    cd TestDeploy\ServiceProvider
echo    .\启动服务提供端.bat
echo.
echo 3️⃣ 打开第三个命令行窗口
echo    cd TestDeploy\AccessClient
echo    .\启动访问客户端.bat
echo.
echo 📖 详细说明请查看: TestDeploy\测试指南.txt
echo.
echo ========================================
pause
