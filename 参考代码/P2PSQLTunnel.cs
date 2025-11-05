using System;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace P2PSystem
{
    /// <summary>
    /// SQL Server TCP隧道 - 服务端
    /// 部署在有SQL Server的机器上
    /// </summary>
    public class P2PSQLTunnelServer
    {
        private P2PPuncher p2pPuncher;
        private ILogger logger;
        private string sqlServerHost;
        private int sqlServerPort;
        private bool isRunning = false;

        // 会话管理：每个SessionID对应一个SQL连接
        private ConcurrentDictionary<string, TcpClient> sessions = new ConcurrentDictionary<string, TcpClient>();

        public P2PSQLTunnelServer(P2PPuncher puncher, ILogger logger, string sqlHost = "127.0.0.1", int sqlPort = 1433)
        {
            this.p2pPuncher = puncher;
            this.logger = logger;
            this.sqlServerHost = sqlHost;
            this.sqlServerPort = sqlPort;
        }

        public async Task StartAsync()
        {
            isRunning = true;
            logger.Info($"🚇 SQL隧道服务端已启动 (转发到 {sqlServerHost}:{sqlServerPort})");

            while (isRunning)
            {
                try
                {
                    var result = await p2pPuncher.ReceiveP2PDataAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    if (message.StartsWith("SQL_CONNECT:"))
                    {
                        // 新建SQL连接会话
                        string sessionID = message.Substring(12);
                        _ = Task.Run(() => HandleNewConnection(sessionID));
                    }
                    else if (message.StartsWith("SQL_DATA:"))
                    {
                        // 转发SQL数据包
                        _ = Task.Run(() => HandleSQLData(message));
                    }
                    else if (message.StartsWith("SQL_CLOSE:"))
                    {
                        // 关闭SQL连接
                        string sessionID = message.Substring(10);
                        CloseSession(sessionID);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"❌ SQL隧道服务端错误: {ex.Message}");
                }
            }
        }

        private async Task HandleNewConnection(string sessionID)
        {
            try
            {
                logger.Info($"🔌 创建新的SQL连接会话: {sessionID}");

                // 连接到真实的SQL Server
                var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(sqlServerHost, sqlServerPort);

                sessions[sessionID] = tcpClient;

                // 通知客户端连接成功
                await p2pPuncher.SendDataAsync($"SQL_CONNECTED:{sessionID}");

                // 启动接收SQL Server响应的任务
                _ = Task.Run(() => ReceiveSQLResponses(sessionID, tcpClient));

                logger.Info($"✅ SQL会话 {sessionID} 已建立");
            }
            catch (Exception ex)
            {
                logger.Error($"❌ 无法连接到SQL Server: {ex.Message}");
                await p2pPuncher.SendDataAsync($"SQL_ERROR:{sessionID}:连接失败");
            }
        }

        private async Task HandleSQLData(string message)
        {
            try
            {
                // 格式: SQL_DATA:SessionID:Base64Data
                var parts = message.Split(new[] { ':' }, 3);
                if (parts.Length < 3) return;

                string sessionID = parts[1];
                string base64Data = parts[2];
                byte[] sqlData = Convert.FromBase64String(base64Data);

                if (sessions.TryGetValue(sessionID, out TcpClient tcpClient))
                {
                    // 转发到SQL Server
                    var stream = tcpClient.GetStream();
                    await stream.WriteAsync(sqlData, 0, sqlData.Length);
                    logger.Debug($"📤 转发SQL数据: {sessionID} ({sqlData.Length} 字节)");
                }
                else
                {
                    logger.Warn($"⚠️ 会话不存在: {sessionID}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"❌ 处理SQL数据失败: {ex.Message}");
            }
        }

        private async Task ReceiveSQLResponses(string sessionID, TcpClient tcpClient)
        {
            var stream = tcpClient.GetStream();
            byte[] buffer = new byte[8192];

            try
            {
                while (tcpClient.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;  // 连接关闭

                    // 通过P2P发送SQL响应
                    string base64Data = Convert.ToBase64String(buffer, 0, bytesRead);
                    await p2pPuncher.SendDataAsync($"SQL_RESPONSE:{sessionID}:{base64Data}");
                    
                    logger.Debug($"📥 返回SQL响应: {sessionID} ({bytesRead} 字节)");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"❌ 接收SQL响应失败: {ex.Message}");
            }
            finally
            {
                CloseSession(sessionID);
            }
        }

        private void CloseSession(string sessionID)
        {
            if (sessions.TryRemove(sessionID, out TcpClient tcpClient))
            {
                tcpClient.Close();
                logger.Info($"🔌 关闭SQL会话: {sessionID}");
            }
        }

        public void Stop()
        {
            isRunning = false;
            
            // 关闭所有会话
            foreach (var session in sessions.Values)
            {
                session.Close();
            }
            sessions.Clear();
            
            logger.Info("🛑 SQL隧道服务端已停止");
        }
    }

    /// <summary>
    /// SQL Server TCP隧道 - 客户端代理
    /// 部署在本地机器上
    /// </summary>
    public class P2PSQLTunnelClient
    {
        private P2PPuncher p2pPuncher;
        private ILogger logger;
        private TcpListener tcpListener;
        private int localPort;
        private bool isRunning = false;

        // 会话管理：SessionID → 本地TCP连接
        private ConcurrentDictionary<string, TcpClient> localSessions = new ConcurrentDictionary<string, TcpClient>();

        public P2PSQLTunnelClient(P2PPuncher puncher, ILogger logger, int port = 1433)
        {
            this.p2pPuncher = puncher;
            this.logger = logger;
            this.localPort = port;
        }

        public async Task StartAsync()
        {
            isRunning = true;

            // 启动本地TCP监听
            tcpListener = new TcpListener(IPAddress.Loopback, localPort);
            tcpListener.Start();
            logger.Info($"🚇 SQL代理服务器已启动 (本地端口: {localPort})");
            logger.Info($"💡 可以使用 Server=localhost,{localPort} 连接SQL Server");

            // 接受本地连接
            _ = Task.Run(AcceptLocalConnections);

            // 接收P2P消息
            await ReceiveP2PMessages();
        }

        private async Task AcceptLocalConnections()
        {
            while (isRunning)
            {
                try
                {
                    var localClient = await tcpListener.AcceptTcpClientAsync();
                    string sessionID = Guid.NewGuid().ToString("N").Substring(0, 8);
                    
                    logger.Info($"📱 本地SQL客户端已连接: {localClient.Client.RemoteEndPoint} (会话: {sessionID})");

                    localSessions[sessionID] = localClient;

                    // 通过P2P请求建立SQL连接
                    await p2pPuncher.SendDataAsync($"SQL_CONNECT:{sessionID}");

                    // 启动转发任务
                    _ = Task.Run(() => ForwardLocalToP2P(sessionID, localClient));
                }
                catch (Exception ex)
                {
                    if (isRunning)
                    {
                        logger.Error($"❌ 接受本地连接失败: {ex.Message}");
                    }
                }
            }
        }

        private async Task ForwardLocalToP2P(string sessionID, TcpClient localClient)
        {
            var stream = localClient.GetStream();
            byte[] buffer = new byte[8192];

            try
            {
                while (localClient.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;  // 连接关闭

                    // 通过P2P发送到远程SQL Server
                    string base64Data = Convert.ToBase64String(buffer, 0, bytesRead);
                    await p2pPuncher.SendDataAsync($"SQL_DATA:{sessionID}:{base64Data}");
                    
                    logger.Debug($"📤 转发本地SQL请求: {sessionID} ({bytesRead} 字节)");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"❌ 转发本地数据失败: {ex.Message}");
            }
            finally
            {
                // 通知服务端关闭
                await p2pPuncher.SendDataAsync($"SQL_CLOSE:{sessionID}");
                CloseLocalSession(sessionID);
            }
        }

        private async Task ReceiveP2PMessages()
        {
            while (isRunning)
            {
                try
                {
                    var result = await p2pPuncher.ReceiveP2PDataAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    if (message.StartsWith("SQL_CONNECTED:"))
                    {
                        string sessionID = message.Substring(14);
                        logger.Info($"✅ SQL会话已建立: {sessionID}");
                    }
                    else if (message.StartsWith("SQL_RESPONSE:"))
                    {
                        // 格式: SQL_RESPONSE:SessionID:Base64Data
                        _ = Task.Run(() => HandleSQLResponse(message));
                    }
                    else if (message.StartsWith("SQL_ERROR:"))
                    {
                        var parts = message.Split(':');
                        if (parts.Length >= 2)
                        {
                            string sessionID = parts[1];
                            logger.Error($"❌ SQL连接错误: {sessionID}");
                            CloseLocalSession(sessionID);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"❌ 接收P2P消息失败: {ex.Message}");
                }
            }
        }

        private async Task HandleSQLResponse(string message)
        {
            try
            {
                // 格式: SQL_RESPONSE:SessionID:Base64Data
                var parts = message.Split(new[] { ':' }, 3);
                if (parts.Length < 3) return;

                string sessionID = parts[1];
                string base64Data = parts[2];
                byte[] responseData = Convert.FromBase64String(base64Data);

                if (localSessions.TryGetValue(sessionID, out TcpClient localClient))
                {
                    // 转发到本地SQL客户端
                    var stream = localClient.GetStream();
                    await stream.WriteAsync(responseData, 0, responseData.Length);
                    logger.Debug($"📥 返回SQL响应给本地: {sessionID} ({responseData.Length} 字节)");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"❌ 处理SQL响应失败: {ex.Message}");
            }
        }

        private void CloseLocalSession(string sessionID)
        {
            if (localSessions.TryRemove(sessionID, out TcpClient localClient))
            {
                localClient.Close();
                logger.Info($"🔌 关闭本地会话: {sessionID}");
            }
        }

        public void Stop()
        {
            isRunning = false;
            tcpListener?.Stop();
            
            // 关闭所有本地会话
            foreach (var session in localSessions.Values)
            {
                session.Close();
            }
            localSessions.Clear();
            
            logger.Info("🛑 SQL代理服务器已停止");
        }
    }

    /// <summary>
    /// SQL隧道使用示例
    /// </summary>
    public class SQLTunnelExample
    {
        public static async Task RunServerExample()
        {
            // 在ServiceProvider端（有SQL Server的机器）
            var config = new ClientConfig
            {
                PeerID = "服务提供端",
                GroupID = "测试组1",
                GroupKey = "test123",
                Servers = new[] { "42.51.41.138" }
            };

            var logger = new ConsoleLogger();
            var puncher = new P2PPuncher(config, logger);

            // 注册到服务器
            await puncher.RegisterToServerAsync();

            // 启动SQL隧道服务端
            var sqlTunnel = new P2PSQLTunnelServer(puncher, logger, "127.0.0.1", 1433);
            await sqlTunnel.StartAsync();
        }

        public static async Task RunClientExample()
        {
            // 在AccessClient端（本地机器）
            var config = new ClientConfig
            {
                PeerID = "访问客户端",
                GroupID = "测试组1",
                GroupKey = "test123",
                Servers = new[] { "42.51.41.138" }
            };

            var logger = new ConsoleLogger();
            var puncher = new P2PPuncher(config, logger);

            // 注册到服务器
            await puncher.RegisterToServerAsync();

            // 连接到服务提供端
            var target = new PeerInfo { PeerID = "服务提供端" };
            bool connected = await puncher.ConnectWithFallbackAsync(target);

            if (connected)
            {
                // 启动SQL代理服务器
                var sqlProxy = new P2PSQLTunnelClient(puncher, logger, 1433);
                await sqlProxy.StartAsync();

                // 现在可以使用本地连接字符串
                string connectionString = "Server=localhost,1433;Database=MyDB;User Id=sa;Password=YourPassword;";
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    Console.WriteLine("✅ 通过P2P隧道成功连接到SQL Server！");

                    var cmd = new SqlCommand("SELECT @@VERSION", conn);
                    string version = (string)await cmd.ExecuteScalarAsync();
                    Console.WriteLine($"SQL Server版本: {version}");
                }
            }
        }
    }
}
