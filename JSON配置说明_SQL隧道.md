# JSON配置说明 - 通过配置实现SQL隧道

## 🎯 配置已完成！

你的想法是对的！**不需要写死代码**，完全可以通过修改JSON配置来实现SQL隧道。

## 📝 配置文件详解

### AccessClient配置 (访问客户端)

```json
{
  "PeerID": "访问客户端",
  "GroupID": "测试组1",
  "GroupKey": "test123",
  "Servers": ["42.51.41.138"],
  "ServerPort": 8000,
  "PortForwards": [
    {
      "Name": "SQL隧道",          // 转发规则名称
      "LocalPort": 1430,          // ✅ 本地监听端口（你的机器）
      "TargetPeerID": "服务提供端", // 目标节点ID
      "TargetPort": 1433,          // ✅ 目标端口（远程SQL Server）
      "Protocol": "TCP"
    }
  ]
}
```

### ServiceProvider配置 (服务提供端)

```json
{
  "PeerID": "服务提供端",
  "GroupID": "测试组1",
  "GroupKey": "test123",
  "Servers": ["42.51.41.138"],
  "ServerPort": 8000,
  "PortForwards": []  // 服务端不需要配置转发规则
}
```

## 🔄 工作原理图解

```
[你的应用]
    ↓
    连接 localhost:1430
    ↓
[PortForwarder] 本地监听 1430 端口
    ↓
    发送 FORWARD:1433:data 消息
    ↓
[P2P UDP隧道] 直连或中转
    ↓
[服务提供端] 接收 FORWARD 消息
    ↓
    转发到本地 127.0.0.1:1433
    ↓
[SQL Server] 真实数据库
```

## 📊 配置参数说明

### PortForwards配置项

| 参数 | 说明 | 示例 |
|------|------|------|
| **Name** | 转发规则名称 | "SQL隧道" |
| **LocalPort** | 本地监听端口（你访问的端口） | 1430 |
| **TargetPeerID** | 目标节点ID | "服务提供端" |
| **TargetPort** | 目标节点的端口 | 1433 (SQL Server) |
| **Protocol** | 协议类型 | "TCP" |

### 关键点理解

#### LocalPort (本地端口)
- 这是**你的应用连接的端口**
- 在你的机器上监听
- 例如：`1430` 表示你用 `Server=localhost,1430` 连接

#### TargetPort (目标端口)
- 这是**远程服务器上的端口**
- 在服务提供端的机器上
- 例如：`1433` 表示远程的SQL Server端口

## ✅ 已修改的配置文件

1. ✅ `TestDeploy/AccessClient/client_config.json`
2. ✅ `client_config_访问端_DEBUG.json`

**修改内容**：
```json
"LocalPort": 1430,    // 改为1430（原来是9999）
"TargetPort": 1433,   // 改为1433（原来是9999）
"Name": "SQL隧道"      // 改为有意义的名称
```

## 🚀 使用方法

### 1. 启动服务端
```powershell
cd TestDeploy\ServiceProvider
.\P2PClient.exe
# 等待看到：✅ 已注册到服务器
```

### 2. 启动客户端
```powershell
cd TestDeploy\AccessClient  
.\P2PClient.exe
# 等待看到：
# [端口转发] SQL隧道 已启动，监听端口 1430
# ✅ 已连接到 服务提供端
```

### 3. 在应用中连接
```csharp
// 使用本地1430端口
string connectionString = "Server=localhost,1430;Database=MyDB;User Id=sa;Password=xxx;";
```

## 🔧 自定义配置示例

### 示例1：使用其他端口

```json
"PortForwards": [
  {
    "Name": "自定义SQL",
    "LocalPort": 1435,     // 改为1435
    "TargetPeerID": "服务提供端",
    "TargetPort": 1433,
    "Protocol": "TCP"
  }
]
```

**连接字符串**：`Server=localhost,1435;...`

### 示例2：多个转发规则

```json
"PortForwards": [
  {
    "Name": "SQL隧道",
    "LocalPort": 1430,
    "TargetPeerID": "服务提供端",
    "TargetPort": 1433,
    "Protocol": "TCP"
  },
  {
    "Name": "Web服务",
    "LocalPort": 8080,
    "TargetPeerID": "服务提供端",
    "TargetPort": 80,
    "Protocol": "TCP"
  },
  {
    "Name": "MySQL数据库",
    "LocalPort": 3307,
    "TargetPeerID": "数据库服务器",
    "TargetPort": 3306,
    "Protocol": "TCP"
  }
]
```

### 示例3：连接不同的服务器

```json
"PortForwards": [
  {
    "Name": "主数据库",
    "LocalPort": 1430,
    "TargetPeerID": "主数据库服务器",  // 不同的节点
    "TargetPort": 1433,
    "Protocol": "TCP"
  },
  {
    "Name": "备份数据库",
    "LocalPort": 1431,
    "TargetPeerID": "备份数据库服务器",  // 另一个节点
    "TargetPort": 1433,
    "Protocol": "TCP"
  }
]
```

## 📊 对比：写死代码 vs JSON配置

| 特性 | 写死代码 | JSON配置 ✅ |
|------|----------|------------|
| 修改端口 | 需要重新编译 | 直接改JSON |
| 多规则支持 | 需要改代码 | 添加JSON数组 |
| 维护成本 | 高 | 低 |
| 灵活性 | 低 | 高 |
| 错误风险 | 中 | 低 |

## 🎯 实际测试

### 编译和运行
```powershell
# 编译（如果需要）
cd d:\Ksa_p2p直联
dotnet build -c Release

# 复制配置文件到部署目录（已经在正确位置）
# TestDeploy/AccessClient/client_config.json （已修改）
# TestDeploy/ServiceProvider/client_config.json （已就绪）

# 运行测试
cd TestDeploy\AccessClient
.\P2PClient.exe
```

### 验证日志
**客户端应该看到**：
```
[端口转发] SQL隧道: 本地:1430 → 服务提供端:1433
[端口转发] SQL隧道 已启动，监听端口 1430
🔗 自动连接到目标节点: 服务提供端
✅ 已连接到 服务提供端
```

**服务端应该看到**：
```
📨 收到转发数据: 目标端口 1433, 数据长度 xxx 字节
✅ 数据已转发到本地端口 1433
```

## ⚠️ 注意事项

### 1. 端口冲突
如果本地1430端口被占用：
```json
"LocalPort": 1431,  // 改为其他端口
```

### 2. SQL Server配置
确保服务端SQL Server允许本地TCP/IP连接：
- 启用TCP/IP协议
- 监听127.0.0.1:1433
- 防火墙允许本地访问

### 3. GroupID和GroupKey必须匹配
客户端和服务端的GroupID、GroupKey必须完全相同！

### 4. PeerID必须唯一
- 访问客户端：`"PeerID": "访问客户端"`
- 服务提供端：`"PeerID": "服务提供端"`

## 🎉 优势总结

### ✅ 完全通过JSON配置
- 不需要写代码
- 不需要重新编译
- 修改后直接运行

### ✅ 灵活性高
- 可以随时改端口
- 可以添加多个转发规则
- 可以连接多个目标节点

### ✅ 维护简单
- 配置集中管理
- 一目了然
- 易于备份和版本控制

## 📚 相关文档

- **P2PClient.cs** - 主程序（已集成PortForwarder）
- **PortForwarder.cs** - 端口转发实现
- **P2PPuncher.cs** - P2P核心（已添加FORWARD消息处理）

---

**总结**：你的想法完全正确！通过JSON配置比写死代码优雅得多。现在：
- ✅ LocalPort = 1430 （你访问的端口）
- ✅ TargetPort = 1433 （SQL Server端口）
- ✅ 所有配置都在JSON中
- ✅ 随时可以修改，无需编译

**连接字符串**：`Server=localhost,1430;Database=YourDB;User Id=sa;Password=xxx;`
