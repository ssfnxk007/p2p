/*
 * 企业级 P2P 服务器
 * 功能完整版：配置文件 + 分组密钥验证 + 详细日志
 */

using System;
using System.Threading.Tasks;
using P2PConfig;

namespace P2PServer
{
    class P2PServerMain
    {
        static void Main(string[] args)
        {
            // 设置控制台编码为 UTF-8
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;
            
            // ========== 加载配置 ==========
            Console.WriteLine("正在加载配置文件...");
            var config = P2PConfig.ServerConfig.Load("server_config.json");
            
            // ========== 初始化日志 ==========
            P2PConfig.Logger.Initialize(config.Logging);
            var logger = P2PConfig.Logger.Get();
            
            logger.Info("========================================");
            logger.Info("  企业级 P2P 通信系统 - 服务器");
            logger.Info("  版本: 1.0.0");
            logger.Info("========================================");
            logger.Info("");
            
            logger.Info($"监听端口: {config.ServerPort}");
            logger.Info($"最大客户端: {config.MaxClients}");
            logger.Info($"已配置组数: {config.Groups.Count}");
            logger.Info("");

            foreach (var group in config.Groups)
            {
                logger.Info($"  • 组: {group.GroupID} - {group.Description}");
            }
            logger.Info("");

            // ========== 启动服务器 ==========
            var server = new P2PServer(config, logger);
            server.Start();

            // ========== 命令行界面 ==========
            logger.Info("========================================");
            logger.Info("系统已就绪！支持以下命令：");
            logger.Info("  list   - 查看在线客户端");
            logger.Info("  stats  - 查看统计信息");
            logger.Info("  quit   - 退出程序");
            logger.Info("========================================");
            logger.Info("");

            while (true)
            {
                Console.Write("> ");
                string cmd = Console.ReadLine();

                switch (cmd.ToLower())
                {
                    case "list":
                        server.ShowClients();
                        break;

                    case "stats":
                        // TODO: 显示统计信息
                        logger.Info("统计功能开发中...");
                        break;

                    case "quit":
                        logger.Info("正在停止服务器...");
                        logger.Close();
                        return;

                    default:
                        logger.Warn($"未知命令: {cmd}");
                        break;
                }
            }
        }
    }
}
