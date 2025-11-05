# P2P 系统 - 编译和配置工具
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  P2P 系统 - 编译和配置工具" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 检查 .NET SDK
Write-Host "检查 .NET SDK..." -ForegroundColor Yellow
$dotnetVersion = dotnet --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 未检测到 .NET SDK" -ForegroundColor Red
    Write-Host "请先安装 .NET SDK: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    pause
    exit 1
}
Write-Host "✅ 检测到 .NET SDK 版本: $dotnetVersion" -ForegroundColor Green
Write-Host ""

# 清理旧文件
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "步骤1: 清理旧编译文件" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
if (Test-Path "TestDeploy") {
    Write-Host "删除旧的测试目录..." -ForegroundColor Yellow
    Remove-Item -Path "TestDeploy" -Recurse -Force
}
Write-Host "✅ 清理完成" -ForegroundColor Green
Write-Host ""

# 创建测试目录结构
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "步骤2: 创建测试目录" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
New-Item -ItemType Directory -Path "TestDeploy\Server" -Force | Out-Null
New-Item -ItemType Directory -Path "TestDeploy\ServiceProvider" -Force | Out-Null
New-Item -ItemType Directory -Path "TestDeploy\AccessClient" -Force | Out-Null
Write-Host "✅ 目录创建完成" -ForegroundColor Green
Write-Host ""

# 编译服务器
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "步骤3: 编译服务器端" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
dotnet build P2PServer.csproj -c Release -o TestDeploy\Server --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 服务器端编译失败" -ForegroundColor Red
    pause
    exit 1
}
Write-Host "✅ 服务器端编译成功" -ForegroundColor Green
Write-Host ""

# 编译客户端
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "步骤4: 编译客户端" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
dotnet build P2PClient.csproj -c Release -o TestDeploy\ServiceProvider --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 客户端编译失败" -ForegroundColor Red
    pause
    exit 1
}
Write-Host "✅ 客户端编译成功" -ForegroundColor Green
Write-Host ""

# 复制客户端到访问端
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "步骤5: 复制客户端到访问端" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Copy-Item -Path "TestDeploy\ServiceProvider\*" -Destination "TestDeploy\AccessClient\" -Recurse -Force
Write-Host "✅ 复制完成" -ForegroundColor Green
Write-Host ""

# 创建配置文件
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "步骤6: 创建配置文件" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 服务器配置
$serverConfig = @{
    ServerPort = 8000
    MaxClients = 1000
    Groups = @(
        @{
            GroupID = "测试组1"
            GroupKey = "test123"
            Description = "测试用组"
        }
    )
    Logging = @{
        Level = "INFO"
        LogToFile = $true
        LogFilePath = "logs/server_{date}.log"
    }
    Advanced = @{
        ClientTimeout = 30
        CleanupInterval = 10
        EnablePortForward = $true
    }
}
$serverConfig | ConvertTo-Json -Depth 10 | Out-File -FilePath "TestDeploy\Server\server_config.json" -Encoding UTF8

# 服务提供端配置
$serviceProviderConfig = @{
    PeerID = "服务提供端"
    GroupID = "测试组1"
    GroupKey = "test123"
    Servers = @("127.0.0.1")
    ServerPort = 8000
    PortForwards = @()
    Logging = @{
        Level = "INFO"
        LogToFile = $true
        LogFilePath = "logs/service_provider_{date}.log"
    }
    Advanced = @{
        HeartbeatInterval = 1000
        PunchRetryCount = 30
        EnableP2P = $true
        EnableRelay = $true
    }
}
$serviceProviderConfig | ConvertTo-Json -Depth 10 | Out-File -FilePath "TestDeploy\ServiceProvider\client_config.json" -Encoding UTF8

# 访问客户端配置
$accessClientConfig = @{
    PeerID = "访问客户端"
    GroupID = "测试组1"
    GroupKey = "test123"
    Servers = @("127.0.0.1")
    ServerPort = 8000
    PortForwards = @(
        @{
            Name = "测试连接"
            LocalPort = 9999
            TargetPeerID = "服务提供端"
            TargetPort = 9999
            Protocol = "TCP"
        }
    )
    Logging = @{
        Level = "INFO"
        LogToFile = $true
        LogFilePath = "logs/access_client_{date}.log"
    }
    Advanced = @{
        HeartbeatInterval = 1000
        PunchRetryCount = 30
        EnableP2P = $true
        EnableRelay = $true
    }
}
$accessClientConfig | ConvertTo-Json -Depth 10 | Out-File -FilePath "TestDeploy\AccessClient\client_config.json" -Encoding UTF8

Write-Host "✅ 配置文件创建完成" -ForegroundColor Green
Write-Host ""

# 创建启动脚本
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "步骤7: 创建启动脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 服务器启动脚本
@"
@echo off
chcp 65001 >nul
title P2P 服务器端
cls
echo ========================================
echo   P2P 服务器端 - 监听端口 8000
echo ========================================
echo.
P2PServer.exe
pause
"@ | Out-File -FilePath "TestDeploy\Server\启动服务器.bat" -Encoding UTF8

# 服务提供端启动脚本
@"
@echo off
chcp 65001 >nul
title 服务提供端
cls
echo ========================================
echo   服务提供端 - 提供服务
echo ========================================
echo.
P2PClient.exe
pause
"@ | Out-File -FilePath "TestDeploy\ServiceProvider\启动服务提供端.bat" -Encoding UTF8

# 访问客户端启动脚本
@"
@echo off
chcp 65001 >nul
title 访问客户端
cls
echo ========================================
echo   访问客户端 - 访问服务
echo ========================================
echo.
P2PClient.exe
pause
"@ | Out-File -FilePath "TestDeploy\AccessClient\启动访问客户端.bat" -Encoding UTF8

Write-Host "✅ 启动脚本创建完成" -ForegroundColor Green
Write-Host ""

# 创建说明文档
$testGuide = @"
========================================
  P2P 系统测试指南
========================================

目录结构：
  TestDeploy\
  ├── Server\              [服务器端]
  │   ├── P2PServer.exe
  │   ├── server_config.json
  │   └── 启动服务器.bat
  │
  ├── ServiceProvider\     [服务提供端]
  │   ├── P2PClient.exe
  │   ├── client_config.json
  │   └── 启动服务提供端.bat
  │
  └── AccessClient\        [访问客户端]
      ├── P2PClient.exe
      ├── client_config.json
      └── 启动访问客户端.bat

========================================
测试步骤（按顺序）：
========================================

1. 启动服务器端
   打开新的命令行窗口，执行：
   cd TestDeploy\Server
   .\启动服务器.bat
   
   预期日志：
   ✅ 服务器启动在端口 8000
   支持的组: 测试组1
   等待客户端连接...

2. 启动服务提供端
   打开新的命令行窗口，执行：
   cd TestDeploy\ServiceProvider
   .\启动服务提供端.bat
   
   预期日志：
   ✅ 注册成功！组: 测试组1, 公网地址: 127.0.0.1:xxxxx
   💓 心跳 [时间]

3. 启动访问客户端
   打开新的命令行窗口，执行：
   cd TestDeploy\AccessClient
   .\启动访问客户端.bat
   
   关键日志观察：
   📡 步骤1: 查询目标节点公网地址...
   🔍 查询节点信息: 服务提供端（通过心跳机制）
   💓 心跳+查询 [时间] 查询目标: 服务提供端
   ✅ 通过心跳获取节点信息: 服务提供端 -> 127.0.0.1:xxxxx
   🎯 步骤2: 尝试 P2P 打洞...
   
   成功结果1（P2P直连）：
   ✅ P2P 直连成功！
   类型: ⚡ P2P 直连
   
   成功结果2（服务器中转）：
   ✅ 服务器中转模式已启用！
   类型: 🔄 服务器中转

========================================
验证 P2P 改进效果：
========================================

改进前：
  - 服务器收不到 QUERY 请求 ❌
  - P2P 打洞从未触发 ❌
  - 100% 服务器中转

改进后（预期）：
  - 服务器收到心跳携带的查询 ✅
  - P2P 打洞正常触发 ✅
  - 本地测试：80-90% P2P 直连
  - 跨网络测试：30-40% P2P 直连，60-70% 中转

========================================
服务器端关键日志：
========================================

改进前：
  💓 心跳: 访问客户端
  （没有收到 QUERY 请求）

改进后：
  💓 心跳: 访问客户端
  🔍 心跳携带查询: 访问客户端 查询 服务提供端
  ✅ 返回节点信息: 服务提供端 → 访问客户端 [组测试组1]

========================================
配置说明：
========================================

如需修改配置，编辑各目录下的配置文件：
  - Server\server_config.json
  - ServiceProvider\client_config.json
  - AccessClient\client_config.json

重要参数：
  - GroupID: 必须相同才能互相通信
  - GroupKey: 密钥，必须匹配
  - Servers: 服务器地址（本地测试用 127.0.0.1）
  - TargetPeerID: 目标节点的 PeerID

========================================
日志位置：
========================================

各程序启动后会在 logs\ 目录生成日志文件：
  - Server\logs\server_*.log
  - ServiceProvider\logs\service_provider_*.log
  - AccessClient\logs\access_client_*.log

========================================
故障排查：
========================================

1. 如果客户端无法注册：
   - 检查服务器是否启动
   - 检查 GroupID 和 GroupKey 是否匹配

2. 如果查询超时：
   - 检查服务提供端是否已启动并注册成功
   - 查看服务器日志是否收到心跳携带的查询

3. 如果 P2P 打洞失败：
   - 这是正常的，系统会自动降级到服务器中转
   - 本地测试环境P2P成功率通常很高

========================================
"@
$testGuide | Out-File -FilePath "TestDeploy\测试指南.txt" -Encoding UTF8

Write-Host "✅ 说明文档创建完成" -ForegroundColor Green
Write-Host ""

# 完成
Write-Host "========================================" -ForegroundColor Green
Write-Host "  ✅ 编译和配置完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "目录结构：" -ForegroundColor Cyan
Write-Host "  TestDeploy\" -ForegroundColor White
Write-Host "  ├── Server\              [服务器端]" -ForegroundColor Yellow
Write-Host "  ├── ServiceProvider\     [服务提供端]" -ForegroundColor Yellow
Write-Host "  └── AccessClient\        [访问客户端]" -ForegroundColor Yellow
Write-Host ""
Write-Host "下一步测试：" -ForegroundColor Cyan
Write-Host ""
Write-Host "1️⃣  打开第一个命令行窗口" -ForegroundColor Green
Write-Host "   cd TestDeploy\Server" -ForegroundColor White
Write-Host "   .\启动服务器.bat" -ForegroundColor White
Write-Host ""
Write-Host "2️⃣  打开第二个命令行窗口" -ForegroundColor Green
Write-Host "   cd TestDeploy\ServiceProvider" -ForegroundColor White
Write-Host "   .\启动服务提供端.bat" -ForegroundColor White
Write-Host ""
Write-Host "3️⃣  打开第三个命令行窗口" -ForegroundColor Green
Write-Host "   cd TestDeploy\AccessClient" -ForegroundColor White
Write-Host "   .\启动访问客户端.bat" -ForegroundColor White
Write-Host ""
Write-Host "📖 详细说明请查看: TestDeploy\测试指南.txt" -ForegroundColor Cyan
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
pause
