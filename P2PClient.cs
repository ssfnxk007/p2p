/*
 * 企业级 P2P 客户端
 * 功能完整版：配置文件 + 分组密钥 + 端口转发 + 详细日志
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using P2PConfig;

namespace P2PPuncher
{
    class P2PClient
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
            logger.Info("  企业级 P2P 通信系统 - 客户端");
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

            // 2. 启动接收循环（必须在注册前启动，避免接收冲突）
            var cts = new CancellationTokenSource();
            var receiveTask = puncher.ReceiveDataAsync(cts.Token);
            
            // 监控接收循环的异常
            _ = receiveTask.ContinueWith(t => 
            {
                if (t.IsFaulted)
                {
                    logger.Error($"❌❌❌ 接收循环异常退出: {t.Exception?.GetBaseException().Message}");
                    logger.Error($"   异常堆栈: {t.Exception?.GetBaseException().StackTrace}");
                }
                else if (t.IsCompleted)
                {
                    logger.Warn("⚠️ 接收循环正常退出");
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

            // 4. 启动端口转发（并自动连接目标节点）
            if (config.PortForwards != null && config.PortForwards.Count > 0)
            {
                logger.Info($"配置了 {config.PortForwards.Count} 个端口转发规则");
                var forwarder = new PortForwarder(puncher, logger);
                
                // 收集所有目标节点
                var targetPeers = new HashSet<string>();
                
                foreach (var rule in config.PortForwards)
                {
                    await forwarder.StartForwardAsync(rule);
                    targetPeers.Add(rule.TargetPeerID);
                }
                
                // 自动连接到所有目标节点（延迟2秒等待对方上线）
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000); // 等待2秒，确保双方都注册完成
                    
                    foreach (var targetPeerID in targetPeers)
                    {
                        logger.Info($"🔗 自动连接到目标节点: {targetPeerID}");
                        
                        var targetPeer = new PeerInfo
                        {
                            PeerID = targetPeerID,
                            PublicIP = "",  // 服务器会提供
                            PublicPort = 0
                        };
                        
                        bool success = await puncher.ConnectWithFallbackAsync(targetPeer);
                        
                        if (success)
                        {
                            logger.Info($"✅ 已连接到 {targetPeerID}");
                            logger.LogConnection(targetPeerID, puncher.GetConnectionType(), puncher.GetConnectionStatus());
                        }
                        else
                        {
                            logger.Error($"❌ 连接失败: {targetPeerID}");
                        }
                    }
                });
            }

            // 5. 命令行界面
            logger.Info("");
            logger.Info("========================================");
            logger.Info("系统已就绪！支持以下命令：");
            logger.Info("  connect <PeerID>  - 连接到指定节点");
            logger.Info("  send <message>    - 发送消息");
            logger.Info("  status            - 查看连接状态");
            logger.Info("  quit              - 退出程序");
            logger.Info("========================================");
            logger.Info("");

            string currentTarget = null;

            while (true)
            {
                Console.Write("> ");
                string cmd = Console.ReadLine();
                var parts = cmd.Split(' ', 2);

                try
                {
                    switch (parts[0].ToLower())
                    {
                        case "connect":
                            if (parts.Length < 2)
                            {
                                logger.Warn("用法: connect <PeerID>");
                                break;
                            }
                            
                            currentTarget = parts[1];
                            logger.Info($"正在连接到 {currentTarget}...");
                            
                            var targetPeer = new PeerInfo
                            {
                                PeerID = currentTarget,
                                PublicIP = "",  // 服务器会提供
                                PublicPort = 0
                            };
                            
                            bool success = await puncher.ConnectWithFallbackAsync(targetPeer);
                            
                            if (success)
                            {
                                logger.Info($"✅ 已连接到 {currentTarget}");
                                logger.LogConnection(currentTarget, puncher.GetConnectionType(), puncher.GetConnectionStatus());
                            }
                            else
                            {
                                logger.Error($"❌ 连接失败");
                            }
                            break;

                        case "send":
                            if (currentTarget == null)
                            {
                                logger.Warn("请先使用 connect 命令连接节点");
                                break;
                            }
                            
                            if (parts.Length < 2)
                            {
                                logger.Warn("用法: send <message>");
                                break;
                            }
                            
                            await puncher.SendDataToTargetAsync(currentTarget, parts[1]);
                            logger.Info($"✅ 消息已发送");
                            break;

                        case "status":
                            logger.Info($"当前状态: {puncher.GetConnectionStatus()}");
                            if (currentTarget != null)
                            {
                                logger.Info($"当前目标: {currentTarget}");
                                logger.Info($"连接类型: {puncher.GetConnectionType()}");
                            }
                            break;

                        case "quit":
                            logger.Info("正在退出...");
                            cts.Cancel();
                            puncher.Stop();
                            logger.Close();
                            return;

                        default:
                            logger.Warn($"未知命令: {parts[0]}");
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
