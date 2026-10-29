using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChatClientDemo;

/// <summary>Lỗi server trả về (ok = false).</summary>
public class ChatException : Exception
{
    public ChatException(string message) : base(message) { }
}

/// <summary>
/// Lớp kết nối tới ChatSocketServer, có thể chép nguyên sang project WinForms/WPF của nhóm.
///   - Connect():   TcpClient.Connect, đọc gói HELLO, (tuỳ chọn) trao đổi khoá RSA/AES
///   - Request():   gửi lệnh và chờ câu trả lời có cùng "id"
///   - EventReceived: sự kiện server đẩy xuống (tin nhắn mới, ai đó online, ...)
/// Một luồng nền đọc liên tục bằng StreamReader.ReadLine().
/// Lưu ý WinForms: EventReceived chạy trên luồng nền, cập nhật giao diện phải qua Control.Invoke.
/// </summary>
public sealed class ChatConnection : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly object _writeLock = new();
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private byte[]? _aesKey;
    private byte[]? _pendingKey;
    private long _pendingKeyRequestId;
    private long _nextId;

    public string Host { get; private set; } = "";
    public int HttpPort { get; private set; } = 8080;
    public int MediaPort { get; private set; } = 9002;
    public string ServerName { get; private set; } = "";
    public bool IsSecure => _aesKey != null;
    public bool IsConnected => _tcp?.Connected == true;
    public string? Token { get; set; }

    /// <summary>(tên sự kiện, data). Chạy trên luồng nền.</summary>
    public event Action<string, JsonElement>? EventReceived;
    /// <summary>Mất kết nối (lý do).</summary>
    public event Action<string>? Disconnected;

    public void Connect(string host, int port, bool secure)
    {
        Host = host;
        _tcp = new TcpClient();
        _tcp.Connect(host, port);
        _tcp.NoDelay = true;
        var stream = _tcp.GetStream();
        _reader = new StreamReader(stream, new UTF8Encoding(false));
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };

        // Gói đầu tiên luôn là HELLO (chưa mã hoá)
        var helloLine = _reader.ReadLine() ?? throw new IOException("Server đóng kết nối.");
        using var doc = JsonDocument.Parse(helloLine);
        var hello = doc.RootElement.GetProperty("data");
        ServerName = hello.GetProperty("serverName").GetString() ?? "";
        HttpPort = hello.GetProperty("httpPort").GetInt32();
        MediaPort = hello.GetProperty("mediaPort").GetInt32();
        var publicKeyXml = hello.GetProperty("publicKey").GetString()!;
        var required = hello.GetProperty("encryptionRequired").GetBoolean();

        new Thread(ReadLoop) { IsBackground = true, Name = "Chat reader" }.Start();

        if (secure || required) Secure(publicKeyXml);
    }

    /// <summary>Tạo khoá AES-256, mã hoá bằng khoá công khai RSA của server rồi gửi lệnh SECURE.</summary>
    private void Secure(string publicKeyXml)
    {
        using var rsa = RSA.Create();
        rsa.FromXmlString(publicKeyXml);
        var key = RandomNumberGenerator.GetBytes(32);
        var encryptedKey = rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA1);

        _pendingKey = key;
        _pendingKeyRequestId = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[_pendingKeyRequestId] = tcs;
        WriteLine(Serialize(new { cmd = "SECURE", id = _pendingKeyRequestId, data = new { key = Convert.ToBase64String(encryptedKey) } }));
        Wait(tcs, _pendingKeyRequestId, 10_000);
    }

    public JsonElement Request(string cmd, object? data = null, int timeoutMs = 15_000)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        WriteLine(Serialize(new { cmd, id, data }));
        return Wait(tcs, id, timeoutMs);
    }

    public Task<JsonElement> RequestAsync(string cmd, object? data = null, int timeoutMs = 15_000) =>
        Task.Run(() => Request(cmd, data, timeoutMs));

    private JsonElement Wait(TaskCompletionSource<JsonElement> tcs, long id, int timeoutMs)
    {
        if (!tcs.Task.Wait(timeoutMs))
        {
            _pending.TryRemove(id, out _);
            throw new TimeoutException("Server không trả lời.");
        }
        return tcs.Task.GetAwaiter().GetResult();
    }

    private void ReadLoop()
    {
        var reason = "Server đóng kết nối.";
        try
        {
            while (true)
            {
                var line = _reader!.ReadLine();
                if (line == null) break;
                if (_aesKey != null) line = DecryptLine(_aesKey, line);

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;

                if (root.TryGetProperty("event", out var evt))
                {
                    EventReceived?.Invoke(evt.GetString()!, data);
                    continue;
                }

                var id = root.GetProperty("id").GetInt64();
                var ok = root.GetProperty("ok").GetBoolean();
                // Câu trả lời SECURE thành công: bật mã hoá TRƯỚC khi đọc dòng tiếp theo
                if (ok && id == _pendingKeyRequestId && _pendingKey != null)
                {
                    _aesKey = _pendingKey;
                    _pendingKey = null;
                }
                if (_pending.TryRemove(id, out var tcs))
                {
                    if (ok) tcs.TrySetResult(data);
                    else tcs.TrySetException(new ChatException(root.GetProperty("error").GetString() ?? "Lỗi"));
                }
            }
        }
        catch (Exception ex)
        {
            reason = ex.Message;
        }

        foreach (var pair in _pending) pair.Value.TrySetException(new IOException("Mất kết nối."));
        _pending.Clear();
        Disconnected?.Invoke(reason);
    }

    private void WriteLine(string json)
    {
        lock (_writeLock)
        {
            _writer!.WriteLine(_aesKey != null ? EncryptLine(_aesKey, json) : json);
            _writer.Flush();
        }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    // Base64( IV 16 byte + AES-CBC(JSON) )
    private static string EncryptLine(byte[] key, string text)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(text), aes.IV);
        return Convert.ToBase64String(aes.IV.Concat(cipher).ToArray());
    }

    private static string DecryptLine(byte[] key, string line)
    {
        var packet = Convert.FromBase64String(line);
        using var aes = Aes.Create();
        aes.Key = key;
        return Encoding.UTF8.GetString(aes.DecryptCbc(packet[16..], packet[..16]));
    }

    // ------------------------------------------------------------------ HTTP (chương 4: WebClient)

    /// <summary>Gửi tệp: WebClient.UploadData, thân request là nội dung tệp.</summary>
    public string UploadFile(long conversationId, string filePath, string? caption = null)
    {
        using var web = new WebClient();
        web.Headers[HttpRequestHeader.Authorization] = "Bearer " + Token;
        var url = $"http://{Host}:{HttpPort}/upload?conversationId={conversationId}&clientMessageId={Guid.NewGuid()}" +
                  $"&fileName={Uri.EscapeDataString(Path.GetFileName(filePath))}" +
                  (caption == null ? "" : $"&caption={Uri.EscapeDataString(caption)}");
        var result = web.UploadData(url, "POST", File.ReadAllBytes(filePath));
        return Encoding.UTF8.GetString(result);
    }

    /// <summary>Tải tệp đính kèm: WebClient.DownloadFile.</summary>
    public void DownloadFile(long attachmentId, string savePath)
    {
        using var web = new WebClient();
        web.Headers[HttpRequestHeader.Authorization] = "Bearer " + Token;
        web.DownloadFile($"http://{Host}:{HttpPort}/files/{attachmentId}", savePath);
    }

    /// <summary>Đổi ảnh đại diện.</summary>
    public string UploadAvatar(string imagePath)
    {
        using var web = new WebClient();
        web.Headers[HttpRequestHeader.Authorization] = "Bearer " + Token;
        var url = $"http://{Host}:{HttpPort}/avatar?fileName={Uri.EscapeDataString(Path.GetFileName(imagePath))}";
        return Encoding.UTF8.GetString(web.UploadData(url, "POST", File.ReadAllBytes(imagePath)));
    }

    // ------------------------------------------------------------------ UDP (chương 3: UdpClient)

    /// <summary>Tìm server trong LAN: broadcast "CHAT_DISCOVER" và chờ trả lời.</summary>
    public static (string Host, int TcpPort, string Name)? Discover(int discoveryPort = 9001, int timeoutMs = 1500)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.ReceiveTimeout = timeoutMs;
        var request = Encoding.UTF8.GetBytes("CHAT_DISCOVER");
        udp.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort));
        try
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            var reply = udp.Receive(ref remote);
            using var doc = JsonDocument.Parse(reply);
            return (remote.Address.ToString(), doc.RootElement.GetProperty("tcpPort").GetInt32(),
                doc.RootElement.GetProperty("serverName").GetString() ?? "");
        }
        catch (SocketException)
        {
            return null;   // hết thời gian chờ, không có server nào trả lời
        }
    }

    public void Dispose()
    {
        try
        {
            _tcp?.Close();
        }
        catch
        {
            // bỏ qua
        }
    }
}

/// <summary>
/// Gửi/nhận âm thanh, hình ảnh cuộc gọi qua UDP (định dạng gói xem PROTOCOL.md).
/// Ứng dụng thật sẽ thu micro bằng NAudio (WaveInEvent) rồi gọi SendAudio với từng khối PCM,
/// và phát dữ liệu nhận được bằng WaveOutEvent + BufferedWaveProvider.
/// </summary>
public sealed class MediaChannel : IDisposable
{
    public const byte KindJoin = 1, KindAudio = 2, KindVideo = 3, KindMusic = 4;

    private readonly UdpClient _udp = new(0);   // cổng ngẫu nhiên
    private readonly IPEndPoint _server;
    private readonly long _callId;
    private readonly byte[] _token;
    private volatile bool _running = true;

    /// <summary>(loại gói, userId người gửi, dữ liệu)</summary>
    public event Action<byte, long, byte[]>? Received;

    public MediaChannel(string host, int port, long callId, string mediaTokenBase64)
    {
        _server = new IPEndPoint(Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork), port);
        _callId = callId;
        _token = Convert.FromBase64String(mediaTokenBase64);
        new Thread(ReceiveLoop) { IsBackground = true, Name = "UDP media" }.Start();
        new Thread(KeepAlive) { IsBackground = true, Name = "UDP keepalive" }.Start();
    }

    public void SendAudio(byte[] pcm) => Send(KindAudio, pcm);
    public void SendVideoFrame(byte[] jpeg) => Send(KindVideo, jpeg);
    public void SendMusic(byte[] pcm) => Send(KindMusic, pcm);

    public void Send(byte kind, byte[] payload)
    {
        var packet = new byte[25 + payload.Length];
        packet[0] = kind;
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(_callId)).CopyTo(packet, 1);
        _token.CopyTo(packet, 9);
        payload.CopyTo(packet, 25);
        _udp.Send(packet, packet.Length, _server);
    }

    /// <summary>Gói JOIN mỗi 5 giây để server biết (và nhớ) địa chỉ UDP của mình.</summary>
    private void KeepAlive()
    {
        while (_running)
        {
            try
            {
                Send(KindJoin, Array.Empty<byte>());
            }
            catch
            {
                // bỏ qua
            }
            Thread.Sleep(5000);
        }
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var data = _udp.Receive(ref remote);
                if (data.Length < 17) continue;
                var sender = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(data, 9));
                Received?.Invoke(data[0], sender, data[17..]);
            }
            catch (SocketException)
            {
                // Windows: ICMP port unreachable; bỏ qua
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _udp.Close();
    }
}
