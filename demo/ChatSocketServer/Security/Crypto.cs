using System.Security.Cryptography;
using System.Text;

namespace ChatSocketServer.Security;

/// <summary>
/// Băm mật khẩu bằng PBKDF2 (SHA-256, 100.000 vòng, salt ngẫu nhiên 16 byte).
/// Chuỗi lưu trong Users.PasswordHash có dạng: PBKDF2$vòng$salt(base64)$hash(base64).
/// Không bao giờ lưu hoặc ghi log mật khẩu gốc.
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            // So sánh thời gian cố định để tránh tấn công đo thời gian
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class Tokens
{
    /// <summary>Token phiên đăng nhập: 32 byte ngẫu nhiên dạng hex (64 ký tự).</summary>
    public static string NewSessionToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>Token UDP cho mỗi người trong cuộc gọi: 16 byte.</summary>
    public static byte[] NewMediaToken() => RandomNumberGenerator.GetBytes(16);

    /// <summary>Mã 6 chữ số gửi qua email khi quên mật khẩu.</summary>
    public static string NewNumericCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Sha256(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}

/// <summary>
/// Mã hoá kênh TCP theo mô hình "khoá phiên" (chương 10):
///   1. Server có cặp khoá RSA 2048 bit, gửi khoá công khai (XML) cho client trong gói HELLO.
///   2. Client tạo khoá AES-256 ngẫu nhiên (khoá phiên), mã hoá bằng RSA-OAEP rồi gửi lệnh SECURE.
///   3. Server giải mã bằng khoá bí mật RSA => hai bên cùng có khoá AES.
///   4. Từ đó mỗi dòng gửi đi = Base64( IV 16 byte + AES-CBC(JSON) ). Mỗi gói một IV ngẫu nhiên.
/// </summary>
public static class SecureChannel
{
    private static readonly RSA ServerRsa = RSA.Create(2048);

    /// <summary>Khoá công khai dạng XML, đọc được bằng RSACryptoServiceProvider.FromXmlString trên .NET Framework.</summary>
    public static string PublicKeyXml { get; } = ServerRsa.ToXmlString(false);

    /// <summary>Giải mã khoá AES client gửi lên (RSA-OAEP SHA-1, tương thích RSACryptoServiceProvider.Encrypt(data, true)).</summary>
    public static byte[] DecryptSessionKey(string base64)
    {
        byte[] key;
        try
        {
            key = ServerRsa.Decrypt(Convert.FromBase64String(base64), RSAEncryptionPadding.OaepSHA1);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new Core.AppException("Không giải mã được khoá phiên. Hãy mã hoá khoá AES bằng khoá công khai trong gói HELLO.");
        }
        if (key.Length != 16 && key.Length != 24 && key.Length != 32)
            throw new Core.AppException("Khoá AES phải dài 16, 24 hoặc 32 byte.");
        return key;
    }

    public static string EncryptLine(byte[] key, string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(plainText), aes.IV, PaddingMode.PKCS7);
        var packet = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, packet, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, packet, aes.IV.Length, cipher.Length);
        return Convert.ToBase64String(packet);
    }

    public static string DecryptLine(byte[] key, string line)
    {
        try
        {
            var packet = Convert.FromBase64String(line);
            if (packet.Length < 32) throw new FormatException();
            using var aes = Aes.Create();
            aes.Key = key;
            var iv = packet.AsSpan(0, 16).ToArray();
            var plain = aes.DecryptCbc(packet.AsSpan(16).ToArray(), iv, PaddingMode.PKCS7);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new Core.AppException("Gói tin mã hoá không hợp lệ.");
        }
    }
}
