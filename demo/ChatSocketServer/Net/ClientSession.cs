using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ChatSocketServer.Core;
using ChatSocketServer.Security;

namespace ChatSocketServer.Net;

/// <summary>
/// Một kết nối TCP của một client. Mỗi phiên chạy trên MỘT LUỒNG RIÊNG (chương 3B):
/// luồng này đọc từng dòng bằng StreamReader.ReadLine() (blocking) và xử lý lệnh.
/// Các luồng khác (ví dụ khi người khác gửi tin) có thể gọi SendEvent cùng lúc,
/// nên việc ghi vào StreamWriter được bảo vệ bằng lock.
/// </summary>
public sealed class ClientSession
{
    public const int MaxLineLength = 1_000_000;

    private readonly ServerContext _ctx;
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly object _writeLock = new();
    private byte[]? _aesKey;
    private volatile bool _closed;

    public Guid Id { get; } = Guid.NewGuid();
    public string RemoteAddress { get; }
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    /// <summary>Người dùng đã đăng nhập trên phiên này (null = chưa đăng nhập).</summary>
    public long? UserId { get; set; }
    public string? Username { get; set; }
    /// <summary>SHA-256 của token đăng nhập, dùng khi đăng xuất / đổi mật khẩu.</summary>
    public string? TokenHash { get; set; }

    public bool IsSecure => _aesKey != null;
    public string Name => Username ?? RemoteAddress;
    public long Uid => UserId ?? throw new AppException("Bạn chưa đăng nhập.");

    public ClientSession(TcpClient client, ServerContext ctx)
    {
        _ctx = ctx;
        _client = client;
        _client.NoDelay = true;
        _client.ReceiveTimeout = ctx.Config.ClientTimeoutSeconds * 1000;   // không nhận được gì => IOException
        _client.SendTimeout = 15_000;

        NetworkStream stream = client.GetStream();
        _reader = new StreamReader(stream, new UTF8Encoding(false));
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
        RemoteAddress = client.Client.RemoteEndPoint?.ToString() ?? "không rõ";
    }

    public void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Client " + RemoteAddress };
        thread.Start();
    }

    private void Run()
    {
        Log.Net($"+ Kết nối mới từ {RemoteAddress} (đang có {_ctx.Sessions.Count} kết nối)");
        try
        {
            SendEvent(Events.Hello, new
            {
                serverName = _ctx.Config.ServerName,
                version = "1.0",
                publicKey = SecureChannel.PublicKeyXml,
                encryptionRequired = _ctx.Config.RequireEncryption,
                httpPort = _ctx.Config.HttpPort,
                mediaPort = _ctx.Config.MediaPort,
                serverTime = DateTime.UtcNow
            });

            while (!_closed)
            {
                var line = _reader.ReadLine();     // chờ tới khi có một dòng hoặc client đóng kết nối
                if (line == null) break;           // client đóng kết nối bình thường
                if (line.Length == 0) continue;
                LastActivity = DateTime.UtcNow;

                if (line.Length > MaxLineLength)
                {
                    SendResponse(Response.Fail("?", 0, "Gói tin quá lớn (tối đa 1 MB). Gửi tệp qua HTTP."));
                    break;
                }

                string json;
                try
                {
                    json = _aesKey != null ? SecureChannel.DecryptLine(_aesKey, line) : line;
                }
                catch (AppException ex)
                {
                    SendResponse(Response.Fail("?", 0, ex.Message));
                    continue;
                }

                Request? request = null;
                try
                {
                    request = JsonSerializer.Deserialize<Request>(json, Json.Options);
                }
                catch (JsonException)
                {
                    // xử lý bên dưới
                }

                if (request == null || string.IsNullOrWhiteSpace(request.Cmd))
                {
                    SendResponse(Response.Fail("?", 0, "Gói tin phải là JSON dạng {\"cmd\":\"...\",\"id\":1,\"data\":{...}}."));
                    continue;
                }

                _ctx.Handler.Handle(this, request);
            }
        }
        catch (IOException)
        {
            // Mất kết nối, client tắt đột ngột hoặc quá thời gian chờ (ReceiveTimeout)
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Lỗi phiên {Name}", ex);
        }
        finally
        {
            Close();
            _ctx.Sessions.Remove(this);
            Log.Net($"- {Name} ({RemoteAddress}) đã ngắt kết nối");
        }
    }

    public void SendEvent(string evt, object? data) => Send(new EventPacket(evt, data));

    public void SendResponse(Response response) => Send(response);

    private void Send(object packet)
    {
        if (_closed) return;
        var json = Json.Serialize(packet);
        try
        {
            lock (_writeLock)
            {
                _writer.WriteLine(_aesKey != null ? SecureChannel.EncryptLine(_aesKey, json) : json);
                _writer.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Close();
        }
    }

    /// <summary>
    /// Gửi câu trả lời SECURE (chưa mã hoá) rồi bật mã hoá ngay trong cùng một lock,
    /// để không có gói nào khác chen vào giữa ở dạng sai.
    /// </summary>
    public void EnableEncryption(byte[] key, Response ok)
    {
        lock (_writeLock)
        {
            _writer.WriteLine(Json.Serialize(ok));
            _writer.Flush();
            _aesKey = key;
        }
    }

    /// <summary>Báo lý do rồi đóng kết nối (đổi mật khẩu, quản trị viên đuổi, server dừng).</summary>
    public void Kick(string reason)
    {
        SendEvent(Events.Kicked, new { reason });
        Close();
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        try
        {
            _client.Close();
        }
        catch
        {
            // đã đóng
        }
    }
}
