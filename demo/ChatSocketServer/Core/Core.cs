using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatSocketServer.Core;

/// <summary>Cấu hình đọc từ appsettings.json.</summary>
public class ServerConfig
{
    public string ServerName { get; set; } = "ChatApp Server";
    public int TcpPort { get; set; } = 9000;
    public int HttpPort { get; set; } = 8080;
    public int DiscoveryPort { get; set; } = 9001;
    public int MediaPort { get; set; } = 9002;

    /// <summary>Sqlite hoặc SqlServer.</summary>
    public string DatabaseProvider { get; set; } = "Sqlite";
    public string SqliteConnection { get; set; } = "Data Source=data/chatapp.db;Default Timeout=15;Foreign Keys=True";
    public string SqlServerConnection { get; set; } = "Server=localhost;Database=ChatApp;Trusted_Connection=True;TrustServerCertificate=True";

    public string DataDir { get; set; } = "data";
    /// <summary>true = client bắt buộc gửi SECURE (trao đổi khoá RSA/AES) trước khi đăng nhập.</summary>
    public bool RequireEncryption { get; set; }
    public bool SeedDemoData { get; set; } = true;
    public int MaxFileMB { get; set; } = 25;
    public int SessionDays { get; set; } = 30;
    /// <summary>Không nhận được gói nào (kể cả PING) trong khoảng này thì coi như mất kết nối.</summary>
    public int ClientTimeoutSeconds { get; set; } = 90;
    public SmtpConfig Smtp { get; set; } = new();

    public static ServerConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn($"Không thấy {path}, dùng cấu hình mặc định.");
            return new ServerConfig();
        }
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };
        return JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), options) ?? new ServerConfig();
    }
}

public class SmtpConfig
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public bool Enabled => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>Lỗi nghiệp vụ: thông báo được gửi nguyên văn về client.</summary>
public class AppException : Exception
{
    public AppException(string message) : base(message) { }
}

/// <summary>Ghi log ra console có màu, đồng thời giữ 200 dòng gần nhất để hiện trên trang trạng thái.</summary>
public static class Log
{
    private static readonly object Lock = new();
    private static readonly Queue<string> Recent = new();

    public static void Info(string message) => Write(message, ConsoleColor.Gray);
    public static void Success(string message) => Write(message, ConsoleColor.Green);
    public static void Net(string message) => Write(message, ConsoleColor.Cyan);
    public static void Warn(string message) => Write(message, ConsoleColor.Yellow);
    public static void Error(string message, Exception? ex = null) =>
        Write(ex == null ? message : $"{message}: {ex.GetType().Name} - {ex.Message}", ConsoleColor.Red);

    private static void Write(string message, ConsoleColor color)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (Lock)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ResetColor();
            Recent.Enqueue(line);
            while (Recent.Count > 200) Recent.Dequeue();
        }
    }

    public static List<string> GetRecent()
    {
        lock (Lock) return Recent.Reverse().ToList();
    }
}

/// <summary>Cấu hình JSON dùng chung: tên trường camelCase, bỏ trường null, giữ nguyên tiếng Việt.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(object? value) => JsonSerializer.Serialize(value, Options);
}

/// <summary>Truy cập các trường trong "data" của gói tin yêu cầu, báo lỗi rõ ràng khi thiếu.</summary>
public sealed class Args
{
    private readonly JsonElement _data;

    public Args(JsonElement data) => _data = data;

    private bool TryGet(string name, out JsonElement value)
    {
        value = default;
        if (_data.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in _data.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined;
            }
        return false;
    }

    public string Str(string name) =>
        StrOpt(name) ?? throw new AppException($"Thiếu trường \"{name}\".");

    public string? StrOpt(string name)
    {
        if (!TryGet(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
    }

    public long Long(string name) =>
        LongOpt(name) ?? throw new AppException($"Thiếu trường \"{name}\".");

    public long? LongOpt(string name)
    {
        if (!TryGet(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        throw new AppException($"Trường \"{name}\" phải là số.");
    }

    public int Int(string name, int defaultValue) => (int)(LongOpt(name) ?? defaultValue);

    public bool Bool(string name, bool defaultValue = false)
    {
        if (!TryGet(name, out var v)) return defaultValue;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => throw new AppException($"Trường \"{name}\" phải là true/false.")
        };
    }

    public List<long> Longs(string name)
    {
        if (!TryGet(name, out var v) || v.ValueKind != JsonValueKind.Array)
            throw new AppException($"Trường \"{name}\" phải là mảng số.");
        var list = new List<long>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var n)) list.Add(n);
        return list;
    }
}
