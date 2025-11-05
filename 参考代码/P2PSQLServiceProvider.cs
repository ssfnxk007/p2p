/*
 * SQL隧道服务提供端
 * 功能：转发P2P请求到本地SQL Server (localhost:1433)
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using P2PConfig;

namespace P2PPuncher
{
    class P2PSQLServiceProvider
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
            logger.Info("  SQL隧道服务端 (转发到本地SQL Server)");
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

            logger.Info("✅ 服务端已注册，等待客户端连接...");
            
            // 等待2秒
            await Task.Delay(2000);
            
            // 5. 启动SQL隧道服务端
            logger.Info("");
            logger.Info("🚇 正在启动SQL隧道服务端...");
            
            // 检查本地SQL Server配置
            Console.Write("请输入本地SQL Server地址（回车使用127.0.0.1）: ");
            string sqlHost = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(sqlHost)) sqlHost = "127.0.0.1";
            
            Console.Write("请输入本地SQL Server端口（回车使用1433）: ");
            string sqlPortStr = Console.ReadLine();
            int sqlPort = string.IsNullOrWhiteSpace(sqlPortStr) ? 1433 : int.Parse(sqlPortStr);
            
            var sqlTunnel = new P2PSQLTunnelServer(puncher, logger, sqlHost, sqlPort);
            
            // 在后台启动SQL隧道
            _ = Task.Run(async () =>
            {
                try
                {
                    await sqlTunnel.StartAsync();
                }
                catch (Exception ex)
                {
                    logger.Error($"❌ SQL隧道服务端异常: {ex.Message}");
                }
            });
            
            await Task.Delay(1000);  // 等待启动
            
            logger.Info("");
            logger.Info("========================================");
            logger.Info("✅ SQL隧道服务端已就绪！");
            logger.Info("");
            logger.Info("📊 配置信息：");
            logger.Info($"   本地SQL Server: {sqlHost}:{sqlPort}");
            logger.Info($"   监听P2P连接: {config.PeerID}");
            logger.Info($"   服务组: {config.GroupID}");
            logger.Info("");
            logger.Info("💡 等待访问客户端连接...");
            logger.Info("========================================");
            logger.Info("");
            
            logger.Info("支持以下命令：");
            logger.Info("  status    - 查看连接状态");
            logger.Info("  stats     - 查看统计信息");
            logger.Info("  quit      - 退出程序");
            logger.Info("");

            // 6. 命令行界面
            while (true)
            {
                Console.Write("> ");
                string cmd = Console.ReadLine()?.ToLower();

                try
                {
                    switch (cmd)
                    {
                        case "status":
                            logger.Info($"服务状态: 运行中");
                            logger.Info($"节点ID: {config.PeerID}");
                            logger.Info($"SQL Server: {sqlHost}:{sqlPort}");
                            logger.Info($"P2P连接状态: {puncher.GetConnectionStatus()}");
                            break;

                        case "stats":
                            logger.Info($"📊 统计信息:");
                            logger.Info($"   服务运行时间: {DateTime.Now.Subtract(System.Diagnostics.Process.GetCurrentProcess().StartTime):hh\\:mm\\:ss}");
                            logger.Info($"   活跃会话: 查看日志文件获取详细信息");
                            break;

                        case "quit":
                        case "exit":
                            logger.Info("正在退出...");
                            sqlTunnel.Stop();
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
    }
}
