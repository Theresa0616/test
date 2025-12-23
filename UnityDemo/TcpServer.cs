using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace UnityDemo
{
    public class TcpServer
    {
        private TcpListener? _tcpListener;
        private List<TcpClient> _connectedClients;
        private bool _isRunning;
        private CancellationTokenSource? _cancellationTokenSource;

        public TcpServer()
        {
            _connectedClients = new List<TcpClient>();
        }

        public void Start(string ip, int port)
        {
            if (_isRunning)
                return;

            _cancellationTokenSource = new CancellationTokenSource();
            IPAddress ipAddress = IPAddress.Parse(ip);
            _tcpListener = new TcpListener(ipAddress, port);

            _tcpListener.Start();
            _isRunning = true;

            // 开始接受客户端连接
            _ = Task.Run(AcceptClientsAsync);

            ServerStarted?.Invoke(this, EventArgs.Empty);
        }

        private async Task AcceptClientsAsync()
        {
            if (_tcpListener == null || _cancellationTokenSource == null)
                return;

            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // 异步接受客户端连接
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync(_cancellationTokenSource.Token);

                    // 处理新客户端连接
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch (OperationCanceledException)
                {
                    // 服务器停止时正常退出
                    break;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream? stream = null;
            try
            {
                lock (_connectedClients)
                {
                    _connectedClients.Add(client);
                }

                stream = client.GetStream();
                byte[] buffer = new byte[1024];
                StringBuilder messageBuilder = new StringBuilder();

                ClientConnected?.Invoke(this, client);

                while (client.Connected && !(_cancellationTokenSource?.Token.IsCancellationRequested ?? true))
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource?.Token ?? CancellationToken.None);

                    if (bytesRead == 0)
                    {
                        // 客户端断开连接
                        break;
                    }

                    // 将读取的数据转换为字符串
                    string receivedChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuilder.Append(receivedChunk);

                    // 处理完整的消息（以分号分隔）
                    ProcessCompleteMessages(messageBuilder, client);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
            finally
            {
                // 清理客户端连接
                lock (_connectedClients)
                {
                    _connectedClients.Remove(client);
                }

                stream?.Close();
                client.Close();

                ClientDisconnected?.Invoke(this, client);
            }
        }

        private void ProcessCompleteMessages(StringBuilder messageBuilder, TcpClient client)
        {
            string allData = messageBuilder.ToString();
            int semicolonIndex;

            // 循环处理所有以分号结尾的完整消息
            while ((semicolonIndex = allData.IndexOf(';')) >= 0)
            {
                // 提取完整消息（不包括分号）
                string completeMessage = allData.Substring(0, semicolonIndex);

                // 触发消息接收事件
                if (!string.IsNullOrEmpty(completeMessage))
                {
                    DataReceived?.Invoke(this, (client, completeMessage));
                }

                // 移除已处理的消息（包括分号）
                allData = allData.Substring(semicolonIndex + 1);
            }

            // 清空StringBuilder并重新添加未处理完的数据
            messageBuilder.Clear();
            messageBuilder.Append(allData);
        }

        public async Task SendToClientAsync(TcpClient client, string message)
        {
            if (string.IsNullOrWhiteSpace(message) || !client.Connected)
                return;

            try
            {
                NetworkStream stream = client.GetStream();
                byte[] data = Encoding.UTF8.GetBytes(message + ";"); // 添加分号作为消息结束符
                await stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        public async Task BroadcastAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            List<TcpClient> clients;
            lock (_connectedClients)
            {
                clients = new List<TcpClient>(_connectedClients);
            }

            var tasks = new List<Task>();
            foreach (var client in clients)
            {
                if (client.Connected)
                {
                    tasks.Add(SendToClientAsync(client, message));
                }
            }

            await Task.WhenAll(tasks);
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _cancellationTokenSource?.Cancel();

            lock (_connectedClients)
            {
                foreach (var client in _connectedClients)
                {
                    client.Close();
                }
                _connectedClients.Clear();
            }

            _tcpListener?.Stop();
            _isRunning = false;

            ServerStopped?.Invoke(this, EventArgs.Empty);
        }

        public bool IsRunning => _isRunning;

        public int ConnectedClientsCount
        {
            get
            {
                lock (_connectedClients)
                {
                    return _connectedClients.Count;
                }
            }
        }

        // 事件定义
        public event EventHandler<EventArgs>? ServerStarted;
        public event EventHandler<EventArgs>? ServerStopped;
        public event EventHandler<TcpClient>? ClientConnected;
        public event EventHandler<TcpClient>? ClientDisconnected;
        public event EventHandler<(TcpClient Client, string Message)>? DataReceived;
        public event EventHandler<Exception>? ErrorOccurred;
    }
}