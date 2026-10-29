using ChatSocketServer.Core;

namespace ChatSocketServer.Services;

/// <summary>
/// Lưu tệp xuống đĩa bằng FileStream (chương 2).
///   data/avatars/  ảnh đại diện (ai cũng xem được qua HTTP GET /avatars/...)
///   data/files/    tệp đính kèm (chỉ thành viên hội thoại tải được qua GET /files/{id})
/// </summary>
public sealed class FileStore
{
    public const long MaxAvatarBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".msi", ".ps1", ".vbs", ".scr", ".jar", ".sh"
    };

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".svg"] = "image/svg+xml",
        [".txt"] = "text/plain; charset=utf-8", [".html"] = "text/html; charset=utf-8", [".css"] = "text/css",
        [".js"] = "application/javascript", [".json"] = "application/json", [".pdf"] = "application/pdf",
        [".zip"] = "application/zip", [".rar"] = "application/vnd.rar", [".7z"] = "application/x-7z-compressed",
        [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint", [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".mp4"] = "video/mp4", [".ico"] = "image/x-icon"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
    };

    public string AvatarDir { get; }
    public string FilesDir { get; }
    public long MaxFileBytes { get; }

    public FileStore(ServerConfig config)
    {
        var root = Path.GetFullPath(config.DataDir);
        AvatarDir = Path.Combine(root, "avatars");
        FilesDir = Path.Combine(root, "files");
        Directory.CreateDirectory(AvatarDir);
        Directory.CreateDirectory(FilesDir);
        MaxFileBytes = config.MaxFileMB * 1024L * 1024L;
    }

    public static string MimeOf(string fileName) =>
        MimeTypes.TryGetValue(Path.GetExtension(fileName), out var mime) ? mime : "application/octet-stream";

    public static bool IsImage(string fileName) => ImageExtensions.Contains(Path.GetExtension(fileName));

    /// <returns>URL công khai, ví dụ /avatars/3f2a....png</returns>
    public string SaveAvatar(byte[] data, string fileName)
    {
        if (data.Length == 0) throw new AppException("Tệp rỗng.");
        if (data.Length > MaxAvatarBytes) throw new AppException("Ảnh đại diện tối đa 5 MB.");
        if (!IsImage(fileName)) throw new AppException("Chỉ nhận ảnh PNG, JPG, GIF, WEBP, BMP.");

        var name = $"{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}";
        WriteFile(Path.Combine(AvatarDir, name), data);
        return "/avatars/" + name;
    }

    /// <returns>Đường dẫn tương đối trong thư mục files, lưu vào Attachments.StoragePath</returns>
    public string SaveAttachment(byte[] data, string fileName)
    {
        if (data.Length == 0) throw new AppException("Tệp rỗng.");
        if (data.Length > MaxFileBytes) throw new AppException($"Tệp tối đa {MaxFileBytes / 1024 / 1024} MB.");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (BlockedExtensions.Contains(ext)) throw new AppException("Loại tệp này không được phép gửi.");

        var folder = DateTime.UtcNow.ToString("yyyyMM");
        Directory.CreateDirectory(Path.Combine(FilesDir, folder));
        var relative = $"{folder}/{Guid.NewGuid():N}{ext}";
        WriteFile(Path.Combine(FilesDir, relative), data);
        return relative;
    }

    private static void WriteFile(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096);
        stream.Write(data, 0, data.Length);
    }

    public string AttachmentFullPath(string storagePath) => SafeCombine(FilesDir, storagePath);

    public string AvatarFullPath(string fileName) => SafeCombine(AvatarDir, Path.GetFileName(fileName));

    /// <summary>Chặn đường dẫn kiểu "../../" thoát ra ngoài thư mục gốc.</summary>
    public static string SafeCombine(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new AppException("Đường dẫn không hợp lệ.");
        return full;
    }
}
