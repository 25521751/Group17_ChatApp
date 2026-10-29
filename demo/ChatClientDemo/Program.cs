using System.Text;
using System.Text.Json;
using ChatClientDemo;

// =====================================================================
//  Client console để thử ChatSocketServer (không cần giao diện).
//  Chạy: dotnet run                 -> tự tìm server trong LAN bằng UDP broadcast
//        dotnet run -- 192.168.1.10 -> kết nối thẳng tới IP
//  Mở 2 cửa sổ, đăng nhập alice và bob để thử nhắn tin qua lại.
// =====================================================================

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

string host;
int port = 9000;
if (args.Length > 0)
{
    host = args[0];
}
else
{
    Console.WriteLine("Đang tìm server trong mạng LAN (UDP broadcast)...");
    var found = ChatConnection.Discover();
    if (found is { } f)
    {
        (host, port) = (f.Host, f.TcpPort);
        Console.WriteLine($"Tìm thấy \"{f.Name}\" tại {host}:{port}");
    }
    else
    {
        host = "127.0.0.1";
        Console.WriteLine("Không có server nào trả lời, thử 127.0.0.1");
    }
}

Console.Write("Mã hoá kênh truyền RSA + AES? (Y/n): ");
var secure = !string.Equals(Console.ReadLine()?.Trim(), "n", StringComparison.OrdinalIgnoreCase);

using var chat = new ChatConnection();
long myId = 0;
long? current = null;                 // hội thoại đang mở
MediaChannel? media = null;
long? activeCallId = null;
var receivedAudioPackets = 0;

chat.EventReceived += OnEvent;
chat.Disconnected += reason => Print(ConsoleColor.Red, $"Mất kết nối: {reason}");

try
{
    chat.Connect(host, port, secure);
}
catch (Exception ex)
{
    Print(ConsoleColor.Red, $"Không kết nối được {host}:{port} - {ex.Message}");
    return;
}
Print(ConsoleColor.Green, $"Đã kết nối \"{chat.ServerName}\"{(chat.IsSecure ? " (đã mã hoá AES-256)" : "")}");

// ---------------------------------------------------------------- đăng nhập
while (true)
{
    Console.Write("Tên đăng nhập [alice]: ");
    var login = Console.ReadLine()?.Trim();
    Console.Write("Mật khẩu [123456]: ");
    var password = Console.ReadLine()?.Trim();
    try
    {
        var result = chat.Request("LOGIN", new
        {
            login = string.IsNullOrEmpty(login) ? "alice" : login,
            password = string.IsNullOrEmpty(password) ? "123456" : password,
            deviceInfo = "ChatClientDemo (console)"
        });
        chat.Token = result.GetProperty("token").GetString();
        myId = result.GetProperty("user").GetProperty("userId").GetInt64();
        Print(ConsoleColor.Green, $"Xin chào {result.GetProperty("user").GetProperty("displayName").GetString()}!");
        break;
    }
    catch (ChatException ex)
    {
        Print(ConsoleColor.Red, ex.Message);
    }
}

PrintHelp();
Run(() => ListConversations());

// ---------------------------------------------------------------- vòng lệnh
while (true)
{
    var line = Console.ReadLine();
    if (line == null || line.Trim() == "/quit") break;
    if (line.Trim().Length == 0) continue;

    var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var cmd = parts[0].ToLowerInvariant();
    var arg = parts.Length > 1 ? parts[1].Trim() : "";

    Run(() =>
    {
        switch (cmd)
        {
            case "/help": PrintHelp(); break;
            case "/list": ListConversations(); break;
            case "/open": OpenConversation(long.Parse(arg)); break;
            case "/friends":
                foreach (var f in chat.Request("FRIEND_LIST").EnumerateArray())
                    Console.WriteLine($"  [{f.GetProperty("userId")}] {f.GetProperty("displayName")} {(f.GetProperty("isOnline").GetBoolean() ? "● online" : "")}");
                break;
            case "/search":
                foreach (var r in chat.Request("SEARCH_USERS", new { q = arg }).EnumerateArray())
                    Console.WriteLine($"  [{r.GetProperty("user").GetProperty("userId")}] {r.GetProperty("user").GetProperty("displayName")} ({r.GetProperty("relationship")})");
                break;
            case "/add":
                chat.Request("FRIEND_REQUEST", new { receiverId = long.Parse(arg) });
                Console.WriteLine("  Đã gửi lời mời.");
                break;
            case "/requests":
                var req = chat.Request("FRIEND_REQUESTS");
                foreach (var r in req.GetProperty("incoming").EnumerateArray())
                    Console.WriteLine($"  Lời mời #{r.GetProperty("requestId")} từ {r.GetProperty("sender").GetProperty("displayName")}  → /accept {r.GetProperty("requestId")}");
                break;
            case "/accept":
                chat.Request("FRIEND_ACCEPT", new { requestId = long.Parse(arg) });
                Console.WriteLine("  Đã chấp nhận.");
                break;
            case "/dm":
                var conv = chat.Request("CONV_DIRECT", new { userId = long.Parse(arg) });
                OpenConversation(conv.GetProperty("conversationId").GetInt64());
                break;
            case "/group":
                // /group Tên nhóm|2,3
                var g = arg.Split('|');
                var created = chat.Request("GROUP_CREATE", new { name = g[0], memberIds = g[1].Split(',').Select(long.Parse).ToArray() });
                OpenConversation(created.GetProperty("conversationId").GetInt64());
                break;
            case "/file":
                RequireConversation();
                var uploaded = chat.UploadFile(current!.Value, arg.Trim('"'));
                Console.WriteLine($"  Đã tải lên: {uploaded}");
                break;
            case "/get":
                // /get <attachmentId> <đường dẫn lưu>
                var g2 = arg.Split(' ', 2);
                chat.DownloadFile(long.Parse(g2[0]), g2[1].Trim('"'));
                Console.WriteLine($"  Đã lưu {g2[1]}");
                break;
            case "/sticker":
                RequireConversation();
                Send(new { conversationId = current, clientMessageId = Guid.NewGuid().ToString(), type = "sticker", stickerId = int.Parse(arg) });
                break;
            case "/call":
                RequireConversation();
                var start = chat.Request("CALL_START", new { conversationId = current, type = arg == "video" ? "video" : "voice" });
                JoinMedia(start);
                Console.WriteLine("  Đang gọi... (/hangup để huỷ)");
                break;
            case "/answer":
                JoinMedia(chat.Request("CALL_ACCEPT", new { callId = long.Parse(arg) }));
                Console.WriteLine("  Đã nghe máy. /beep gửi thử âm thanh, /hangup để gác máy.");
                break;
            case "/beep":
                // Gửi 1 giây âm thanh thử (sóng sin 440Hz, PCM 16-bit mono 8kHz) chia thành 10 gói UDP
                if (media == null) throw new ChatException("Chưa ở trong cuộc gọi.");
                var pcm = Sine(440, 8000, 1.0);
                for (var i = 0; i < pcm.Length; i += 1600) media.SendAudio(pcm[i..Math.Min(i + 1600, pcm.Length)]);
                Console.WriteLine($"  Đã gửi {pcm.Length} byte âm thanh qua UDP.");
                break;
            case "/hangup":
                if (activeCallId != null) chat.Request("CALL_LEAVE", new { callId = activeCallId });
                StopMedia();
                break;
            default:
                if (cmd.StartsWith('/')) throw new ChatException("Lệnh không rõ, gõ /help.");
                RequireConversation();
                Send(new { conversationId = current, clientMessageId = Guid.NewGuid().ToString(), type = "text", content = line });
                break;
        }
    });
}

try
{
    chat.Request("LOGOUT");
}
catch
{
    // bỏ qua
}
StopMedia();
return;

// ================================================================= hàm hỗ trợ

void Send(object request)
{
    // Câu trả lời (ack) xác nhận server đã lưu. Tin cũng quay về qua sự kiện MESSAGE_NEW.
    chat.Request("SEND", request);
}

void ListConversations()
{
    Console.WriteLine("  Cuộc trò chuyện:");
    foreach (var c in chat.Request("CONV_LIST").EnumerateArray())
    {
        var unread = c.GetProperty("unreadCount").GetInt32();
        var last = c.TryGetProperty("lastMessage", out var lm) ? Preview(lm) : "";
        Console.WriteLine($"  [{c.GetProperty("conversationId")}] {c.GetProperty("name")}{(unread > 0 ? $" ({unread} chưa đọc)" : "")} - {last}");
    }
    Console.WriteLine("  Gõ /open <số> để mở.");
}

void OpenConversation(long id)
{
    current = id;
    var page = chat.Request("HISTORY", new { conversationId = id, limit = 20 });
    var items = page.GetProperty("items").EnumerateArray().ToList();
    Print(ConsoleColor.Cyan, $"---- Hội thoại {id} ({items.Count} tin gần nhất) ----");
    foreach (var m in items) PrintMessage(m);
    if (items.Count > 0) chat.Request("READ", new { conversationId = id, messageId = items[^1].GetProperty("messageId").GetInt64() });
}

void RequireConversation()
{
    if (current == null) throw new ChatException("Chưa mở cuộc trò chuyện nào. Gõ /list rồi /open <số>.");
}

void JoinMedia(JsonElement result)
{
    var call = result.GetProperty("call");
    var mediaInfo = result.GetProperty("media");
    activeCallId = call.GetProperty("callId").GetInt64();
    StopMedia();
    media = new MediaChannel(chat.Host, mediaInfo.GetProperty("udpPort").GetInt32(), activeCallId.Value,
        mediaInfo.GetProperty("mediaToken").GetString()!);
    receivedAudioPackets = 0;
    media.Received += (kind, sender, data) =>
    {
        if (kind == MediaChannel.KindJoin) return;
        receivedAudioPackets++;
        if (receivedAudioPackets % 10 == 1)
            Print(ConsoleColor.DarkCyan, $"  [UDP] nhận gói loại {kind} ({data.Length} byte) từ user {sender}, tổng {receivedAudioPackets} gói");
    };
}

void StopMedia()
{
    media?.Dispose();
    media = null;
}

void OnEvent(string evt, JsonElement data)
{
    switch (evt)
    {
        case "MESSAGE_NEW":
            if (data.GetProperty("conversationId").GetInt64() == current)
            {
                PrintMessage(data);
                if (data.TryGetProperty("sender", out var s) && s.GetProperty("userId").GetInt64() != myId)
                    Task.Run(() => chat.Request("READ", new { conversationId = current, messageId = data.GetProperty("messageId").GetInt64() }));
            }
            else
            {
                Print(ConsoleColor.Yellow, $"  🔔 Tin mới ở hội thoại {data.GetProperty("conversationId")}: {Preview(data)}");
            }
            break;
        case "TYPING":
            if (data.GetProperty("conversationId").GetInt64() == current && data.GetProperty("isTyping").GetBoolean())
                Print(ConsoleColor.DarkGray, $"  (user {data.GetProperty("userId")} đang nhập...)");
            break;
        case "PRESENCE":
            Print(ConsoleColor.DarkGray, $"  user {data.GetProperty("userId")} {(data.GetProperty("isOnline").GetBoolean() ? "vừa online" : "vừa offline")}");
            break;
        case "NOTIFICATION":
            Print(ConsoleColor.Yellow, $"  🔔 {data.GetProperty("content")}");
            break;
        case "CALL_INCOMING":
            Print(ConsoleColor.Magenta, $"  📞 Cuộc gọi {data.GetProperty("type")} #{data.GetProperty("callId")} - gõ /answer {data.GetProperty("callId")}");
            break;
        case "CALL_ENDED":
            Print(ConsoleColor.Magenta, $"  📞 Cuộc gọi kết thúc ({data.GetProperty("status")})");
            activeCallId = null;
            StopMedia();
            break;
        case "KICKED":
            Print(ConsoleColor.Red, $"  {data.GetProperty("reason")}");
            break;
        case "READ_RECEIPT":
        case "MESSAGE_UPDATED":
        case "REACTIONS_UPDATED":
        case "CALL_UPDATED":
            break;
        default:
            Print(ConsoleColor.DarkGray, $"  [sự kiện {evt}]");
            break;
    }
}

void PrintMessage(JsonElement m)
{
    var time = m.GetProperty("createdAt").GetDateTime().ToLocalTime().ToString("HH:mm");
    var type = m.GetProperty("type").GetString();
    if (type is "system" or "call")
    {
        Print(ConsoleColor.DarkGray, $"  {time}  — {m.GetProperty("content")} —");
        return;
    }
    var name = m.TryGetProperty("sender", out var s) ? s.GetProperty("displayName").GetString() : "?";
    var mine = m.TryGetProperty("sender", out var s2) && s2.GetProperty("userId").GetInt64() == myId;
    Print(mine ? ConsoleColor.Green : ConsoleColor.White, $"  {time}  {name}: {Preview(m)}");
}

static string Preview(JsonElement m)
{
    if (m.GetProperty("isDeleted").GetBoolean()) return "(đã thu hồi)";
    var text = m.TryGetProperty("content", out var c) ? c.GetString() : null;
    if (m.TryGetProperty("attachments", out var atts) && atts.GetArrayLength() > 0)
    {
        var a = atts[0];
        text = $"[tệp #{a.GetProperty("attachmentId")} {a.GetProperty("fileName")}] {text}";
    }
    if (m.GetProperty("type").GetString() == "sticker") text = "[sticker]";
    return text ?? "";
}

void Run(Action action)
{
    try
    {
        action();
    }
    catch (ChatException ex)
    {
        Print(ConsoleColor.Red, "  " + ex.Message);
    }
    catch (Exception ex)
    {
        Print(ConsoleColor.Red, $"  Lỗi: {ex.Message}");
    }
}

static void Print(ConsoleColor color, string text)
{
    lock (Console.Out)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}

static void PrintHelp()
{
    Console.WriteLine("""
      Lệnh:
        /list                 danh sách cuộc trò chuyện      /open <id>         mở cuộc trò chuyện
        <nội dung>            gửi tin vào cuộc đang mở       /sticker <id>      gửi sticker (1-6)
        /friends              danh sách bạn bè               /dm <userId>       nhắn riêng một người bạn
        /search <từ khoá>     tìm người dùng                 /add <userId>      gửi lời mời kết bạn
        /requests             lời mời đã nhận                /accept <id>       chấp nhận lời mời
        /group Tên|2,3        tạo nhóm với user 2 và 3
        /file <đường dẫn>     gửi tệp (HTTP POST)            /get <id> <lưu>    tải tệp (HTTP GET)
        /call [video]         gọi trong cuộc đang mở         /answer <callId>   nghe máy
        /beep                 gửi thử âm thanh qua UDP       /hangup            gác máy
        /quit                 thoát
    """);
}

static byte[] Sine(int frequency, int sampleRate, double seconds)
{
    var samples = (int)(sampleRate * seconds);
    var bytes = new byte[samples * 2];
    for (var i = 0; i < samples; i++)
    {
        var value = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * short.MaxValue * 0.3);
        BitConverter.GetBytes(value).CopyTo(bytes, i * 2);
    }
    return bytes;
}
