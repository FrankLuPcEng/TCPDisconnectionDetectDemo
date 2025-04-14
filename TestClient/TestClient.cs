using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

public class TestClient : IDisposable
{
    private readonly ILogger<TestClient> _logger;
    private readonly string ip;
    private readonly int port;

    private TcpClient? client;
    private NetworkStream? stream;
    private CancellationTokenSource cts = new();
    private DateTime lastReceivedTime;

    public string Name { get; set; } = "TestClient-" + DateTime.Now.ToShortTimeString();

    public bool EnableConnectionRetry { get; set; } = true;
    public bool EnableCheckConnection { get; set; } = true;
    public bool EnableTcpKeepAlive { get; set; } = true;
    public int ConnectionRetryInterval { get; set; } = 3000;
    public int CheckConnectionInterval { get; set; } = 10000;
    public int TCPKeepAliveTime { get; set; } = 5000;
    public int TCPKeepAliveInterval { get; set; } = 1000;
    public int IdleTimeout { get; set; } = 30000; // 閒置時間設定

    public TestClient(string ip, int port, ILogger<TestClient> logger)
    {
        this.ip = ip;
        this.port = port;
        this._logger = logger;
        this.lastReceivedTime = DateTime.Now;
    }

    public async Task StartAsync()
    {
        await EnsureConnectedAsync();

        if (EnableCheckConnection)
        {
            _ = Task.Run(() => CheckConnectionAsync(cts.Token));
        }

        await ReadFromServerAsync();

        cts.Cancel();
        client?.Close();
        _logger.LogInformation("Client terminated.");
    }

    private void SetKeepAlive()
    {
        if (client?.Client != null)
        {
            var keepAlive = new byte[12];
            BitConverter.GetBytes((uint)1).CopyTo(keepAlive, 0);
            BitConverter.GetBytes((uint)TCPKeepAliveTime).CopyTo(keepAlive, 4);
            BitConverter.GetBytes((uint)TCPKeepAliveInterval).CopyTo(keepAlive, 8);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _ = client.Client.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);
                _logger.LogDebug("TCP Keep-Alive 設定完成: {@KeepAliveSettings}", new
                {
                    Enable = EnableTcpKeepAlive,
                    Time = TCPKeepAliveTime,
                    Interval = TCPKeepAliveInterval
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, TCPKeepAliveTime / 1000);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, TCPKeepAliveInterval / 1000);
            }
            else
            {
                _logger.LogWarning("不支援的作業系統，無法設定 TCP Keep-Alive");
                throw new PlatformNotSupportedException("不支援的作業系統，無法設定 TCP Keep-Alive");
            }
        }
    }

    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private TaskCompletionSource<bool>? reconnectingTask;
    private readonly object reconnectLock = new();

    private async Task EnsureConnectedAsync()
    {
        TaskCompletionSource<bool>? myTcs;

        lock (reconnectLock)
        {
            if (reconnectingTask != null)
            {
                myTcs = reconnectingTask;
            }
            else
            {
                reconnectingTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                myTcs = reconnectingTask;
            }
        }

        if (myTcs != reconnectingTask)
        {
            _logger.LogInformation("等待其他任務完成重連...");
            await myTcs.Task;
            return;
        }

        try
        {
            await _connectionLock.WaitAsync();
            _logger.LogInformation("[{0}]嘗試連線至伺服器...", Name);

            while (EnableConnectionRetry)
            {
                try
                {
                    client?.Close();
                    client = new TcpClient();
                    await client.ConnectAsync(ip, port);

                    if (EnableTcpKeepAlive)
                    {
                        SetKeepAlive();
                    }

                    stream = client.GetStream();
                    _logger.LogInformation("[{0}]成功連線至伺服器; {1}", Name, new { ((IPEndPoint)client.Client.LocalEndPoint)?.Port });

                    reconnectingTask?.SetResult(true);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[{0}]連線失敗，{RetryInterval} 毫秒後重試...", Name, new { IP = ip, Port = port, RetryInterval = ConnectionRetryInterval });
                    await Task.Delay(ConnectionRetryInterval);
                }
            }
        }
        catch (Exception ex)
        {
            reconnectingTask?.SetException(ex);
            throw;
        }
        finally
        {
            _connectionLock.Release();
            lock (reconnectLock)
            {
                reconnectingTask = null;
            }
        }
    }

    private async Task CheckConnectionAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool isHealthy = false;

            try
            {

                var socket = client?.Client;
                if (socket != null)
                {
                    var isDisconnected = !socket.Connected;
                    var isUnreadable = socket.Poll(CheckConnectionInterval * 1000, SelectMode.SelectRead) && client.Available == 0;
                    var isError = socket.Poll(CheckConnectionInterval * 1000, SelectMode.SelectError);

                    if (isDisconnected || isUnreadable || isError)
                    {
                        _logger.LogWarning("連線檢查異常：{@CheckResult}", new
                        {
                            Disconnected = isDisconnected,
                            Unreadable = isUnreadable,
                            Error = isError
                        });
                        await EnsureConnectedAsync();
                    }
                    else
                    {
                        isHealthy = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "連線檢查時發生例外");
            }

            if (isHealthy)
            {
                _logger.LogDebug("連線檢查正常");
            }

            await Task.Delay(CheckConnectionInterval, token);
        }
    }

    private async Task ReadFromServerAsync()
    {
        byte[] buffer = new byte[20];

        while (true)
        {
            try
            {
                int bytesRead = await stream!.ReadAsync(buffer);
                if (bytesRead == 0)
                {
                    _logger.LogWarning("伺服器中斷連線，嘗試重新連線...");
                    await EnsureConnectedAsync();
                    continue;
                }

                lastReceivedTime = DateTime.Now; // 更新最後接收時間
                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _logger.LogInformation("接收到伺服器資料：{ServerResponse}", response);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "接收資料發生錯誤，嘗試重新連線...");
                await EnsureConnectedAsync();
            }

            await Task.Delay(1000);
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        client?.Close();
        stream?.Dispose();
        cts.Dispose();
        _logger.LogInformation("已釋放資源");
        GC.SuppressFinalize(this);
    }
}
