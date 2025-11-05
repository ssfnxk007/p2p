/*
 * TCP 端口转发模块
 * 支持本地端口 → P2P/中转 → 远程端口
 */

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using P2PConfig;

namespace P2PPuncher
{
    public class PortForwarder
    {
        private UdpPuncher puncher;
        private Logger logger;
        private Dictionary<int, TcpListener> listeners = new Dictionary<int, TcpListener>();
        private CancellationTokenSource cts = new CancellationTokenSource();

        public PortForwarder(UdpPuncher puncher, Logger logger)
        {
            this.puncher = puncher;
            this.logger = logger;
        }

        // ========== 启动端口转发 ==========
        public Task StartForwardAsync(PortForwardRule rule)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, rule.LocalPort);
                listener.Start();
                listeners[rule.LocalPort] = listener;

                logger.LogPortForward(rule.Name, rule.LocalPort, rule.TargetPeerID, rule.TargetPort);
                logger.Info($"[端口转发] {rule.Name} 已启动，监听端口 {rule.LocalPort}");

                // 异步接受连接
                _ = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var client = await listener.AcceptTcpClientAsync();
                            logger.Debug($"[端口转发] {rule.Name} 接受新连接");

                            // 处理连接
                            _ = HandleForwardConnectionAsync(client, rule);
                        }
                        catch (Exception ex)
                        {
                            if (!cts.Token.IsCancellationRequested)
                                logger.Error($"[端口转发] 接受连接失败: {ex.Message}");
                        }
                    }
                }, cts.Token);
            }
            catch (Exception ex)
            {
                logger.Error($"[端口转发] 启动失败: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        // ========== 处理单个转发连接 ==========
        private async Task HandleForwardConnectionAsync(TcpClient client, PortForwardRule rule)
        {
            try
            {
                var stream = client.GetStream();
                logger.Debug($"[端口转发] {rule.Name} 开始转发数据");

                // TCP over UDP 转发
                byte[] buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    // 通过 P2P/中转 发送数据
                    string data = Convert.ToBase64String(buffer, 0, bytesRead);
                    string message = $"FORWARD:{rule.TargetPort}:{data}";

                    await puncher.SendDataToTargetAsync(rule.TargetPeerID, message);
                    logger.Debug($"[端口转发] {rule.Name} 发送 {bytesRead} 字节");
                }
            }
            catch (Exception ex)
            {
                logger.Warn($"[端口转发] 连接关闭: {ex.Message}");
            }
            finally
            {
                client?.Close();
            }
        }

        // ========== 停止所有转发 ==========
        public void StopAll()
        {
            cts.Cancel();
            
            foreach (var listener in listeners.Values)
            {
                listener.Stop();
            }
            
            listeners.Clear();
            logger.Info("[端口转发] 已停止所有转发");
        }
    }
}
