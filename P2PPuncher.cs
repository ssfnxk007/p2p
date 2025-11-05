/*
 * 高成功率 P2P UDP 打洞工具 (C# 实现)
 * 参考 KSA 的核心技术
 * 用途：个人学习研究
 */

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using P2PConfig;

namespace P2PPuncher
{
    // ========== 节点信息 ==========
    public class PeerInfo
    {
        public string PublicIP { get; set; }
        public int PublicPort { get; set; }
        public string PeerID { get; set; }
    }

    // ========== 核心 P2P 打洞类 ==========
    public class UdpPuncher
    {
        private UdpClient udpClient;
        private string myPeerID;
        private string myGroupID;
        private string myGroupKey;  // 新增：分组密钥
        private PeerInfo myPublicInfo;
        private bool isRunning;
        private Logger logger;  // 新增：日志系统
        
        // 配置参数（参考 KSA 的最佳实践）
        private string[] SERVER_IPS;
        private int SERVER_PORT;
        private int HEARTBEAT_INTERVAL;
        private int PUNCH_RETRY;
        
        // 改进：查询队列（通过心跳机制查询）
        private string currentQueryTarget = null;  // 当前查询目标
        private TaskCompletionSource<PeerInfo> currentQueryTask = null;  // 当前查询任务
        private readonly object queryLock = new object();  // 查询锁
        
        // 注册响应等待
        private TaskCompletionSource<string> registerResponseTask = null;
        private readonly object registerLock = new object();
        
        // 中转响应等待
        private TaskCompletionSource<string> relayResponseTask = null;
        private readonly object relayLock = new object();
        
        // P2P 打洞状态管理
        private TaskCompletionSource<bool> punchResultTask = null;
        private readonly object punchLock = new object();
        private PeerInfo currentPunchTarget = null;
        private IPEndPoint currentTarget = null;  // 当前通信目标（P2P或服务器）
        
        // P2P 连接保活
        private CancellationTokenSource keepAliveCts = null;
        private Task keepAliveTask = null;
        
        // 端口转发映射（ConnectionID -> TCP连接）
        private Dictionary<string, TcpClient> forwardConnections = new Dictionary<string, TcpClient>();
        private Dictionary<string, Task> forwardReadTasks = new Dictionary<string, Task>();
        private Dictionary<string, IPEndPoint> forwardRemoteEPs = new Dictionary<string, IPEndPoint>();
        private object forwardLock = new object();
        
        // 端口转发响应事件
        public event Action<string> OnForwardResponse;
        
        public UdpPuncher(string peerID, string groupID, string groupKey, string[] serverIPs, int serverPort, Logger logger)
        {
            myPeerID = peerID;
            myGroupID = groupID;
            myGroupKey = groupKey;
            myPublicInfo = new PeerInfo { PeerID = peerID };
            this.logger = logger;
            
            // 配置参数（优化后的参数，提高成功率）
            SERVER_IPS = serverIPs;
            SERVER_PORT = serverPort;
            HEARTBEAT_INTERVAL = 1000;      // 1秒心跳保持NAT映射
            PUNCH_RETRY = 30;                // 增加到30次打洞尝试
        }

        // ========== 初始化 ==========
        public bool Initialize()
        {
            try
            {
                udpClient = new UdpClient(0); // 随机端口
                int localPort = ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
                Console.WriteLine($"✅ 本地端口: {localPort}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 初始化失败: {ex.Message}");
                return false;
            }
        }

        // ========== 关键技术1: 注册到服务器获取公网信息（改进版：避免接收冲突）==========
        public async Task<bool> RegisterToServerAsync()
        {
            // 尝试多个服务器（高可用）
            foreach (var serverIP in SERVER_IPS)
            {
                try
                {
                    var serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), SERVER_PORT);
                    
                    // 创建注册响应等待任务
                    lock (registerLock)
                    {
                        registerResponseTask = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    
                    // 发送注册请求（带组ID和密钥）
                    string registerMsg = $"REGISTER:{myPeerID}:{myGroupID}:{myGroupKey}";
                    byte[] data = Encoding.UTF8.GetBytes(registerMsg);
                    await udpClient.SendAsync(data, data.Length, serverEndPoint);
                    
                    logger.Info($"📡 正在注册到服务器 {serverIP}...");
                    
                    // 等待接收循环处理响应（超时5秒）
                    var timeoutTask = Task.Delay(5000);
                    var completedTask = await Task.WhenAny(registerResponseTask.Task, timeoutTask).ConfigureAwait(false);
                    
                    if (completedTask == registerResponseTask.Task)
                    {
                        string response = await registerResponseTask.Task.ConfigureAwait(false);
                        
                        // 解析: "OK:公网IP:公网端口"
                        if (ParseServerResponse(response))
                        {
                            logger.Info($"✅ 注册成功！服务器: {serverIP}, 组: {myGroupID}, 公网地址: {myPublicInfo.PublicIP}:{myPublicInfo.PublicPort}");
                            lock (registerLock)
                            {
                                registerResponseTask = null;
                            }
                            return true;
                        }
                        else if (response.Contains("INVALID_KEY"))
                        {
                            logger.Error($"❌ 组密钥错误！请检查配置文件");
                            lock (registerLock)
                            {
                                registerResponseTask = null;
                            }
                            return false;
                        }
                    }
                    
                    logger.Warn($"⚠️ 服务器 {serverIP} 无响应，尝试下一个...");
                    lock (registerLock)
                    {
                        registerResponseTask = null;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"⚠️ 连接服务器 {serverIP} 失败: {ex.Message}");
                }
            }
            
            logger.Error("❌ 所有服务器都无法连接");
            return false;
        }

        // ========== 关键技术2: 高频心跳保持 NAT 映射活跃（改进版：支持查询）==========
        public void StartHeartbeat()
        {
            isRunning = true;
            Task.Run(async () =>
            {
                // 使用第一个可用的服务器
                IPEndPoint serverEndPoint = null;
                foreach (var ip in SERVER_IPS)
                {
                    try
                    {
                        serverEndPoint = new IPEndPoint(IPAddress.Parse(ip), SERVER_PORT);
                        break;
                    }
                    catch { }
                }
                
                if (serverEndPoint == null) return;
                
                while (isRunning)
                {
                    try
                    {
                        string heartbeat;
                        
                        // 检查是否有待查询的目标
                        lock (queryLock)
                        {
                            if (!string.IsNullOrEmpty(currentQueryTarget))
                            {
                                // 心跳中携带查询请求
                                heartbeat = $"HEARTBEAT:{myPeerID}:QUERY:{currentQueryTarget}";
                                logger.Debug($"💓 心跳+查询 [{DateTime.Now:HH:mm:ss}] 查询目标: {currentQueryTarget}");
                            }
                            else
                            {
                                // 普通心跳
                                heartbeat = $"HEARTBEAT:{myPeerID}";
                                logger.Debug($"💓 心跳 [{DateTime.Now:HH:mm:ss}]");
                            }
                        }
                        
                        byte[] data = Encoding.UTF8.GetBytes(heartbeat);
                        await udpClient.SendAsync(data, data.Length, serverEndPoint);
                        logger.Debug($"📤 已发送到 {serverEndPoint}: {heartbeat}");
                        
                        await Task.Delay(HEARTBEAT_INTERVAL);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ 心跳失败: {ex.Message}");
                    }
                }
            });
        }

        // ========== 关键技术3: 双向同时打洞（重构版，不创建独立接收循环）==========
        public async Task<bool> PunchHoleAsync(PeerInfo targetPeer)
        {
            logger.Info($"\n🎯 开始P2P打洞到: {targetPeer.PublicIP}:{targetPeer.PublicPort}");
            
            var targetEndPoint = new IPEndPoint(
                IPAddress.Parse(targetPeer.PublicIP), 
                targetPeer.PublicPort
            );

            // 创建打洞任务（主接收循环会处理响应）
            lock (punchLock)
            {
                punchResultTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                currentPunchTarget = targetPeer;
            }

            // ========== 核心：快速多次发送打洞包 ==========
            // 创建发送任务
            var sendTask = Task.Run(async () =>
            {
                for (int i = 0; i < PUNCH_RETRY; i++)
                {
                    try
                    {
                        string punchMsg = $"PUNCH:{myPeerID}";
                        byte[] data = Encoding.UTF8.GetBytes(punchMsg);
                        await udpClient.SendAsync(data, data.Length, targetEndPoint);
                        
                        logger.Debug($"🔨 打洞尝试 {i + 1}/{PUNCH_RETRY} -> {targetEndPoint}");
                        await Task.Delay(100); // 每100ms一次
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"⚠️ 打洞发送失败: {ex.Message}");
                    }
                }
            });

            // 等待打洞成功或超时（3秒）
            var timeoutTask = Task.Delay(3000);
            var completedTask = await Task.WhenAny(punchResultTask.Task, timeoutTask).ConfigureAwait(false);

            bool success = false;
            if (completedTask == punchResultTask.Task)
            {
                success = await punchResultTask.Task.ConfigureAwait(false);
                if (success)
                {
                    logger.Info($"✅ P2P 打洞成功！目标: {currentTarget}");
                }
            }
            else
            {
                logger.Warn($"⚠️ P2P 打洞超时");
            }

            // 清理
            lock (punchLock)
            {
                punchResultTask = null;
                currentPunchTarget = null;
            }

            // 打洞成功，启动保活
            if (success && currentTarget != null && !useRelay)
            {
                StartKeepAlive();
            }

            return success;
        }

        // ========== P2P连接保活（防止NAT超时）==========
        private void StartKeepAlive()
        {
            // 停止旧的保活任务
            StopKeepAlive();
            
            keepAliveCts = new CancellationTokenSource();
            var ct = keepAliveCts.Token;
            
            keepAliveTask = Task.Run(async () =>
            {
                logger.Info("💓 P2P保活已启动 (每30秒)");
                
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(30000, ct); // 30秒间隔
                        
                        if (currentTarget != null && !useRelay)
                        {
                            // 发送保活包
                            string keepAliveMsg = $"KEEPALIVE:{myPeerID}";
                            byte[] data = Encoding.UTF8.GetBytes(keepAliveMsg);
                            await udpClient.SendAsync(data, data.Length, currentTarget);
                            logger.Debug($"💓 发送P2P保活包到 {currentTarget}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"⚠️ P2P保活发送失败: {ex.Message}");
                    }
                }
                
                logger.Info("💓 P2P保活已停止");
            }, ct);
        }
        
        private void StopKeepAlive()
        {
            if (keepAliveCts != null)
            {
                keepAliveCts.Cancel();
                keepAliveCts.Dispose();
                keepAliveCts = null;
            }
        }

        // ========== 关键技术4: 多端口同时尝试（提高成功率）==========
        public async Task<bool> PunchHoleMultiPortAsync(PeerInfo targetPeer)
        {
            Console.WriteLine($"\n🎯 多端口打洞模式");
            
            // 尝试目标端口及附近端口（端口预测，扩大范围）
            var portsToTry = new List<int>
            {
                targetPeer.PublicPort,
                targetPeer.PublicPort + 1,
                targetPeer.PublicPort - 1,
                targetPeer.PublicPort + 2,
                targetPeer.PublicPort - 2,
                targetPeer.PublicPort + 3,
                targetPeer.PublicPort - 3,
                targetPeer.PublicPort + 4,
                targetPeer.PublicPort - 4
            };

            var tasks = new List<Task<bool>>();
            
            foreach (var port in portsToTry)
            {
                if (port > 0 && port < 65536)
                {
                    var testPeer = new PeerInfo
                    {
                        PublicIP = targetPeer.PublicIP,
                        PublicPort = port,
                        PeerID = targetPeer.PeerID
                    };
                    
                    tasks.Add(PunchHoleAsync(testPeer));
                    await Task.Delay(100); // 错开时间
                }
            }

            var results = await Task.WhenAll(tasks);
            return Array.Exists(results, r => r == true);
        }

        // ========== 关键技术5: 降级到服务器中转（打洞失败时）==========
        private bool useRelay = false;

        public async Task<bool> ConnectWithFallbackAsync(PeerInfo targetPeer)
        {
            logger.Info($"\n🔗 尝试连接到: {targetPeer.PeerID}");
            var startTime = DateTime.Now;
            
            // 步骤1：查询目标节点信息（带超时和重试）
            logger.Info("📡 步骤1: 查询目标节点公网地址...");
            PeerInfo peerInfo = null;
            
            // 最多重试2次查询
            for (int retry = 0; retry < 2 && peerInfo == null; retry++)
            {
                if (retry > 0)
                {
                    logger.Info($"🔄 重试查询 ({retry + 1}/2)...");
                    await Task.Delay(500); // 等待500ms再重试
                }
                
                peerInfo = await QueryPeerInfoAsync(targetPeer.PeerID);
            }
            
            if (peerInfo != null)
            {
                logger.Info($"✅ 获取到目标节点地址: {peerInfo.PublicIP}:{peerInfo.PublicPort}");
                
                // 步骤2：尝试 P2P 打洞（智能重试）
                logger.Info("🎯 步骤2: 尝试 P2P 打洞...");
                
                // 首次尝试
                bool punchSuccess = await PunchHoleAsync(peerInfo);
                
                // 如果首次失败且耗时<2秒，快速重试一次
                if (!punchSuccess)
                {
                    var elapsed = (DateTime.Now - startTime).TotalSeconds;
                    if (elapsed < 2)
                    {
                        logger.Info("🔄 快速重试P2P打洞...");
                        await Task.Delay(200); // 短暂延迟
                        punchSuccess = await PunchHoleAsync(peerInfo);
                    }
                }
                
                if (punchSuccess)
                {
                    logger.Info($"✅ P2P 直连成功！耗时: {(DateTime.Now - startTime).TotalSeconds:F1}秒");
                    useRelay = false;
                    // currentTarget 已在打洞成功时设置
                    return true;
                }
                
                logger.Warn($"⚠️ P2P 打洞失败，耗时: {(DateTime.Now - startTime).TotalSeconds:F1}秒，降级到服务器中转...");
            }
            else
            {
                logger.Warn("⚠️ 无法获取目标节点信息，直接尝试服务器中转...");
            }
            
            // 步骤3：降级到服务器中转（最后的保障）
            logger.Info("🔄 步骤3: 启用服务器中转模式...");
            bool relaySuccess = await SetupRelayAsync(targetPeer);
            
            if (relaySuccess)
            {
                logger.Info($"✅ 服务器中转模式已启用！总耗时: {(DateTime.Now - startTime).TotalSeconds:F1}秒");
                useRelay = true;
                return true;
            }

            logger.Error($"❌ 所有连接方式均失败，总耗时: {(DateTime.Now - startTime).TotalSeconds:F1}秒");
            return false;
        }
        
        // ========== 从服务器查询节点信息（改进版：通过心跳机制）==========
        private async Task<PeerInfo> QueryPeerInfoAsync(string targetPeerID)
        {
            logger.Info($"🔍 查询节点信息: {targetPeerID}（通过心跳机制）");
            
            // 创建查询任务
            TaskCompletionSource<PeerInfo> queryTask;
            
            lock (queryLock)
            {
                // 检查是否已有查询在进行
                if (currentQueryTask != null)
                {
                    logger.Warn("⚠️ 已有查询在进行，等待完成...");
                    return null;
                }
                
                // 设置当前查询
                currentQueryTarget = targetPeerID;
                currentQueryTask = new TaskCompletionSource<PeerInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                queryTask = currentQueryTask;
            }
            
            try
            {
                // 等待心跳携带查询并收到响应（最多等待5秒，因为心跳间隔1秒）
                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(queryTask.Task, timeoutTask).ConfigureAwait(false);
                
                if (completedTask == timeoutTask)
                {
                    logger.Warn($"⚠️ 查询超时: {targetPeerID}");
                    lock (queryLock)
                    {
                        currentQueryTask = null;
                        currentQueryTarget = null;
                    }
                    return null;
                }
                
                return await queryTask.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error($"⚠️ 查询异常: {ex.Message}");
                lock (queryLock)
                {
                    currentQueryTask = null;
                    currentQueryTarget = null;
                }
                return null;
            }
        }

        // ========== 设置服务器中转（带重试）==========
        private async Task<bool> SetupRelayAsync(PeerInfo targetPeer)
        {
            // 尝试多个服务器，每个服务器最多重试2次
            foreach (var serverIP in SERVER_IPS)
            {
                for (int retry = 0; retry < 2; retry++)
                {
                    try
                    {
                        if (retry > 0)
                        {
                            logger.Info($"🔄 重试中转请求 ({retry + 1}/2) 到 {serverIP}...");
                            await Task.Delay(300); // 短暂延迟
                        }
                        
                        var serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), SERVER_PORT);
                        
                        // 创建中转响应等待任务
                        lock (relayLock)
                        {
                            relayResponseTask = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                        }
                        
                        // 通知服务器开启中转
                        string relayMsg = $"RELAY_START:{myPeerID}:{targetPeer.PeerID}";
                        byte[] data = Encoding.UTF8.GetBytes(relayMsg);
                        await udpClient.SendAsync(data, data.Length, serverEndPoint);
                        logger.Info($"📤 发送中转请求到 {serverIP}: {relayMsg}");

                        // 等待服务器确认（通过TaskCompletionSource）
                        var timeoutTask = Task.Delay(2000); // 缩短到2秒
                        var completedTask = await Task.WhenAny(relayResponseTask.Task, timeoutTask).ConfigureAwait(false);
                        
                        if (completedTask == relayResponseTask.Task)
                        {
                            string response = await relayResponseTask.Task.ConfigureAwait(false);
                            logger.Info($"📥 收到中转响应: {response}");
                            
                            if (response == "RELAY_OK")
                            {
                                currentTarget = serverEndPoint;
                                logger.Info($"💡 数据将通过服务器 {serverIP}:{SERVER_PORT} 中转");
                                return true;
                            }
                            else if (response == "RELAY_DENIED")
                            {
                                logger.Warn($"⛔ 服务器 {serverIP} 拒绝中转（可能不在同组）");
                                break; // 拒绝的话不重试
                            }
                        }
                        else
                        {
                            logger.Warn($"⏱️ 等待 {serverIP} 中转响应超时 ({retry + 1}/2)");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"⚠️ 服务器 {serverIP} 中转失败 ({retry + 1}/2): {ex.Message}");
                    }
                    finally
                    {
                        // 清理任务
                        lock (relayLock)
                        {
                            relayResponseTask = null;
                        }
                    }
                }
            }
            
            logger.Error("❌ 所有服务器中转都失败");
            return false;
        }

        // ========== 发送数据（自动选择直连/中转）==========
        public async Task SendDataAsync(IPEndPoint target, string message)
        {
            string finalMsg;
            IPEndPoint finalTarget;

            if (useRelay)
            {
                // 中转模式：包装消息
                finalMsg = $"RELAY_DATA:{myPeerID}:{message}";
                finalTarget = currentTarget; // 发送到服务器
                Console.WriteLine($"📤 [中转] 发送: {message}");
            }
            else
            {
                // 直连模式
                finalMsg = message;
                finalTarget = target;
                Console.WriteLine($"📤 [直连] 发送: {message}");
            }

            byte[] data = Encoding.UTF8.GetBytes(finalMsg);
            await udpClient.SendAsync(data, data.Length, finalTarget);
        }

        // ========== 获取连接状态 ==========
        public string GetConnectionStatus()
        {
            if (useRelay)
                return "🔄 服务器中转";
            else
                return "⚡ P2P 直连";
        }

        // ========== 获取连接类型 ==========
        public ConnectionType GetConnectionType()
        {
            return useRelay ? ConnectionType.SERVER_RELAY : ConnectionType.P2P_DIRECT;
        }

        // ========== 发送数据到指定目标 ==========
        public async Task SendDataToTargetAsync(string targetPeerID, string message)
        {
            if (currentTarget == null)
            {
                logger.Warn("请先连接到目标节点");
                return;
            }

            await SendDataAsync(currentTarget, message);
        }

        // ========== 持续接收数据 ==========
        public async Task ReceiveDataAsync(CancellationToken ct)
        {
            logger.Info("🎧 数据接收循环已启动");
            int loopCount = 0;
            
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    logger.Debug($"🔄 接收循环运行中... (第 {loopCount} 次) [取消令牌: {ct.IsCancellationRequested}]");
                    loopCount++;
                    
                    logger.Debug($"   准备接收数据...");
                    var result = await udpClient.ReceiveAsync();
                    logger.Debug($"   已接收数据");
                    
                    string message = Encoding.UTF8.GetString(result.Buffer);
                    
                    // 调试：记录所有接收到的消息
                    logger.Info($"📥 [RAW] 收到 {result.Buffer.Length} 字节 from {result.RemoteEndPoint}: {message}");
                    logger.Debug($"   原始字节: {BitConverter.ToString(result.Buffer)}");
                    
                    // 处理注册响应
                    if (message.StartsWith("OK:"))
                    {
                        lock (registerLock)
                        {
                            if (registerResponseTask != null && !registerResponseTask.Task.IsCompleted)
                            {
                                logger.Debug("📨 处理注册响应");
                                registerResponseTask.SetResult(message);
                            }
                        }
                        logger.Debug("   ✅ 注册响应处理完成，继续循环...");
                        continue;
                    }
                    
                    // 处理心跳响应（携带查询结果）
                    if (message.StartsWith("HEARTBEAT_OK"))
                    {
                        var parts = message.Split(':');
                        
                        // 检查是否携带节点信息
                        if (parts.Length >= 4 && parts[1] == "PEER_INFO")
                        {
                            // 格式: HEARTBEAT_OK:PEER_INFO:IP:Port
                            logger.Debug($"   处理查询响应: {message}");
                            lock (queryLock)
                            {
                                if (currentQueryTask != null)
                                {
                                    var peerInfo = new PeerInfo
                                    {
                                        PeerID = currentQueryTarget,
                                        PublicIP = parts[2],
                                        PublicPort = int.Parse(parts[3])
                                    };
                                    
                                    logger.Info($"✅ 通过心跳获取节点信息: {currentQueryTarget} -> {peerInfo.PublicIP}:{peerInfo.PublicPort}");
                                    currentQueryTask.SetResult(peerInfo);
                                    currentQueryTask = null;
                                    currentQueryTarget = null;
                                }
                                else
                                {
                                    logger.Debug("   收到查询响应但没有等待任务");
                                }
                            }
                        }
                        else if (parts.Length >= 3 && parts[1] == "ERROR")
                        {
                            // 查询错误
                            lock (queryLock)
                            {
                                if (currentQueryTask != null)
                                {
                                    logger.Warn($"⚠️ 查询失败: {parts[2]}");
                                    currentQueryTask.SetResult(null);
                                    currentQueryTask = null;
                                    currentQueryTarget = null;
                                }
                            }
                        }
                        
                        continue;
                    }
                    
                    // 处理中转响应
                    if (message == "RELAY_OK" || message == "RELAY_DENIED")
                    {
                        lock (relayLock)
                        {
                            if (relayResponseTask != null && !relayResponseTask.Task.IsCompleted)
                            {
                                logger.Info($"📨 处理中转响应: {message}");
                                relayResponseTask.SetResult(message);
                            }
                            else
                            {
                                logger.Warn($"⚠️ 收到中转响应但没有等待任务: {message}, Task={relayResponseTask}, Completed={relayResponseTask?.Task.IsCompleted}");
                            }
                        }
                        continue;
                    }
                    
                    // 处理P2P打洞消息
                    if (message.StartsWith("PUNCH:"))
                    {
                        // 收到对方打洞包，立即回复
                        var parts = message.Split(':');
                        if (parts.Length >= 2)
                        {
                            string fromPeer = parts[1];
                            logger.Info($"📨 收到打洞包: {fromPeer} from {result.RemoteEndPoint}");
                            
                            // 立即回复打洞成功
                            string reply = $"PUNCH_OK:{myPeerID}";
                            byte[] replyData = Encoding.UTF8.GetBytes(reply);
                            await udpClient.SendAsync(replyData, replyData.Length, result.RemoteEndPoint);
                            logger.Info($"📤 已回复打洞响应到 {result.RemoteEndPoint}");
                            
                            // 如果正在打洞，标记成功
                            lock (punchLock)
                            {
                                if (punchResultTask != null && !punchResultTask.Task.IsCompleted)
                                {
                                    currentTarget = result.RemoteEndPoint;
                                    logger.Info($"✅ P2P 打洞成功！对方主动打洞");
                                    punchResultTask.SetResult(true);
                                }
                            }
                        }
                        continue;
                    }
                    
                    if (message.StartsWith("PUNCH_OK:"))
                    {
                        // 收到对方打洞响应，打洞成功
                        var parts = message.Split(':');
                        if (parts.Length >= 2)
                        {
                            string fromPeer = parts[1];
                            logger.Info($"✅ 收到打洞成功响应: {fromPeer} from {result.RemoteEndPoint}");
                            
                            lock (punchLock)
                            {
                                if (punchResultTask != null && !punchResultTask.Task.IsCompleted)
                                {
                                    currentTarget = result.RemoteEndPoint;
                                    logger.Info($"✅ P2P 打洞成功！");
                                    punchResultTask.SetResult(true);
                                }
                            }
                        }
                        continue;
                    }
                    
                    // 处理服务器通知的打洞请求（双向打洞）
                    if (message.StartsWith("PUNCH_START:"))
                    {
                        // 格式: PUNCH_START:PeerID:IP:Port
                        var parts = message.Split(':');
                        if (parts.Length >= 4)
                        {
                            string fromPeer = parts[1];
                            string peerIP = parts[2];
                            int peerPort = int.Parse(parts[3]);
                            
                            logger.Info($"🎯 收到服务器通知: {fromPeer} 想要连接，立即开始打洞...");
                            
                            // 立即向对方发送打洞包（不等待对方先发）
                            var targetEndPoint = new IPEndPoint(IPAddress.Parse(peerIP), peerPort);
                            
                            _ = Task.Run(async () =>
                            {
                                // 快速发送多个打洞包
                                for (int i = 0; i < 10; i++)
                                {
                                    try
                                    {
                                        string punchMsg = $"PUNCH:{myPeerID}";
                                        byte[] data = Encoding.UTF8.GetBytes(punchMsg);
                                        await udpClient.SendAsync(data, data.Length, targetEndPoint);
                                        logger.Debug($"🔨 被动打洞 {i + 1}/10 -> {targetEndPoint}");
                                        await Task.Delay(100);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Warn($"⚠️ 被动打洞发送失败: {ex.Message}");
                                    }
                                }
                            });
                        }
                        continue;
                    }
                    
                    // 处理P2P保活包
                    if (message.StartsWith("KEEPALIVE:"))
                    {
                        var parts = message.Split(':');
                        if (parts.Length >= 2)
                        {
                            string fromPeer = parts[1];
                            logger.Debug($"💓 收到P2P保活包: {fromPeer}");
                            
                            // 回复保活确认
                            string reply = $"KEEPALIVE_OK:{myPeerID}";
                            byte[] replyData = Encoding.UTF8.GetBytes(reply);
                            await udpClient.SendAsync(replyData, replyData.Length, result.RemoteEndPoint);
                        }
                        continue;
                    }
                    
                    if (message.StartsWith("KEEPALIVE_OK:"))
                    {
                        logger.Debug($"💓 收到P2P保活确认");
                        continue;
                    }
                    
                    // 处理端口转发消息（服务端接收）
                    if (message.StartsWith("FORWARD:"))
                    {
                        _ = Task.Run(() => HandleForwardMessageAsync(message, result.RemoteEndPoint));
                        continue;
                    }
                    
                    // 处理端口转发响应（客户端接收）
                    if (message.StartsWith("FORWARD_RESPONSE:"))
                    {
                        OnForwardResponse?.Invoke(message);
                        continue;
                    }
                    
                    // 处理中转消息
                    if (message.StartsWith("RELAYED:"))
                    {
                        var parts = message.Split(new[] { ':' }, 3);
                        if (parts.Length >= 3)
                        {
                            string fromPeer = parts[1];
                            string actualMsg = parts[2];
                            logger.Info($"📥 [中转] 收到 [{fromPeer}]: {actualMsg}");
                        }
                    }
                    else
                    {
                        // 普通消息
                        logger.Info($"📥 [直连] 收到 [{result.RemoteEndPoint}]: {message}");
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.Error($"⚠️ 接收异常: {ex.Message}");
                    logger.Error($"   异常堆栈: {ex.StackTrace}");
                }
            }
            
            logger.Warn($"🛑 接收循环已退出 [取消令牌: {ct.IsCancellationRequested}]");
        }

        // ========== 辅助方法：解析服务器响应 ==========
        private bool ParseServerResponse(string response)
        {
            try
            {
                // 格式: "OK:公网IP:公网端口"
                var parts = response.Split(':');
                if (parts.Length >= 3 && parts[0] == "OK")
                {
                    myPublicInfo.PublicIP = parts[1];
                    myPublicInfo.PublicPort = int.Parse(parts[2]);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ 解析响应失败: {ex.Message}");
            }
            return false;
        }

        // ========== 处理端口转发消息 ==========
        private async Task HandleForwardMessageAsync(string message, IPEndPoint remoteEndPoint)
        {
            try
            {
                // 格式: FORWARD:ConnectionID:RequestID:TargetPort:Base64Data
                var parts = message.Split(new[] { ':' }, 5);
                if (parts.Length < 5) return;
                
                string connectionId = parts[1];
                string requestId = parts[2];
                int targetPort = int.Parse(parts[3]);
                byte[] data = Convert.FromBase64String(parts[4]);
                
                logger.Debug($"📨 收到转发数据: 连接{connectionId}, 目标端口 {targetPort}, 数据长度 {data.Length} 字节");
                
                // 获取或创建TCP连接
                TcpClient tcpClient = null;
                NetworkStream stream = null;
                bool isNewConnection = false;
                
                try
                {
                    lock (forwardLock)
                    {
                        if (!forwardConnections.TryGetValue(connectionId, out tcpClient) || !tcpClient.Connected)
                        {
                            // 创建新连接
                            tcpClient = new TcpClient();
                            forwardConnections[connectionId] = tcpClient;
                            forwardRemoteEPs[connectionId] = remoteEndPoint;
                            isNewConnection = true;
                            logger.Info($"🔌 创建新TCP连接: {connectionId} → 127.0.0.1:{targetPort}");
                        }
                    }
                    
                    // 确保已连接
                    if (!tcpClient.Connected)
                    {
                        await tcpClient.ConnectAsync("127.0.0.1", targetPort);
                        logger.Debug($"✅ TCP连接已建立: {connectionId}");
                        
                        // 启动后台读取任务
                        stream = tcpClient.GetStream();
                        var readTask = StartBackgroundReadTask(connectionId, stream, remoteEndPoint);
                        lock (forwardLock)
                        {
                            forwardReadTasks[connectionId] = readTask;
                        }
                        logger.Debug($"🔄 已启动后台读取任务: {connectionId}");
                    }
                    else
                    {
                        stream = tcpClient.GetStream();
                    }
                    
                    // 发送数据到本地端口（不等待响应，后台任务会处理）
                    await stream.WriteAsync(data, 0, data.Length);
                    logger.Debug($"✅ 数据已转发到本地端口 {targetPort} (连接{connectionId})");
                }
                catch (Exception ex)
                {
                    logger.Error($"❌ 转发失败: {ex.Message}");
                    
                    // 出错时清理连接
                    CleanupConnection(connectionId);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"❌ 处理转发消息失败: {ex.Message}");
            }
        }
        
        // ========== 后台读取任务 ==========
        private async Task StartBackgroundReadTask(string connectionId, NetworkStream stream, IPEndPoint remoteEndPoint)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (isRunning)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        // 连接关闭
                        logger.Debug($"🔌 SQL Server关闭了连接: {connectionId}");
                        break;
                    }
                    
                    // 使用ConnectionID作为标识发送响应
                    string responseData = Convert.ToBase64String(buffer, 0, bytesRead);
                    string responseMsg = $"FORWARD_RESPONSE:{connectionId}:{responseData}";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseMsg);
                    await udpClient.SendAsync(responseBytes, responseBytes.Length, remoteEndPoint);
                    logger.Debug($"📤 [后台] 已发回响应: {bytesRead} 字节 (连接{connectionId})");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"❌ [后台读取] 连接 {connectionId} 异常: {ex.Message}");
            }
            finally
            {
                CleanupConnection(connectionId);
            }
        }
        
        // ========== 清理连接 ==========
        private void CleanupConnection(string connectionId)
        {
            lock (forwardLock)
            {
                if (forwardConnections.ContainsKey(connectionId))
                {
                    forwardConnections[connectionId]?.Close();
                    forwardConnections.Remove(connectionId);
                }
                forwardReadTasks.Remove(connectionId);
                forwardRemoteEPs.Remove(connectionId);
                logger.Debug($"🗑️ 已清理连接: {connectionId}");
            }
        }
        
        // ========== 停止 ==========
        public void Stop()
        {
            isRunning = false;
            
            // 清理所有连接
            lock (forwardLock)
            {
                foreach (var conn in forwardConnections.Values)
                {
                    conn?.Close();
                }
                forwardConnections.Clear();
                forwardReadTasks.Clear();
                forwardRemoteEPs.Clear();
            }
            
            udpClient?.Close();
        }
    }
}
