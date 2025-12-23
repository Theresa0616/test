using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace UnityDemo
{
    public class Tcp
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;

        public async Task<bool> ConnectAsync(string ip, int port)
        {
            if (_networkStream != null)
            {
                Disconnect();
            }

            _tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(4000);

            // 异步连接
            await _tcpClient.ConnectAsync(ip, port, cts.Token);

            _networkStream = _tcpClient.GetStream();

            // 开始接收数据
            _ = Task.Run(ReceiveDataAsync);

            return true;
        }

        public bool IsConnected()
        {
            return _networkStream != null;
        }

        public async Task SendMessageAsync(string message)
        {
            if (_networkStream == null || string.IsNullOrWhiteSpace(message))
                return;

            byte[] data = Encoding.UTF8.GetBytes(message);

            await _networkStream.WriteAsync(data, 0, data.Length);
        }

        public async Task ReceiveDataAsync()
        {
            byte[] buffer = new byte[1024];
            StringBuilder messageBuilder = new StringBuilder();

            while (_networkStream != null)
            {
                int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                {
                    // 连接已关闭
                    Disconnect();
                    break;
                }

                // 将读取的数据转换为字符串
                string receivedChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageBuilder.Append(receivedChunk);

                // 处理完整的消息（以分号分隔）
                ProcessCompleteMessages(messageBuilder);
            }
        }

        private void ProcessCompleteMessages(StringBuilder messageBuilder)
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
                    DataReceived?.Invoke(this, completeMessage);
                }

                // 移除已处理的消息（包括分号）
                allData = allData.Substring(semicolonIndex + 1);
            }

            // 清空StringBuilder并重新添加未处理完的数据
            messageBuilder.Clear();
            messageBuilder.Append(allData);
        }

        public void Disconnect()
        {
            _networkStream?.Close();
            _tcpClient?.Close();
            _tcpClient = null;
            _networkStream = null;
        }

        public event EventHandler<string>? DataReceived;
    }
}
