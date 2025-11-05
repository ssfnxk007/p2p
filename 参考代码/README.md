# 参考代码说明

## 📁 文件夹内容

这个文件夹包含了**早期方案**的代码，作为技术参考保留。

## 🗂️ 文件清单

### 早期方案：独立SQL隧道程序（已弃用）

| 文件 | 说明 | 状态 |
|------|------|------|
| `P2PSQLAccessClient.cs` | 独立的访问客户端程序 | ❌ 已弃用 |
| `P2PSQLServiceProvider.cs` | 独立的服务提供端程序 | ❌ 已弃用 |
| `P2PSQLTunnel.cs` | SQL隧道核心实现类 | ❌ 已弃用 |
| `SQL隧道部署说明.md` | 早期部署文档 | ❌ 已过时 |

## ✅ 当前使用的方案

**通过JSON配置实现，无需独立程序**：

### 文件位置
```
TestDeploy/
├── AccessClient/
│   ├── P2PClient.exe          # ✅ 统一客户端
│   └── client_config.json     # ✅ JSON配置
└── ServiceProvider/
    ├── P2PClient.exe          # ✅ 统一客户端
    └── client_config.json     # ✅ JSON配置
```

### 配置方式
```json
// TestDeploy/AccessClient/client_config.json
"PortForwards": [
  {
    "Name": "SQL隧道",
    "LocalPort": 1430,
    "TargetPeerID": "服务提供端",
    "TargetPort": 1433,
    "Protocol": "TCP"
  }
]
```

## 🔄 方案对比

| 特性 | 早期方案（参考代码） | 当前方案（JSON配置） |
|------|-------------------|-------------------|
| 程序数量 | 2个独立程序 | 1个统一程序 |
| 配置方式 | 写死代码 | JSON配置 |
| 修改配置 | 需要重新编译 | ✅ 直接改JSON |
| 灵活性 | 低 | ✅ 高 |
| 维护成本 | 高 | ✅ 低 |
| 推荐使用 | ❌ 否 | ✅ 是 |

## 📚 为什么保留这些代码？

1. **技术参考**：展示了SQL隧道的底层实现原理
2. **学习价值**：理解TCP over UDP的转发机制
3. **历史记录**：记录方案演进过程
4. **代码示例**：可以参考其中的实现细节

## 🎯 如何使用当前方案

### 1. 使用现成的部署文件
```powershell
# 访问客户端
cd TestDeploy\AccessClient
.\启动访问客户端.bat

# 服务提供端
cd TestDeploy\ServiceProvider
.\启动服务提供端.bat
```

### 2. 修改配置（如需要）
编辑 `TestDeploy/AccessClient/client_config.json`：
```json
"LocalPort": 1430,  // 改为你想要的端口
"TargetPort": 1433  // SQL Server端口
```

### 3. 连接数据库
```csharp
string connectionString = "Server=localhost,1430;Database=MyDB;...";
```

## 📖 相关文档

- **当前部署说明**：`../TestDeploy/部署说明.md`
- **JSON配置说明**：`../JSON配置说明_SQL隧道.md`
- **项目总结**：`../项目总结与展望.md`
- **端口配置**：`../端口配置说明.md`

---

**总结**：
- ✅ 现在使用统一的 `P2PClient.exe` + JSON配置
- ✅ 更灵活、更易维护
- ✅ 这个文件夹的代码仅作参考

**不要使用这个文件夹的代码进行部署！**

请使用 `TestDeploy/` 文件夹中的部署文件。
