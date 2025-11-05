/*
 * P2P 配置管理和日志系统
 * 支持配置文件、分组密钥、端口转发
 */

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace P2PConfig
{
    // ========== 客户端配置 ==========
    public class ClientConfig
    {
        public string PeerID { get; set; }
        public string GroupID { get; set; }
        public string GroupKey { get; set; }
        public List<string> Servers { get; set; }
        public int ServerPort { get; set; }
        public List<PortForwardRule> PortForwards { get; set; }
        public LoggingConfig Logging { get; set; }
        public AdvancedConfig Advanced { get; set; }

        public static ClientConfig Load(string path = "client_config.json")
        {
            if (!File.Exists(path))
            {
                var defaultConfig = new ClientConfig
                {
                    PeerID = "Client1",
                    GroupID = "default",
                    GroupKey = "change_me",
                    Servers = new List<string> { "127.0.0.1" },
                    ServerPort = 8000,
                    PortForwards = new List<PortForwardRule>(),
                    Logging = new LoggingConfig(),
                    Advanced = new AdvancedConfig()
                };
                
                File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true }));
                return defaultConfig;
            }

            return JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(path));
        }
    }

    // ========== 服务器配置 ==========
    public class ServerConfig
    {
        public int ServerPort { get; set; }
        public int MaxClients { get; set; }
        public List<GroupConfig> Groups { get; set; }
        public LoggingConfig Logging { get; set; }
        public ServerAdvancedConfig Advanced { get; set; }

        public static ServerConfig Load(string path = "server_config.json")
        {
            if (!File.Exists(path))
            {
                var defaultConfig = new ServerConfig
                {
                    ServerPort = 8000,
                    MaxClients = 1000,
                    Groups = new List<GroupConfig>
                    {
                        new GroupConfig { GroupID = "default", GroupKey = "change_me", Description = "默认组" }
                    },
                    Logging = new LoggingConfig(),
                    Advanced = new ServerAdvancedConfig()
                };
                
                File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true }));
                return defaultConfig;
            }

            return JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path));
        }
    }

    // ========== 端口转发规则 ==========
    public class PortForwardRule
    {
        public string Name { get; set; }
        public int LocalPort { get; set; }
        public string TargetPeerID { get; set; }
        public int TargetPort { get; set; }
        public string Protocol { get; set; } = "TCP";
    }

    // ========== 组配置 ==========
    public class GroupConfig
    {
        public string GroupID { get; set; }
        public string GroupKey { get; set; }
        public string Description { get; set; }
    }

    // ========== 日志配置 ==========
    public class LoggingConfig
    {
        public string Level { get; set; } = "INFO";
        public bool LogToFile { get; set; } = true;
        public string LogFilePath { get; set; } = "logs/p2p_{date}.log";
    }

    // ========== 高级配置 ==========
    public class AdvancedConfig
    {
        public int HeartbeatInterval { get; set; } = 1000;
        public int PunchRetryCount { get; set; } = 10;
        public bool EnableP2P { get; set; } = true;
        public bool EnableRelay { get; set; } = true;
    }

    public class ServerAdvancedConfig
    {
        public int ClientTimeout { get; set; } = 30;
        public int CleanupInterval { get; set; } = 10;
        public bool EnablePortForward { get; set; } = true;
    }

    // ========== 日志系统 ==========
    public enum LogLevel
    {
        DEBUG,
        INFO,
        WARN,
        ERROR
    }

    public enum ConnectionType
    {
        P2P_DIRECT,      // P2P 直连
        SERVER_RELAY,    // 服务器中转
        PORT_FORWARD     // 端口转发
    }

    public class Logger
    {
        private static Logger instance;
        private LogLevel currentLevel;
        private bool logToFile;
        private string logFilePath;
        private StreamWriter logWriter;

        private Logger(LoggingConfig config)
        {
            currentLevel = Enum.Parse<LogLevel>(config.Level);
            logToFile = config.LogToFile;
            
            if (logToFile)
            {
                logFilePath = config.LogFilePath.Replace("{date}", DateTime.Now.ToString("yyyyMMdd"));
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
                
                // 使用 FileShare.ReadWrite 允许多个进程同时写入
                var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                logWriter = new StreamWriter(fileStream);
                logWriter.AutoFlush = true;
            }
        }

        public static void Initialize(LoggingConfig config)
        {
            instance = new Logger(config);
        }

        public static Logger Get()
        {
            return instance ?? (instance = new Logger(new LoggingConfig()));
        }

        private void Log(LogLevel level, string message, ConsoleColor color = ConsoleColor.White)
        {
            if (level < currentLevel) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logMsg = $"[{timestamp}] [{level}] {message}";

            // 控制台输出
            Console.ForegroundColor = color;
            Console.WriteLine(logMsg);
            Console.ResetColor();

            // 文件输出
            if (logToFile && logWriter != null)
            {
                logWriter.WriteLine(logMsg);
            }
        }

        public void Debug(string message) => Log(LogLevel.DEBUG, message, ConsoleColor.Gray);
        public void Info(string message) => Log(LogLevel.INFO, message, ConsoleColor.White);
        public void Warn(string message) => Log(LogLevel.WARN, message, ConsoleColor.Yellow);
        public void Error(string message) => Log(LogLevel.ERROR, message, ConsoleColor.Red);

        // ========== 专用日志方法 ==========
        public void LogConnection(string peerID, ConnectionType type, string details = "")
        {
            string typeStr = type switch
            {
                ConnectionType.P2P_DIRECT => "⚡ P2P直连",
                ConnectionType.SERVER_RELAY => "🔄 服务器中转",
                ConnectionType.PORT_FORWARD => "🌐 端口转发",
                _ => "未知"
            };

            Info($"[连接] {peerID} | 类型: {typeStr} | {details}");
        }

        public void LogPunch(string target, int attempt, int total, bool success = false)
        {
            if (success)
                Info($"[打洞] ✅ 成功连接到 {target}");
            else
                Debug($"[打洞] 🔨 尝试 {attempt}/{total} → {target}");
        }

        public void LogRelay(string from, string to, int bytes)
        {
            Debug($"[中转] {from} → {to} | {bytes} 字节");
        }

        public void LogPortForward(string rule, int localPort, string target, int targetPort)
        {
            Info($"[端口转发] {rule}: 本地:{localPort} → {target}:{targetPort}");
        }

        public void Close()
        {
            logWriter?.Close();
        }
    }
}
