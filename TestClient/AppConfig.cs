using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class AppConfig
{
    private readonly IConfiguration _config;
    private readonly ILogger _logger;

    public AppConfig(IConfiguration config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        LogSettings();
    }

    public string Mode => _config.GetValue("Mode", "server");
    public string IP => _config.GetValue("ConnectionSetting:IP", "127.0.0.1");
    public int Port => _config.GetValue("ConnectionSetting:Port", 5000);
    public bool EnableTcpKeepAlive => _config.GetValue("ConnectionSetting:TcpKeepAlive:Enable", true);
    public int TcpKeepAliveTime => _config.GetValue("ConnectionSetting:TcpKeepAlive:Time", 5000);
    public int TcpKeepAliveInterval => _config.GetValue("ConnectionSetting:TcpKeepAlive:Interval", 5000);
    public bool EnableConnectionRetry => _config.GetValue("ConnectionSetting:ConnectionRetry:Enable", true);
    public int ConnectionRetryInterval => _config.GetValue("ConnectionSetting:ConnectionRetry:Interval", 3000);
    public bool EnableCheckConnection => _config.GetValue("ConnectionSetting:CheckConnection:Enable", true);
    public int CheckConnectionInterval => _config.GetValue("ConnectionSetting:CheckConnection:Interval", 10000);

    private void LogSettings()
    {
        _logger.LogInformation("🔧 載入組態設定：{@Config}", new
        {
            Mode,
            IP,
            Port,
            EnableTcpKeepAlive,
            TcpKeepAliveTime,
            TcpKeepAliveInterval,
            EnableConnectionRetry,
            ConnectionRetryInterval,
            EnableCheckConnection,
            CheckConnectionInterval
        });
    }
}
