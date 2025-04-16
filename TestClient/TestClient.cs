using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

public class TestClient : IDisposable
{
    private readonly ILogger<TestClient> _logger;
    private readonly string _ip;
    private readonly int _port;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private TaskCompletionSource<bool>? _reconnectingTask;
    private readonly object _reconnectLock = new();

    public string Name { get; set; }
    public bool EnableConnectionRetry { get; set; } = true;
    public bool EnableCheckConnection { get; set; } = true;
    public bool EnableTcpKeepAlive { get; set; } = true;
    public int ConnectionRetryInterval { get; set; } = 3000;
    public int CheckConnectionInterval { get; set; } = 10000;
    public int TCPKeepAliveTime { get; set; } = 5000;
    public int TCPKeepAliveInterval { get; set; } = 1000;

    public TestClient(string ip, int port, ILogger<TestClient> logger)
    {
        _ip = ip;
        _port = port;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        await EnsureConnectedAsync();

        if (EnableCheckConnection)
        {
            _ = Task.Run(() => MonitorConnectionAsync(_cts.Token));
        }

        await ReceiveDataAsync(_cts.Token);

        _cts.Cancel();
        _client?.Close();
        _logger.LogInformation("Client terminated. Name: {0}, Port: {1}", Name, _port);
    }

    private async Task EnsureConnectedAsync()
    {
        if (IsReconnecting())
        {
            _logger.LogInformation("等待其他任務完成重連...");
            await _reconnectingTask!.Task;
            return;
        }

        try
        {
            await _connectionLock.WaitAsync();
            _logger.LogInformation("[{0}]嘗試連線至伺服器...", Name);

            while (EnableConnectionRetry)
            {
                if (await TryConnectAsync())
                {
                    _reconnectingTask?.SetResult(true);
                    break;
                }

                _logger.LogWarning("連線失敗，{RetryInterval} 毫秒後重試...", ConnectionRetryInterval);
                await Task.Delay(ConnectionRetryInterval);
            }
        }
        finally
        {
            _connectionLock.Release();
            ResetReconnectingTask();
        }
    }

    private async Task<bool> TryConnectAsync()
    {
        try
        {
            _client?.Close();
            _client = new TcpClient();
            await _client.ConnectAsync(_ip, _port);

            if (EnableTcpKeepAlive)
            {
                ConfigureKeepAlive();
            }

            _stream = _client.GetStream();
            _logger.LogInformation("成功連線至伺服器: {0}:{1}", _ip, _port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "連線失敗");
            return false;
        }
    }

    private void ConfigureKeepAlive()
    {
        if (_client?.Client == null) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var keepAlive = new byte[12];
            BitConverter.GetBytes((uint)1).CopyTo(keepAlive, 0);
            BitConverter.GetBytes((uint)TCPKeepAliveTime).CopyTo(keepAlive, 4);
            BitConverter.GetBytes((uint)TCPKeepAliveInterval).CopyTo(keepAlive, 8);

            _client.Client.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);
        }
        else
        {
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }

        _logger.LogDebug("TCP Keep-Alive 設定完成: Time={0}, Interval={1}", TCPKeepAliveTime, TCPKeepAliveInterval);
    }

    private async Task MonitorConnectionAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!IsConnectionHealthy())
            {
                _logger.LogWarning("連線檢查異常，嘗試重新連線...");
                await EnsureConnectedAsync();
            }

            await Task.Delay(CheckConnectionInterval, token);
        }
    }

    private bool IsConnectionHealthy()
    {
        try
        {
            var socket = _client?.Client;
            if (socket == null || !socket.Connected) return false;

            return !(socket.Poll(0, SelectMode.SelectRead) && _client.Available == 0);
        }
        catch
        {
            return false;
        }
    }

    private async Task ReceiveDataAsync(CancellationToken token)
    {
        byte[] buffer = new byte[1024];

        while (!token.IsCancellationRequested)
        {
            try
            {
                int bytesRead = await _stream!.ReadAsync(buffer, token);
                if (bytesRead == 0)
                {
                    _logger.LogWarning("伺服器中斷連線，嘗試重新連線...");
                    await EnsureConnectedAsync();
                    continue;
                }

                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _logger.LogInformation("接收到伺服器資料：{0}", response);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("接收資料操作已取消");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "接收資料發生錯誤，嘗試重新連線...");
                await EnsureConnectedAsync();
            }
        }
    }

    private bool IsReconnecting()
    {
        lock (_reconnectLock)
        {
            if (_reconnectingTask == null)
            {
                _reconnectingTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return false;
            }

            return true;
        }
    }

    private void ResetReconnectingTask()
    {
        lock (_reconnectLock)
        {
            _reconnectingTask = null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _client?.Close();
        _stream?.Dispose();
        _cts.Dispose();
        _logger.LogInformation("已釋放資源");
        GC.SuppressFinalize(this);
    }
}
