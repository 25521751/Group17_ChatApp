using System.Net;
using System.Text;
using ChatSocketServer.Core;
using ChatSocketServer.Data;
using ChatSocketServer.Services;

namespace ChatSocketServer.Net;

/// <summary>
/// Web server viết bằng HttpListener (chương 4). Dùng cho những việc TCP chat không hợp:
///   GET  /                         trang trạng thái server (HTML)
///   GET  /api/status               trạng thái dạng JSON
///   POST /upload?conversationId=..&clientMessageId=..&fileName=..[&caption=..&replyToId=..]
///                                  thân request = nội dung tệp (WebClient.UploadData / HttpClient)
///   GET  /files/{attachmentId}     tải tệp đính kèm (WebClient.DownloadFile / DownloadData)
///   POST /avatar?fileName=..       đổi ảnh đại diện
///   POST /group-avatar?conversationId=..&fileName=..   đổi ảnh nhóm
///   GET  /avatars/{tên}, /stickers/{tên}               ảnh công khai
/// Xác thực: header "Authorization: Bearer TOKEN" hoặc tham số ?token=TOKEN (token nhận khi LOGIN qua TCP).
/// </summary>
public sealed class HttpFileServer
{
    private sealed class HttpError : Exception
    {
        public int Status { get; }
        public HttpError(int status, string message) : base(message) => Status = status;
    }

    private readonly ServerContext _ctx;
    private readonly string _wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    private HttpListener? _listener;
    private long _requestCount;

    public HttpFileServer(ServerContext ctx) => _ctx = ctx;

    public string Prefix { get; private set; } = "";
    public long RequestCount => Interlocked.Read(ref _requestCount);

    public void Start()
    {
        // "http://+:8080/" nhận kết nối từ mọi máy, nhưng Windows yêu cầu quyền admin hoặc lệnh:
        //   netsh http add urlacl url=http://+:8080/ user=Everyone
        // Nếu không được thì lùi về chỉ nhận từ máy này.
        foreach (var prefix in new[] { $"http://+:{_ctx.Config.HttpPort}/", $"http://localhost:{_ctx.Config.HttpPort}/" })
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                Prefix = prefix;
                break;
            }
            catch (HttpListenerException ex)
            {
                Log.Warn($"Không mở được {prefix} ({ex.Message}).");
                _listener = null;
            }
        }
        if (_listener == null) throw new InvalidOperationException($"Không mở được HTTP server ở cổng {_ctx.Config.HttpPort}.");
        if (Prefix.Contains("localhost"))
            Log.Warn($"HTTP chỉ nhận từ máy này. Để máy khác tải tệp được, chạy (quyền admin): netsh http add urlacl url=http://+:{_ctx.Config.HttpPort}/ user=Everyone");

        new Thread(Loop) { IsBackground = true, Name = "HTTP listener" }.Start();
        Log.Success($"HTTP server (HttpListener) đang chạy: {Prefix}");
    }

    private void Loop()
    {
        while (_listener!.IsListening)
        {
            try
            {
                var context = _listener.GetContext();                       // chờ một HTTP request
                ThreadPool.QueueUserWorkItem(_ =>                           // xử lý song song trên ThreadPool
                {
                    try
                    {
                        Handle(context);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("HTTP", ex);
                    }
                });
            }
            catch (HttpListenerException)
            {
                return;   // listener đã dừng
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        Interlocked.Increment(ref _requestCount);
        response.Headers["Server"] = "ChatSocketServer/1.0 (HttpListener)";
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";

        var path = request.Url?.AbsolutePath ?? "/";
        var method = request.HttpMethod.ToUpperInvariant();
        try
        {
            if (method == "OPTIONS")
            {
                response.StatusCode = 204;
                return;
            }

            if (method == "GET" && path == "/") WriteHtml(response, StatusPage(request));
            else if (method == "GET" && path == "/api/status") WriteJson(response, 200, Status());
            else if (method == "GET" && path.StartsWith("/avatars/")) ServePublic(response, _ctx.Files.AvatarFullPath(path["/avatars/".Length..]));
            else if (method == "GET" && path.StartsWith("/stickers/")) ServePublic(response, FileStore.SafeCombine(Path.Combine(_wwwroot, "stickers"), path["/stickers/".Length..]));
            else if (method == "GET" && path.StartsWith("/files/")) DownloadAttachment(request, response, path["/files/".Length..]);
            else if (method == "POST" && path == "/upload") Upload(request, response);
            else if (method == "POST" && path == "/avatar") UploadAvatar(request, response);
            else if (method == "POST" && path == "/group-avatar") UploadGroupAvatar(request, response);
            else throw new HttpError(404, $"Không có đường dẫn {method} {path}");

            if (path != "/" && path != "/api/status") Log.Info($"HTTP {method} {path} → {response.StatusCode} ({request.RemoteEndPoint})");
        }
        catch (HttpError ex)
        {
            TryWriteError(response, ex.Status, ex.Message);
            Log.Warn($"HTTP {method} {path} → {ex.Status}: {ex.Message}");
        }
        catch (AppException ex)
        {
            TryWriteError(response, 400, ex.Message);
            Log.Warn($"HTTP {method} {path} → 400: {ex.Message}");
        }
        catch (Exception ex)
        {
            TryWriteError(response, 500, "Lỗi máy chủ.");
            Log.Error($"HTTP {method} {path}", ex);
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch
            {
                // client đã ngắt
            }
        }
    }

    // ------------------------------------------------------------------ tệp đính kèm

    private void Upload(HttpListenerRequest request, HttpListenerResponse response)
    {
        var me = Authenticate(request);
        var q = request.QueryString;
        var conversationId = RequiredLong(q["conversationId"], "conversationId");
        var clientMessageId = q["clientMessageId"] ?? throw new HttpError(400, "Thiếu clientMessageId.");
        var fileName = Path.GetFileName(q["fileName"] ?? "");
        if (fileName.Length == 0) throw new HttpError(400, "Thiếu fileName.");
        long? replyToId = long.TryParse(q["replyToId"], out var r) ? r : null;

        _ctx.Conversations.RequireMember(conversationId, me);
        MessageService.ValidateClientMessageId(clientMessageId);

        // Gửi lại cùng clientMessageId (mất mạng giữa chừng) => trả về tin đã lưu, không lưu tệp lần nữa
        var duplicate = _ctx.Messages.FindByClientId(me, clientMessageId);
        if (duplicate != null)
        {
            WriteJson(response, 200, duplicate);
            return;
        }

        var data = ReadBody(request, _ctx.Files.MaxFileBytes);
        var storagePath = _ctx.Files.SaveAttachment(data, fileName);
        var mime = FileStore.MimeOf(fileName);   // xác định theo phần mở rộng, không tin Content-Type client gửi
        var message = _ctx.Messages.SendAttachment(me, conversationId, clientMessageId, fileName, mime, data.Length, storagePath,
            q["caption"], replyToId);
        WriteJson(response, 200, message);
    }

    private void DownloadAttachment(HttpListenerRequest request, HttpListenerResponse response, string idText)
    {
        var me = Authenticate(request);
        var attachmentId = RequiredLong(idText, "attachmentId");
        var (path, mime, name) = _ctx.Messages.GetAttachment(me, attachmentId);

        // Ảnh: hiển thị trực tiếp; tệp khác: trình duyệt hỏi lưu
        var disposition = mime.StartsWith("image/") ? "inline" : "attachment";
        response.Headers["Content-Disposition"] = $"{disposition}; filename*=UTF-8''{Uri.EscapeDataString(name)}";
        WriteFile(response, path, mime);
    }

    // ------------------------------------------------------------------ ảnh đại diện

    private void UploadAvatar(HttpListenerRequest request, HttpListenerResponse response)
    {
        var me = Authenticate(request);
        var fileName = Path.GetFileName(request.QueryString["fileName"] ?? "avatar.png");
        var url = _ctx.Files.SaveAvatar(ReadBody(request, FileStore.MaxAvatarBytes), fileName);
        WriteJson(response, 200, _ctx.Users.SetAvatar(me, url));
    }

    private void UploadGroupAvatar(HttpListenerRequest request, HttpListenerResponse response)
    {
        var me = Authenticate(request);
        var conversationId = RequiredLong(request.QueryString["conversationId"], "conversationId");
        _ctx.Conversations.RequireGroupAdmin(conversationId, me);   // kiểm tra quyền trước khi lưu tệp
        var fileName = Path.GetFileName(request.QueryString["fileName"] ?? "group.png");
        var url = _ctx.Files.SaveAvatar(ReadBody(request, FileStore.MaxAvatarBytes), fileName);
        WriteJson(response, 200, _ctx.Conversations.SetAvatar(me, conversationId, url));
    }

    // ------------------------------------------------------------------ trạng thái

    private object Status()
    {
        var db = _ctx.Db;
        return new
        {
            serverName = _ctx.Config.ServerName,
            startedAt = _ctx.StartedAt,
            uptime = (DateTime.UtcNow - _ctx.StartedAt).ToString(@"d\.hh\:mm\:ss"),
            database = db.Kind.ToString(),
            ports = new { tcp = _ctx.Config.TcpPort, http = _ctx.Config.HttpPort, discovery = _ctx.Config.DiscoveryPort, media = _ctx.Config.MediaPort },
            connections = _ctx.Sessions.Count,
            onlineUsers = _ctx.Sessions.OnlineUserIds().Count,
            users = db.Count("SELECT COUNT(*) FROM Users WHERE IsBot = 0"),
            conversations = db.Count("SELECT COUNT(*) FROM Conversations"),
            messages = db.Count("SELECT COUNT(*) FROM Messages"),
            httpRequests = RequestCount,
            udpRelayedPackets = _ctx.Media.RelayedPackets
        };
    }

    private string StatusPage(HttpListenerRequest request)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var db = _ctx.Db;
        var sessions = _ctx.Sessions.All().OrderBy(s => s.ConnectedAt).ToList();
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"5\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>").Append(E(_ctx.Config.ServerName)).Append("</title>")
            .Append("<style>body{font:14px/1.5 Segoe UI,system-ui,sans-serif;margin:0;background:#f3f5f8;color:#1b2430}")
            .Append("main{max-width:1000px;margin:auto;padding:24px 16px;display:grid;gap:18px}h1{margin:0;font-size:22px}")
            .Append("h2{font-size:15px;margin:0 0 8px}section{background:#fff;border:1px solid #dde3ea;border-radius:8px;padding:14px 16px;overflow-x:auto}")
            .Append(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px}.k{color:#667;font-size:12px}.v{font-size:20px;font-weight:600}")
            .Append("table{border-collapse:collapse;width:100%}td,th{text-align:left;padding:5px 8px;border-bottom:1px solid #eef1f4;white-space:nowrap}")
            .Append("pre{margin:0;font:12px/1.5 Consolas,monospace;white-space:pre-wrap}.ok{color:#178a4c}.no{color:#999}</style></head><body><main>");

        html.Append("<div><h1>").Append(E(_ctx.Config.ServerName)).Append("</h1><div class=\"k\">Trang tự làm mới mỗi 5 giây · tạo bởi HttpListener</div></div>");

        html.Append("<section><div class=\"grid\">");
        void Stat(string k, object v) => html.Append("<div><div class=\"k\">").Append(E(k)).Append("</div><div class=\"v\">").Append(E(v.ToString())).Append("</div></div>");
        Stat("Thời gian chạy", (DateTime.UtcNow - _ctx.StartedAt).ToString(@"d\.hh\:mm\:ss"));
        Stat("Kết nối TCP", _ctx.Sessions.Count);
        Stat("Người online", _ctx.Sessions.OnlineUserIds().Count);
        Stat("Tài khoản", db.Count("SELECT COUNT(*) FROM Users WHERE IsBot = 0"));
        Stat("Tin nhắn", db.Count("SELECT COUNT(*) FROM Messages"));
        Stat("HTTP request", RequestCount);
        Stat("Gói UDP chuyển tiếp", _ctx.Media.RelayedPackets);
        Stat("CSDL", db.Kind);
        html.Append("</div></section>");

        html.Append("<section><h2>Cổng đang mở</h2><table><tr><th>Giao thức</th><th>Cổng</th><th>Dùng cho</th></tr>")
            .Append($"<tr><td>TCP</td><td>{_ctx.Config.TcpPort}</td><td>Chat: một dòng JSON mỗi gói tin</td></tr>")
            .Append($"<tr><td>HTTP</td><td>{_ctx.Config.HttpPort}</td><td>Tải tệp lên/xuống, ảnh đại diện, trang này</td></tr>")
            .Append($"<tr><td>UDP</td><td>{_ctx.Config.DiscoveryPort}</td><td>Tìm server trong LAN (broadcast \"CHAT_DISCOVER\")</td></tr>")
            .Append($"<tr><td>UDP</td><td>{_ctx.Config.MediaPort}</td><td>Âm thanh / hình ảnh cuộc gọi</td></tr></table></section>");

        html.Append("<section><h2>Các kết nối TCP (").Append(sessions.Count).Append(")</h2><table><tr><th>Người dùng</th><th>Địa chỉ</th><th>Kết nối lúc</th><th>Mã hoá</th></tr>");
        foreach (var s in sessions)
            html.Append("<tr><td>").Append(E(s.Username ?? "(chưa đăng nhập)")).Append("</td><td>").Append(E(s.RemoteAddress))
                .Append("</td><td>").Append(s.ConnectedAt.ToLocalTime().ToString("HH:mm:ss dd/MM"))
                .Append("</td><td>").Append(s.IsSecure ? "<span class=\"ok\">AES</span>" : "<span class=\"no\">không</span>").Append("</td></tr>");
        html.Append("</table></section>");

        // Hiển thị chính HTTP request trình duyệt vừa gửi (minh hoạ chương 4)
        html.Append("<section><h2>HTTP request bạn vừa gửi</h2><pre>")
            .Append(E($"{request.HttpMethod} {request.RawUrl} HTTP/{request.ProtocolVersion}")).Append('\n');
        foreach (string? key in request.Headers.AllKeys)
            if (key != null) html.Append(E($"{key}: {request.Headers[key]}")).Append('\n');
        html.Append("</pre></section>");

        html.Append("<section><h2>Nhật ký gần đây</h2><pre>");
        foreach (var line in Log.GetRecent().Take(60)) html.Append(E(line)).Append('\n');
        html.Append("</pre></section></main></body></html>");
        return html.ToString();
    }

    // ------------------------------------------------------------------ hỗ trợ

    private long Authenticate(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        var token = header != null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : request.QueryString["token"];
        return _ctx.Auth.ValidateToken(token) ?? throw new HttpError(401, "Thiếu hoặc sai token. Gửi header Authorization: Bearer <token>.");
    }

    private static long RequiredLong(string? value, string name) =>
        long.TryParse(value, out var n) ? n : throw new HttpError(400, $"Thiếu hoặc sai {name}.");

    /// <summary>Đọc toàn bộ thân request (tối đa maxBytes) bằng Stream.Read theo từng khối 4096 byte.</summary>
    private static byte[] ReadBody(HttpListenerRequest request, long maxBytes)
    {
        if (request.ContentLength64 > maxBytes) throw new HttpError(413, $"Tệp quá lớn (tối đa {maxBytes / 1024 / 1024} MB).");
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = request.InputStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > maxBytes) throw new HttpError(413, $"Tệp quá lớn (tối đa {maxBytes / 1024 / 1024} MB).");
        }
        if (memory.Length == 0) throw new HttpError(400, "Thân request rỗng: hãy gửi nội dung tệp.");
        return memory.ToArray();
    }

    private static void ServePublic(HttpListenerResponse response, string path)
    {
        if (!File.Exists(path)) throw new HttpError(404, "Không tìm thấy tệp.");
        response.Headers["Cache-Control"] = "public, max-age=86400";
        WriteFile(response, path, FileStore.MimeOf(path));
    }

    private static void WriteFile(HttpListenerResponse response, string path, string mime)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        response.StatusCode = 200;
        response.ContentType = mime;
        response.ContentLength64 = file.Length;
        file.CopyTo(response.OutputStream);
    }

    private static void WriteJson(HttpListenerResponse response, int status, object body)
    {
        var bytes = Encoding.UTF8.GetBytes(Json.Serialize(body));
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Ghi lỗi dạng JSON; nếu không ghi được (client đã ngắt, đã gửi một phần) thì bỏ qua.</summary>
    private static void TryWriteError(HttpListenerResponse response, int status, string error)
    {
        try
        {
            WriteJson(response, status, new { status, error });
        }
        catch
        {
            // không thể ghi thêm
        }
    }

    private static void WriteHtml(HttpListenerResponse response, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    public void Stop() => _listener?.Stop();
}
