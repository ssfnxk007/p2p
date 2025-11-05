/*
 * SQL隧道访问客户端
 * 功能：在本地1430端口提供SQL Server访问
 */

using System;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using P2PConfig;

namespace P2PPuncher
{
    class P2PSQLAccessClient
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            
            // ========== 加载配置 ==========
            Console.WriteLine("正在加载配置文件...");
            var config = ClientConfig.Load("client_config.json");
            
            // ========== 初始化日志 ==========
            Logger.Initialize(config.Logging);
            var logger = Logger.Get();
            
            logger.Info("========================================");
            logger.Info("  SQL隧道客户端 (本地端口: 1430)");
            logger.Info("  版本: 1.0.0");
            logger.Info("========================================");
            logger.Info("");
            
            logger.Info($"节点ID: {config.PeerID}");
            logger.Info($"组ID: {config.GroupID}");
            logger.Info($"服务器列表: {string.Join(", ", config.Servers)}");
            logger.Info("");

            // ========== 创建 P2P 实例 ==========
            var puncher = new UdpPuncher(
                config.PeerID,
                config.GroupID,
                config.GroupKey,
                config.Servers.ToArray(),
                config.ServerPort,
                logger
            );

            // 1. 初始化
            if (!puncher.Initialize())
            {
                logger.Error("初始化失败！");
                return;
            }

            // 2. 启动接收循环
            var cts = new CancellationTokenSource();
            var receiveTask = puncher.ReceiveDataAsync(cts.Token);
            
            // 监控接收循环的异常
            _ = receiveTask.ContinueWith(t => 
            {
                if (t.IsFaulted)
                {
                    logger.Error($"❌ 接收循环异常退出: {t.Exception?.GetBaseException().Message}");
                }
            });

            // 3. 注册到服务器
            logger.Info("正在注册到服务器...");
            if (!await puncher.RegisterToServerAsync())
            {
                logger.Error("注册失败！请检查服务器配置");
                return;
            }

            // 4. 启动心跳
            logger.Info("启动心跳保持...");
            puncher.StartHeartbeat();

            // 等待2秒确保服务端也注册完成
            await Task.Delay(2000);

            // 5. 连接到SQL服务提供端
            logger.Info("🔗 正在连接到SQL服务提供端...");
            
            var targetPeer = new PeerInfo
            {
                PeerID = "服务提供端",  // 从配置文件获取或硬编码
                PublicIP = "",
                PublicPort = 0
            };
            
            bool connected = await puncher.ConnectWithFallbackAsync(targetPeer);
            
            if (!connected)
            {
                logger.Error("❌ 无法建立P2P连接，退出程序");
                return;
            }
            
            logger.Info($"✅ P2P连接已建立！");
            logger.LogConnection(targetPeer.PeerID, puncher.GetConnectionType(), puncher.GetConnectionStatus());
            
            // 6. 启动SQL代理服务器（本地端口1430）
            logger.Info("");
            logger.Info("🚇 正在启动SQL代理服务器...");
            
            var sqlProxy = new P2PSQLTunnelClient(puncher, logger, 1430);
            
            // 在后台启动SQL代理
            _ = Task.Run(async () =>
            {
                try
                {
                    await sqlProxy.StartAsync();
                }
                catch (Exception ex)
                {
                    logger.Error($"❌ SQL代理服务器异常: {ex.Message}");
                }
            });
            
            await Task.Delay(2000);  // 等待代理启动
            
            logger.Info("");
            logger.Info("========================================");
            logger.Info("✅ SQL隧道已就绪！");
            logger.Info("");
            logger.Info("📊 连接信息：");
            logger.Info($"   本地监听端口: 1430");
            logger.Info($"   远程SQL Server: 通过P2P直连");
            logger.Info($"   连接类型: {puncher.GetConnectionType()}");
            logger.Info("");
            logger.Info("💡 使用方法：");
            logger.Info("   在您的应用中使用以下连接字符串：");
            logger.Info("   Server=localhost,1430;Database=YourDB;User Id=sa;Password=xxx;");
            logger.Info("");
            logger.Info("========================================");
            logger.Info("");
            
            // 7. 测试SQL连接（可选）
            await TestSQLConnection(logger);
            
            logger.Info("");
            logger.Info("支持以下命令：");
            logger.Info("  test      - 测试SQL连接");
            logger.Info("  status    - 查看连接状态");
            logger.Info("  quit      - 退出程序");
            logger.Info("");

            // 8. 命令行界面
            while (true)
            {
                Console.Write("> ");
                string cmd = Console.ReadLine()?.ToLower();

                try
                {
                    switch (cmd)
                    {
                        case "test":
                            await TestSQLConnection(logger);
                            break;

                        case "status":
                            logger.Info($"P2P连接状态: {puncher.GetConnectionStatus()}");
                            logger.Info($"连接类型: {puncher.GetConnectionType()}");
                            logger.Info($"目标节点: {targetPeer.PeerID}");
                            logger.Info($"本地SQL代理: localhost:1430");
                            break;

                        case "quit":
                        case "exit":
                            logger.Info("正在退出...");
                            sqlProxy.Stop();
                            cts.Cancel();
                            puncher.Stop();
                            logger.Close();
                            return;

                        case "":
                            break;

                        default:
                            logger.Warn($"未知命令: {cmd}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"命令执行失败: {ex.Message}");
                }
            }
        }

        static async Task TestSQLConnection(ILogger logger)
        {
            logger.Info("🧪 开始测试SQL连接...");
            
            // 提示用户输入连接信息
            Console.Write("请输入数据库名称（回车使用master）: ");
            string database = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(database)) database = "master";
            
            Console.Write("请输入用户名（回车使用sa）: ");
            string username = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(username)) username = "sa";
            
            Console.Write("请输入密码: ");
            string password = ReadPassword();
            
            string connectionString = 
                $"Server=localhost,1430;" +
                $"Database={database};" +
                $"User Id={username};" +
                $"Password={password};" +
                $"Connect Timeout=10;";

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    logger.Info("📡 正在连接...");
                    await conn.OpenAsync();
                    logger.Info("✅ 连接成功！");

                    // 查询SQL Server版本
                    var cmd = new SqlCommand("SELECT @@VERSION", conn);
                    string version = (string)await cmd.ExecuteScalarAsync();
                    
                    logger.Info($"📊 SQL Server版本:");
                    logger.Info($"   {version.Split('\n')[0]}");
                    
                    // 查询数据库列表
                    cmd = new SqlCommand("SELECT COUNT(*) FROM sys.databases", conn);
                    int dbCount = (int)await cmd.ExecuteScalarAsync();
                    logger.Info($"📊 数据库数量: {dbCount}");
                    
                    logger.Info("✅ 测试完成！SQL隧道工作正常");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"❌ SQL连接测试失败: {ex.Message}");
                logger.Error($"   请检查：");
                logger.Error($"   1. P2P连接是否正常");
                logger.Error($"   2. 服务端SQL隧道是否启动");
                logger.Error($"   3. 用户名密码是否正确");
            }
        }
        
        static string ReadPassword()
        {
            string password = "";
            ConsoleKeyInfo key;
            
            do
            {
                key = Console.ReadKey(true);
                
                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    password += key.KeyChar;
                    Console.Write("*");
                }
                else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password.Substring(0, password.Length - 1);
                    Console.Write("\b \b");
                }
            }
            while (key.Key != ConsoleKey.Enter);
            
            Console.WriteLine();
            return password;
        }
    }
}
