/*
 * TCP 端口转发模块
 * 支持本地端口 → P2P/中转 → 远程端口
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using P2PConfig;

namespace P2PPuncher
{
    public class PortForwarder
    {
        private UdpPuncher puncher;
        private Logger logger;
        private Dictionary<int, TcpListener> listeners = new Dictionary<int, TcpListener>();
        private Dictionary<Guid, TaskCompletionSource<byte[]>> responseWaiters = new Dictionary<Guid, TaskCompletionSource<byte[]>>();
        private Dictionary<string, System.Collections.Concurrent.BlockingCollection<byte[]>> connectionQueues = new Dictionary<string, System.Collections.Concurrent.BlockingCollection<byte[]>>();
        private CancellationTokenSource cts = new CancellationTokenSource();

        public PortForwarder(UdpPuncher puncher, Logger logger)
        {
            this.puncher = puncher;
            this.logger = logger;
            
            // 订阅P2P响应事件
            puncher.OnForwardResponse += HandleForwardResponse;
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

        // ========== 处理响应（基于ConnectionID）==========
        private void HandleForwardResponse(string message)
        {
            try
            {
                // 格式: FORWARD_RESPONSE:ConnectionID:Base64Data
                var parts = message.Split(new[] { ':' }, 3);
                if (parts.Length < 3) return;
                
                string connectionId = parts[1];
                byte[] responseData = Convert.FromBase64String(parts[2]);
                
                lock (responseWaiters)
                {
                    if (connectionQueues.TryGetValue(connectionId, out var queue))
                    {
                        queue.Add(responseData);
                        logger.Debug($"✅ 收到响应: {responseData.Length} 字节 (连接{connectionId})");
                    }
                    else
                    {
                        logger.Warn($"⚠️ 收到未知连接的响应: {connectionId}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"❌ 处理响应失败: {ex.Message}");
            }
        }
        
        // ========== 处理单个转发连接（异步双向模式）==========
        private async Task HandleForwardConnectionAsync(TcpClient client, PortForwardRule rule)
        {
            // 为这个TCP连接生成唯一ID
            var connectionId = Guid.NewGuid().ToString();
            var stream = client.GetStream();
            
            logger.Debug($"[端口转发] {rule.Name} 开始转发数据 (连接ID: {connectionId})");
            
            // 创建响应队列
            var responseQueue = new System.Collections.Concurrent.BlockingCollection<byte[]>();
            
            // 注册响应回调（基于ConnectionID）
            Action<byte[]> responseHandler = (data) =>
            {
                responseQueue.Add(data);
            };
            
            lock (responseWaiters)
            {
                // 注册响应队列
                connectionQueues[connectionId] = responseQueue;
            }
            
            try
            {
                // 任务1：读取本地TCP数据并发送
                var sendTask = Task.Run(async () =>
                {
                    try
                    {
                        byte[] buffer = new byte[8192];
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            // 生成请求ID（用于日志追踪）
                            var requestId = Guid.NewGuid();
                            
                            // 通过 P2P/中转 发送数据（带上ConnectionID）
                            string data = Convert.ToBase64String(buffer, 0, bytesRead);
                            string message = $"FORWARD:{connectionId}:{requestId}:{rule.TargetPort}:{data}";

                            await puncher.SendDataToTargetAsync(rule.TargetPeerID, message);
                            logger.Debug($"[端口转发] 已发送 {bytesRead} 字节 (连接{connectionId})");
                        }
                        
                        logger.Debug($"[端口转发] 本地TCP连接读取结束 (连接{connectionId})");
                    }
                    catch (Exception ex)
                    {
                        logger.Debug($"[端口转发] 发送任务异常: {ex.Message}");
                    }
                    finally
                    {
                        responseQueue.CompleteAdding();
                    }
                });
                
                // 任务2：接收响应并写回本地TCP
                var receiveTask = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var responseData in responseQueue.GetConsumingEnumerable())
                        {
                            await stream.WriteAsync(responseData, 0, responseData.Length);
                            logger.Debug($"✅ 收到响应: {responseData.Length} 字节 (连接{connectionId})");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug($"[端口转发] 接收任务异常: {ex.Message}");
                    }
                });
                
                // 等待任一任务完成（意味着连接关闭）
                await Task.WhenAny(sendTask, receiveTask);
            }
            catch (Exception ex)
            {
                logger.Warn($"[端口转发] 连接关闭: {ex.Message}");
            }
            finally
            {
                lock (responseWaiters)
                {
                    connectionQueues.Remove(connectionId);
                }
                responseQueue.Dispose();
                client?.Close();
                logger.Debug($"[端口转发] {rule.Name} TCP连接已关闭 (连接ID: {connectionId})");
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
