using System.Net;
using System.Net.Sockets;
using System.Text;
using ChatSocketServer;
using ChatSocketServer.Core;
using ChatSocketServer.Data;
using ChatSocketServer.Net;

// =====================================================================
//  ChatSocketServer - server cho đồ án ứng dụng chat (môn Lập trình mạng căn bản)
//    TCP  9000  chat (TcpListener + Thread + StreamReader/StreamWriter, JSON mỗi dòng)
//    HTTP 8080  tải tệp, ảnh đại diện, trang trạng thái (HttpListener)
//    UDP  9001  tìm server trong LAN (broadcast)
//    UDP  9002  chuyển tiếp âm thanh / hình ảnh cuộc gọi
// =====================================================================

Console.OutputEncoding = Encoding.UTF8;
try { Console.Title = "ChatSocketServer"; } catch (PlatformNotSupportedException) { }

var config = ServerConfig.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
Directory.CreateDirectory(config.DataDir);

var kind = config.DatabaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? DbKind.SqlServer : DbKind.Sqlite;
var db = new Db(kind, kind == DbKind.SqlServer ? config.SqlServerConnection : config.SqliteConnection);

try
{
    DatabaseSetup.Initialize(db, config);
    Log.Success($"Cơ sở dữ liệu: {kind}");
}
catch (Exception ex)
{
    Log.Error("Không khởi tạo được cơ sở dữ liệu", ex);
    if (kind == DbKind.SqlServer)
        Log.Warn("Kiểm tra SQL Server đang chạy, chuỗi kết nối SqlServerConnection trong appsettings.json, và đã chạy database/chat_app_schema.sql.");
    return 1;
}

var ctx = new ServerContext(config, db);
var tcp = new TcpChatServer(ctx);
var http = new HttpFileServer(ctx);
var discovery = new UdpDiscovery(ctx);

try
{
    tcp.Start();
    http.Start();
    discovery.Start();
    ctx.Media.Start();
}
catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
{
    Log.Error("Cổng đã bị chương trình khác dùng (có thể server đang chạy ở cửa sổ khác). Đổi cổng trong appsettings.json", ex);
    return 1;
}
catch (Exception ex)
{
    Log.Error("Không khởi động được server", ex);
    return 1;
}

// Luồng nền: kết thúc các cuộc gọi đổ chuông quá 45 giây
new Thread(() =>
{
    while (true)
    {
        Thread.Sleep(10_000);
        try
        {
            ctx.Calls.ExpireRinging();
        }
        catch (Exception ex)
        {
            Log.Error("Kiểm tra cuộc gọi quá hạn", ex);
        }
    }
}) { IsBackground = true, Name = "Call timeout" }.Start();

// Địa chỉ IP của máy để client trong LAN kết nối (Dns.GetHostEntry - chương 3)
var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList
    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
    .Select(a => a.ToString()).ToList();
Log.Info($"Máy chủ: {Dns.GetHostName()} | IP: {(addresses.Count > 0 ? string.Join(", ", addresses) : "127.0.0.1")}");
Log.Info($"Trang trạng thái: http://localhost:{config.HttpPort}/   |   Gõ \"help\" để xem lệnh quản trị.");

// ---------------------------------------------------------------- lệnh quản trị trên console
while (true)
{
    var line = Console.ReadLine();
    if (line == null)
    {
        Thread.Sleep(Timeout.Infinite);   // chạy nền không có bàn phím
        break;
    }

    var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) continue;
    var arg = parts.Length > 1 ? parts[1] : "";

    switch (parts[0].ToLowerInvariant())
    {
        case "help":
            Console.WriteLine("  status            thống kê nhanh");
            Console.WriteLine("  sessions          danh sách kết nối TCP");
            Console.WriteLine("  kick <username>   ngắt kết nối một người");
            Console.WriteLine("  commands          các lệnh client gửi được");
            Console.WriteLine("  quit              tắt server");
            break;

        case "status":
            Console.WriteLine($"  Kết nối: {ctx.Sessions.Count} | Online: {ctx.Sessions.OnlineUserIds().Count} | " +
                              $"HTTP request: {http.RequestCount} | Gói UDP chuyển tiếp: {ctx.Media.RelayedPackets}");
            break;

        case "sessions":
            foreach (var s in ctx.Sessions.All())
                Console.WriteLine($"  {s.RemoteAddress,-22} {s.Username ?? "(chưa đăng nhập)",-15} {(s.IsSecure ? "AES" : "-"),-4} từ {s.ConnectedAt.ToLocalTime():HH:mm:ss}");
            break;

        case "kick":
            var targets = ctx.Sessions.All().Where(s => string.Equals(s.Username, arg, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var s in targets) s.Kick("Bạn đã bị quản trị viên ngắt kết nối.");
            Console.WriteLine($"  Đã ngắt {targets.Count} kết nối của {arg}.");
            break;

        case "commands":
            Console.WriteLine("  " + string.Join(", ", ctx.Handler.CommandNames));
            break;

        case "quit":
        case "exit":
            Log.Warn("Đang tắt server...");
            tcp.Stop();
            http.Stop();
            return 0;

        default:
            Console.WriteLine("  Lệnh không rõ, gõ help.");
            break;
    }
}
return 0;
