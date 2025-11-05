/*
 * P2P 中心服务器 (C# 实现)
 * 辅助客户端进行 NAT 穿透
 * 用途：个人学习研究
 */

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using P2PConfig;

namespace P2PServer
{
    // ========== 客户端信息 ==========
    public class ClientInfo
    {
        public string PeerID { get; set; }
        public IPEndPoint PublicEndPoint { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public string RelayTargetID { get; set; } // 中转目标
        public string GroupID { get; set; } // 所属组ID（新增）
    }

    // ========== P2P 服务器 ==========
    public class P2PServer
    {
        private UdpClient server;
        private Dictionary<string, ClientInfo> clients;
        private ServerConfig config;  // 新增：配置
        private Logger logger;        // 新增：日志
        private const int PORT = 8000;

        public P2PServer(ServerConfig config, Logger logger)
        {
            clients = new Dictionary<string, ClientInfo>();
            this.config = config;
            this.logger = logger;
        }

        // ========== 启动服务器 ==========
        public void Start()
        {
            try
            {
                server = new UdpClient(config.ServerPort);
                logger.Info($"✅ 服务器启动在端口 {config.ServerPort}");
                logger.Info($"支持的组: {string.Join(", ", config.Groups.Select(g => g.GroupID))}");
                logger.Info("等待客户端连接...\n");

                Task.Run(() => ListenAsync());
                Task.Run(() => CleanupInactiveClients());
            }
            catch (Exception ex)
            {
                logger.Error($"❌ 启动失败: {ex.Message}");
            }
        }

        // ========== 监听客户端消息 ==========
        private async Task ListenAsync()
        {
            while (true)
            {
                try
                {
                    var result = await server.ReceiveAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);
                    
                    logger.Debug($"📨 收到 [{result.RemoteEndPoint}]: {message}");
                    
                    await HandleMessageAsync(message, result.RemoteEndPoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ 接收异常: {ex.Message}");
                }
            }
        }

        // ========== 处理客户端消息 ==========
        private async Task HandleMessageAsync(string message, IPEndPoint clientEndPoint)
        {
            var parts = message.Split(':');
            if (parts.Length < 2) return;

            string command = parts[0];
            string peerID = parts[1];

            switch (command)
            {
                case "REGISTER":
                    // 格式: REGISTER:PeerID:GroupID:GroupKey
                    string groupID = parts.Length >= 3 ? parts[2] : "default";
                    string groupKey = parts.Length >= 4 ? parts[3] : "";
                    await HandleRegisterAsync(peerID, groupID, groupKey, clientEndPoint);
                    break;

                case "HEARTBEAT":
                    // 格式: HEARTBEAT:PeerID 或 HEARTBEAT:PeerID:QUERY:TargetPeerID
                    string queryTarget = parts.Length >= 4 && parts[2] == "QUERY" ? parts[3] : null;
                    await HandleHeartbeatAsync(peerID, queryTarget, clientEndPoint);
                    break;

                case "QUERY":
                    // 查询其他节点信息（带组隔离）
                    if (parts.Length >= 3)
                    {
                        Console.WriteLine($"🔍 收到查询请求: {peerID} 查询 {parts[2]}");
                        await HandleQueryAsync(peerID, parts[2], clientEndPoint);
                    }
                    break;

                case "RELAY_START":
                    // 启用中转模式
                    logger.Info($"📨 收到中转请求: {peerID} → {(parts.Length >= 3 ? parts[2] : "?")} 来自 {clientEndPoint}");
                    if (parts.Length >= 3)
                    {
                        await HandleRelayStartAsync(peerID, parts[2], clientEndPoint);
                    }
                    break;

                case "RELAY_DATA":
                    // 中转数据
                    if (parts.Length >= 3)
                    {
                        await HandleRelayDataAsync(peerID, string.Join(":", parts.Skip(2)), clientEndPoint);
                    }
                    break;

                case "PORT_FORWARD":
                    // 内网穿透端口转发
                    if (parts.Length >= 4)
                    {
                        await HandlePortForwardAsync(peerID, parts[2], int.Parse(parts[3]), clientEndPoint);
                    }
                    break;

                case "LIST_GROUP":
                    // 列出同组成员
                    await HandleListGroupAsync(peerID, clientEndPoint);
                    break;
            }
        }

        // ========== 处理注册（支持分组和密钥验证）==========
        private async Task HandleRegisterAsync(string peerID, string groupID, string groupKey, IPEndPoint clientEndPoint)
        {
            // 验证组密钥
            var groupConfig = config.Groups.FirstOrDefault(g => g.GroupID == groupID);
            
            if (groupConfig == null)
            {
                logger.Warn($"⚠️ 未知组ID: {groupID} 来自 {peerID}");
                string errorMsg = "ERROR:UNKNOWN_GROUP";
                byte[] errorData = Encoding.UTF8.GetBytes(errorMsg);
                await server.SendAsync(errorData, errorData.Length, clientEndPoint);
                return;
            }

            if (groupConfig.GroupKey != groupKey)
            {
                logger.Error($"❌ 密钥错误: {peerID} 尝试加入组 {groupID}");
                string errorMsg = "ERROR:INVALID_KEY";
                byte[] errorData = Encoding.UTF8.GetBytes(errorMsg);
                await server.SendAsync(errorData, errorData.Length, clientEndPoint);
                return;
            }

            // 记录客户端信息
            if (!clients.ContainsKey(peerID))
            {
                clients[peerID] = new ClientInfo
                {
                    PeerID = peerID,
                    GroupID = groupID,
                    PublicEndPoint = clientEndPoint,
                    LastHeartbeat = DateTime.Now
                };
                
                logger.LogConnection(peerID, ConnectionType.SERVER_RELAY, $"组:{groupID} @ {clientEndPoint}");
            }
            else
            {
                clients[peerID].PublicEndPoint = clientEndPoint;
                clients[peerID].LastHeartbeat = DateTime.Now;
                clients[peerID].GroupID = groupID;
            }

            // 返回客户端的公网地址
            string response = $"OK:{clientEndPoint.Address}:{clientEndPoint.Port}";
            byte[] data = Encoding.UTF8.GetBytes(response);
            await server.SendAsync(data, data.Length, clientEndPoint);

            logger.Debug($"📤 发送公网信息给 {peerID}: {clientEndPoint}");
        }

        // ========== 处理心跳（改进版：支持响应和查询）==========
        private async Task HandleHeartbeatAsync(string peerID, string queryTarget, IPEndPoint clientEndPoint)
        {
            if (clients.ContainsKey(peerID))
            {
                clients[peerID].LastHeartbeat = DateTime.Now;
                clients[peerID].PublicEndPoint = clientEndPoint;
                logger.Debug($"💓 心跳: {peerID}");
                
                // 构建心跳响应
                string response;
                
                // 如果心跳中携带查询请求
                if (!string.IsNullOrEmpty(queryTarget))
                {
                    logger.Info($"🔍 心跳携带查询: {peerID} 查询 {queryTarget}");
                    
                    // 检查请求者和目标是否同组
                    if (clients.ContainsKey(queryTarget))
                    {
                        var target = clients[queryTarget];
                        string fromGroupID = clients[peerID].GroupID;
                        
                        if (target.GroupID == fromGroupID)
                        {
                            // 同组，返回节点信息
                            response = $"HEARTBEAT_OK:PEER_INFO:{target.PublicEndPoint.Address}:{target.PublicEndPoint.Port}";
                            logger.Info($"✅ 返回节点信息: {queryTarget} → {peerID} [组{fromGroupID}]");
                            
                            // 🆕 双向打洞：通知目标节点准备接收打洞包
                            string notifyMsg = $"PUNCH_START:{peerID}:{clientEndPoint.Address}:{clientEndPoint.Port}";
                            byte[] notifyData = Encoding.UTF8.GetBytes(notifyMsg);
                            await server.SendAsync(notifyData, notifyData.Length, target.PublicEndPoint);
                            logger.Info($"📤 通知 {queryTarget} 准备接收来自 {peerID} 的打洞包");
                        }
                        else
                        {
                            // 不同组，拒绝访问
                            response = $"HEARTBEAT_OK:ERROR:ACCESS_DENIED";
                            logger.Warn($"⛔ 组隔离: {peerID}[组{fromGroupID}] 尝试访问 {queryTarget}[组{target.GroupID}]");
                        }
                    }
                    else
                    {
                        // 目标不存在
                        response = $"HEARTBEAT_OK:ERROR:PEER_NOT_FOUND";
                        logger.Warn($"⚠️ 目标节点 {queryTarget} 未在线");
                    }
                }
                else
                {
                    // 普通心跳，简单响应
                    response = "HEARTBEAT_OK";
                }
                
                // 发送响应
                byte[] data = Encoding.UTF8.GetBytes(response);
                int bytesSent = await server.SendAsync(data, data.Length, clientEndPoint);
                logger.Debug($"📤 心跳响应已发送到 {clientEndPoint}: {response} ({bytesSent} 字节)");
            }
        }

        // ========== 处理查询其他节点（带组隔离）==========
        private async Task HandleQueryAsync(string fromPeerID, string targetPeerID, IPEndPoint clientEndPoint)
        {
            // 检查请求者是否存在
            if (!clients.ContainsKey(fromPeerID))
            {
                string errorMsg = "ERROR:NOT_REGISTERED";
                byte[] errorData = Encoding.UTF8.GetBytes(errorMsg);
                await server.SendAsync(errorData, errorData.Length, clientEndPoint);
                return;
            }

            string fromGroupID = clients[fromPeerID].GroupID;

            // 检查目标节点是否存在
            if (clients.ContainsKey(targetPeerID))
            {
                var target = clients[targetPeerID];
                
                // ⭐ 关键：检查是否同组
                if (target.GroupID != fromGroupID)
                {
                    Console.WriteLine($"⛔ 组隔离: {fromPeerID}[组{fromGroupID}] 尝试访问 {targetPeerID}[组{target.GroupID}]");
                    string denyMsg = "ERROR:ACCESS_DENIED";
                    byte[] denyData = Encoding.UTF8.GetBytes(denyMsg);
                    await server.SendAsync(denyData, denyData.Length, clientEndPoint);
                    return;
                }

                // 同组，允许访问
                string response = $"PEER:{target.PublicEndPoint.Address}:{target.PublicEndPoint.Port}";
                byte[] data = Encoding.UTF8.GetBytes(response);
                await server.SendAsync(data, data.Length, clientEndPoint);

                Console.WriteLine($"✅ 返回节点信息: {targetPeerID} → {fromPeerID} [组{fromGroupID}]");
            }
            else
            {
                string response = "ERROR:PEER_NOT_FOUND";
                byte[] data = Encoding.UTF8.GetBytes(response);
                await server.SendAsync(data, data.Length, clientEndPoint);
            }
        }

        // ========== 处理中转启动（带组验证）==========
        private async Task HandleRelayStartAsync(string fromPeerID, string targetPeerID, IPEndPoint clientEndPoint)
        {
            if (!clients.ContainsKey(fromPeerID))
            {
                clients[fromPeerID] = new ClientInfo
                {
                    PeerID = fromPeerID,
                    PublicEndPoint = clientEndPoint,
                    LastHeartbeat = DateTime.Now,
                    GroupID = "default"
                };
            }

            // 检查是否同组
            if (clients.ContainsKey(targetPeerID))
            {
                string fromGroup = clients[fromPeerID].GroupID;
                string targetGroup = clients[targetPeerID].GroupID;

                if (fromGroup != targetGroup)
                {
                    Console.WriteLine($"⛔ 中转被拒: {fromPeerID}[组{fromGroup}] → {targetPeerID}[组{targetGroup}]");
                    string denyMsg = "RELAY_DENIED";
                    byte[] denyData = Encoding.UTF8.GetBytes(denyMsg);
                    await server.SendAsync(denyData, denyData.Length, clientEndPoint);
                    return;
                }
            }

            // 设置中转目标
            clients[fromPeerID].RelayTargetID = targetPeerID;
            
            string response = "RELAY_OK";
            byte[] data = Encoding.UTF8.GetBytes(response);
            
            // 多次发送以确保可靠性（UDP不保证送达）
            for (int i = 0; i < 3; i++)
            {
                await server.SendAsync(data, data.Length, clientEndPoint);
                logger.Info($"📤 已发送 RELAY_OK 到 {clientEndPoint} (第{i+1}次)");
                await Task.Delay(10); // 间隔10ms
            }
            
            Console.WriteLine($"🔄 启用中转: {fromPeerID} ↔️ {targetPeerID} [组{clients[fromPeerID].GroupID}]");
        }

        // ========== 列出同组成员 ==========
        private async Task HandleListGroupAsync(string peerID, IPEndPoint clientEndPoint)
        {
            if (!clients.ContainsKey(peerID))
            {
                string errorMsg = "ERROR:NOT_REGISTERED";
                byte[] errorData = Encoding.UTF8.GetBytes(errorMsg);
                await server.SendAsync(errorData, errorData.Length, clientEndPoint);
                return;
            }

            string myGroupID = clients[peerID].GroupID;
            
            // 找出同组成员
            var groupMembers = clients.Values
                .Where(c => c.GroupID == myGroupID && c.PeerID != peerID)
                .Select(c => c.PeerID)
                .ToList();

            string response = $"GROUP_MEMBERS:{string.Join(",", groupMembers)}";
            byte[] data = Encoding.UTF8.GetBytes(response);
            await server.SendAsync(data, data.Length, clientEndPoint);

            Console.WriteLine($"📄 {peerID} 查询同组成员: {string.Join(", ", groupMembers)}");
        }

        // ========== 处理中转数据 ==========
        private async Task HandleRelayDataAsync(string fromPeerID, string message, IPEndPoint clientEndPoint)
        {
            if (clients.ContainsKey(fromPeerID) && !string.IsNullOrEmpty(clients[fromPeerID].RelayTargetID))
            {
                string targetPeerID = clients[fromPeerID].RelayTargetID;
                
                if (clients.ContainsKey(targetPeerID))
                {
                    // 转发给目标节点
                    var targetEndPoint = clients[targetPeerID].PublicEndPoint;
                    string relayMsg = $"RELAYED:{fromPeerID}:{message}";
                    byte[] data = Encoding.UTF8.GetBytes(relayMsg);
                    await server.SendAsync(data, data.Length, targetEndPoint);

                    Console.WriteLine($"🔄 中转数据: {fromPeerID} → {targetPeerID} | {message.Substring(0, Math.Min(20, message.Length))}...");
                }
                else
                {
                    Console.WriteLine($"⚠️ 目标节点 {targetPeerID} 不在线");
                }
            }
        }

        // ========== 处理端口转发（内网穿透）==========
        private Dictionary<int, string> portMappings = new Dictionary<int, string>();

        private async Task HandlePortForwardAsync(string peerID, string protocol, int remotePort, IPEndPoint clientEndPoint)
        {
            try
            {
                // 记录端口映射
                portMappings[remotePort] = peerID;
                
                string response = $"PORT_OK:{remotePort}";
                byte[] data = Encoding.UTF8.GetBytes(response);
                await server.SendAsync(data, data.Length, clientEndPoint);

                Console.WriteLine($"🌐 内网穿透: {peerID} 端口 {remotePort} -> {clientEndPoint}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 端口转发失败: {ex.Message}");
            }
        }

        // ========== 清理不活跃客户端 ==========
        private async Task CleanupInactiveClients()
        {
            while (true)
            {
                await Task.Delay(10000); // 每10秒检查一次

                var toRemove = new List<string>();
                foreach (var kvp in clients)
                {
                    if ((DateTime.Now - kvp.Value.LastHeartbeat).TotalSeconds > 30)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }

                foreach (var peerID in toRemove)
                {
                    clients.Remove(peerID);
                    Console.WriteLine($"🗑️ 移除不活跃客户端: {peerID}");
                }
            }
        }

        // ========== 显示在线客户端（按组显示）==========
        public void ShowClients()
        {
            Console.WriteLine("\n========== 在线客户端（按组）==========");
            
            var groups = clients.Values.GroupBy(c => c.GroupID);
            
            foreach (var group in groups)
            {
                Console.WriteLine($"\n[组: {group.Key}]");
                foreach (var client in group)
                {
                    Console.WriteLine($"  • {client.PeerID} - {client.PublicEndPoint}");
                }
            }
            
            Console.WriteLine("\n================================\n");
        }
    }
}
