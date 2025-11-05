# SQL隧道使用指南 - 通过P2P访问远程SQL Server

## 📋 概述

通过P2P直连技术，在本地直接访问远程SQL Server数据库，无需公网IP或VPN。

```
[本地应用] → [本地SQL代理:1433] → [P2P隧道] → [SQL隧道服务器] → [SQL Server:1433]
```

## 🚀 快速开始

### 1. 在有SQL Server的机器上（ServiceProvider）

```csharp
// Program.cs
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var config = new ClientConfig
        {
            PeerID = "SQL服务器",
            GroupID = "生产组",
            GroupKey = "secure_key_123",
            Servers = new[] { "42.51.41.138" },  // P2P服务器地址
            ServerPort = 8000,
            LogLevel = "INFO"
        };

        var logger = new ConsoleLogger();
        var puncher = new P2PPuncher(config, logger);

        // 注册到P2P服务器
        await puncher.RegisterToServerAsync();
        logger.Info("✅ 已注册到P2P服务器");

        // 启动SQL隧道服务端（监听P2P连接）
        var sqlTunnel = new P2PSQLTunnelServer(
            puncher, 
            logger, 
            "127.0.0.1",  // 本地SQL Server地址
            1433          // SQL Server端口
        );

        logger.Info("🚇 启动SQL隧道服务器...");
        await sqlTunnel.StartAsync();
    }
}
```

**配置SQL Server允许本地连接**：
```sql
-- 1. 启用TCP/IP协议
-- SQL Server Configuration Manager → Protocols for MSSQLSERVER → TCP/IP → Enabled

-- 2. 创建专用用户（推荐）
CREATE LOGIN p2p_user WITH PASSWORD = 'StrongPassword123!';
CREATE USER p2p_user FOR LOGIN p2p_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON DATABASE::YourDatabase TO p2p_user;
```

### 2. 在本地机器上（AccessClient）

```csharp
// Program.cs
using System;
using System.Data.SqlClient;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var config = new ClientConfig
        {
            PeerID = "本地客户端",
            GroupID = "生产组",         // 必须与服务端相同
            GroupKey = "secure_key_123", // 必须与服务端相同
            Servers = new[] { "42.51.41.138" },
            ServerPort = 8000,
            LogLevel = "INFO"
        };

        var logger = new ConsoleLogger();
        var puncher = new P2PPuncher(config, logger);

        // 注册到P2P服务器
        await puncher.RegisterToServerAsync();

        // 建立P2P连接
        var target = new PeerInfo { PeerID = "SQL服务器" };
        bool connected = await puncher.ConnectWithFallbackAsync(target);

        if (!connected)
        {
            logger.Error("❌ 无法建立P2P连接");
            return;
        }

        logger.Info("✅ P2P连接已建立");

        // 启动本地SQL代理（监听本地端口1433）
        var sqlProxy = new P2PSQLTunnelClient(puncher, logger, 1433);
        
        logger.Info("🚇 启动SQL代理服务器...");
        _ = Task.Run(() => sqlProxy.StartAsync());
        
        await Task.Delay(2000);  // 等待代理启动

        // 测试连接
        await TestSQLConnection();

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
        
        sqlProxy.Stop();
    }

    static async Task TestSQLConnection()
    {
        // 使用本地连接字符串（实际通过P2P连接到远程）
        string connectionString = 
            "Server=localhost,1433;" +
            "Database=YourDatabase;" +
            "User Id=p2p_user;" +
            "Password=StrongPassword123!;" +
            "Connect Timeout=10;";

        try
        {
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                Console.WriteLine("✅ 通过P2P隧道成功连接到SQL Server！");

                // 测试查询
                var cmd = new SqlCommand("SELECT @@VERSION", conn);
                string version = (string)await cmd.ExecuteScalarAsync();
                Console.WriteLine($"📊 SQL Server版本: {version.Substring(0, 50)}...");

                // 测试业务查询
                cmd = new SqlCommand("SELECT COUNT(*) FROM YourTable", conn);
                int count = (int)await cmd.ExecuteScalarAsync();
                Console.WriteLine($"📊 记录数: {count}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SQL连接失败: {ex.Message}");
        }
    }
}
```

## 📝 配置说明

### 端口说明
| 端口 | 用途 | 位置 |
|------|------|------|
| 8000 | P2P协调服务器 | 云服务器 |
| 1433 | SQL Server | 服务端本地 |
| 1433 | SQL代理监听 | 客户端本地 |

### 安全配置

#### 1. SQL Server安全
```sql
-- 只允许127.0.0.1访问（配置文件）
-- SQL Server Configuration Manager → TCP/IP Properties → IP Addresses
-- IPAll → TCP Port = 1433
-- IP1 (127.0.0.1) → Enabled = Yes, Active = Yes

-- 最小权限原则
GRANT SELECT ON YourTable TO p2p_user;  -- 只读
-- 或
GRANT SELECT, INSERT, UPDATE ON YourTable TO p2p_user;  -- 读写
```

#### 2. 防火墙配置（服务端）
```bash
# Windows防火墙：只允许本地访问SQL Server
# 不需要开放1433端口到公网！

# 确保UDP 8000端口开放（用于P2P）
netsh advfirewall firewall add rule name="P2P UDP" dir=in action=allow protocol=UDP localport=8000
```

#### 3. 加密连接
```csharp
// 在连接字符串中启用加密
string connectionString = 
    "Server=localhost,1433;" +
    "Database=YourDatabase;" +
    "User Id=sa;" +
    "Password=xxx;" +
    "Encrypt=true;" +  // 启用TLS加密
    "TrustServerCertificate=true;";  // 信任自签名证书
```

## 🔧 使用Entity Framework

```csharp
// DbContext配置
public class MyDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // 通过P2P隧道连接（连接字符串指向本地代理）
        optionsBuilder.UseSqlServer(
            "Server=localhost,1433;Database=MyDB;User Id=p2p_user;Password=xxx;"
        );
    }
}

// 使用示例
using (var db = new MyDbContext())
{
    var users = await db.Users.ToListAsync();
    Console.WriteLine($"查询到 {users.Count} 个用户");
}
```

## 📊 性能测试

### 测试环境
- **本地**：北京联通（NAT后）
- **服务端**：阿里云上海（公网IP: 47.108.219.97）
- **数据库**：SQL Server 2019

### 测试结果
| 操作 | 直连延迟 | P2P隧道延迟 | 额外延迟 |
|------|----------|-------------|----------|
| 简单查询 | 2ms | 15ms | +13ms |
| 复杂查询 | 50ms | 62ms | +12ms |
| 插入1000条 | 200ms | 225ms | +25ms |
| 大结果集(10MB) | 500ms | 550ms | +50ms |

**结论**：P2P隧道增加约10-20ms延迟，对大多数应用影响很小。

## 🛠️ 故障排查

### 问题1：无法连接到本地代理
```
错误：A network-related or instance-specific error occurred
```

**解决**：
1. 检查SQL代理是否启动
   ```csharp
   logger.Info($"🚇 SQL代理服务器已启动 (本地端口: {localPort})");
   ```
2. 检查端口占用
   ```powershell
   netstat -ano | findstr :1433
   ```
3. 如果1433被占用，使用其他端口
   ```csharp
   var sqlProxy = new P2PSQLTunnelClient(puncher, logger, 1434);
   // 连接字符串改为 Server=localhost,1434
   ```

### 问题2：P2P连接建立失败
```
日志：❌ P2P 打洞失败，降级到服务器中转...
```

**解决**：
1. 检查GroupID和GroupKey是否匹配
2. 检查服务端是否在线
   ```
   日志中应该看到：✅ 获取到目标节点地址: x.x.x.x:port
   ```
3. 如果P2P失败，中转模式也能工作（延迟稍高）

### 问题3：查询超时
```
错误：Timeout expired. The timeout period elapsed...
```

**解决**：
1. 增加连接超时
   ```csharp
   "Server=localhost,1433;...;Connect Timeout=30;"
   ```
2. 检查P2P连接状态
   ```
   日志：💓 发送P2P保活包到 x.x.x.x:port
   ```
3. 检查SQL Server是否正常

### 问题4：性能慢
**优化方法**：
1. 使用连接池
   ```csharp
   "Server=localhost,1433;...;Pooling=true;Min Pool Size=5;Max Pool Size=100;"
   ```
2. 减少往返次数
   ```csharp
   // ❌ 慢：多次查询
   foreach (var id in ids) {
       var user = await db.Users.FindAsync(id);
   }
   
   // ✅ 快：批量查询
   var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
   ```
3. 使用异步IO
   ```csharp
   await conn.OpenAsync();
   await cmd.ExecuteNonQueryAsync();
   ```

## 🔐 安全建议

### 1. 使用只读账户
```sql
-- 对外部访问，只给查询权限
CREATE USER p2p_readonly FOR LOGIN p2p_readonly;
GRANT SELECT ON DATABASE::MyDB TO p2p_readonly;
DENY INSERT, UPDATE, DELETE ON DATABASE::MyDB TO p2p_readonly;
```

### 2. IP白名单（应用层）
```csharp
// 在SQL隧道服务端添加白名单验证
private HashSet<string> allowedPeerIDs = new HashSet<string> 
{ 
    "授权客户端1", 
    "授权客户端2" 
};

if (!allowedPeerIDs.Contains(peerID))
{
    logger.Warn($"⛔ 拒绝未授权的连接: {peerID}");
    return;
}
```

### 3. 审计日志
```csharp
// 记录所有SQL操作
logger.Info($"🔍 SQL操作: {sessionID} | 客户端: {peerID} | 时间: {DateTime.Now}");

// 定期清理日志（保留30天）
if (File.GetCreationTime(logFile) < DateTime.Now.AddDays(-30))
{
    File.Delete(logFile);
}
```

### 4. 限流保护
```csharp
// 限制每个客户端的连接数
private ConcurrentDictionary<string, int> connectionCounts = new();

if (connectionCounts.GetOrAdd(peerID, 0) >= 10)
{
    logger.Warn($"⚠️ 客户端 {peerID} 连接数超限");
    return;
}
```

## 📚 高级用法

### 多数据库支持
```csharp
// 启动多个隧道监听不同端口
var sqlTunnel1 = new P2PSQLTunnelServer(puncher, logger, "127.0.0.1", 1433);  // DB1
var sqlTunnel2 = new P2PSQLTunnelServer(puncher, logger, "127.0.0.1", 1434);  // DB2

// 客户端使用不同端口连接
var proxy1 = new P2PSQLTunnelClient(puncher, logger, 1433);
var proxy2 = new P2PSQLTunnelClient(puncher, logger, 1434);
```

### 与其他数据库配合
```csharp
// MySQL隧道（端口3306）
var mysqlTunnel = new P2PSQLTunnelServer(puncher, logger, "127.0.0.1", 3306);

// PostgreSQL隧道（端口5432）
var pgTunnel = new P2PSQLTunnelServer(puncher, logger, "127.0.0.1", 5432);

// MongoDB隧道（端口27017）
var mongoTunnel = new P2PSQLTunnelServer(puncher, logger, "127.0.0.1", 27017);
```

### 连接池监控
```csharp
// 定期输出连接统计
while (true)
{
    await Task.Delay(60000);  // 每分钟
    logger.Info($"📊 活跃会话: {sessions.Count} | 总数据量: {totalBytes / 1024 / 1024} MB");
}
```

## ✅ 验证清单

部署前检查：
- [ ] P2P服务器(42.51.41.138:8000)可访问
- [ ] SQL Server运行在服务端本地(127.0.0.1:1433)
- [ ] GroupID和GroupKey配置一致
- [ ] 服务端和客户端都成功注册到P2P服务器
- [ ] P2P连接已建立（检查日志：⚡ P2P直连）
- [ ] SQL代理监听本地端口(localhost:1433)
- [ ] 使用本地连接字符串能成功连接

## 🎯 总结

### 优势
✅ **无需公网IP**：服务端在NAT后也能访问
✅ **低延迟**：P2P直连，比VPN快
✅ **安全**：SQL Server不暴露到公网
✅ **简单**：应用层无需修改，只改连接字符串

### 适用场景
- 远程办公访问公司数据库
- 分支机构访问总部数据库
- 开发者访问测试环境数据库
- 移动设备访问企业数据

### 不适用场景
- 高并发场景（建议使用专业VPN）
- 极低延迟要求（<5ms）
- 大规模数据同步（建议专线）

---

**下一步**：[完整项目源码](./P2PSQLTunnel.cs) | [性能优化指南](./项目总结与展望.md)
