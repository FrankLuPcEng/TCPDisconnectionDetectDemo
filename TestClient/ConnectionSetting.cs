public class ConnectionSetting
{
    public string Name { get; set; } = "TestClient-" + DateTime.Now.ToShortTimeString();
    public string IP { get; set; }
    public int Port { get; set; }
    public ConnectionRetrySetting ConnectionRetry { get; set; }
    public CheckConnectionSetting CheckConnection { get; set; }
    public TcpKeepAliveSetting TcpKeepAlive { get; set; }
}

public class ConnectionRetrySetting
{
    public bool Enable { get; set; }
    public int Interval { get; set; }
}

public class CheckConnectionSetting
{
    public bool Enable { get; set; }
    public int Interval { get; set; }
}

public class TcpKeepAliveSetting
{
    public bool Enable { get; set; }
    public int Interval { get; set; }
    public int Time { get; set; }
}
