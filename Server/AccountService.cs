using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Security.Cryptography;


namespace Server
{
    namespace Chat_room_test_TCP
    {
        public class UserAccount
        {
            public string Username { get; set; }
            public string DisplayName { get; set; }
            public string Email { get; set; }
            public byte[] PasswordSalt { get; set; }
            public byte[] PasswordHash { get; set; }
        }

        // Hợp đồng lưu trữ: sau này có thể thay bằng SQL Server.
        public interface IAccountRepository
        {
            bool TryAdd(UserAccount account); //thêm tài khoảng
            bool TryGet(string username, out UserAccount account); //tìm tài khoảng để đăng nhập
        }

        // Bản thử nghiệm: tài khoản chỉ tồn tại trong RAM.
        public class InMemoryAccountRepository : IAccountRepository
        {
            private readonly ConcurrentDictionary<string, UserAccount>
                accounts = new ConcurrentDictionary<string, UserAccount>(
                    StringComparer.OrdinalIgnoreCase);

            public bool TryAdd(UserAccount account)
            {
                return accounts.TryAdd(account.Username, account);
            }
            public bool TryGet(string username, out UserAccount account)
            {
                return accounts.TryGetValue(username, out account);
            }
        }

        public class AccountService
        {
            private readonly IAccountRepository repository;

            public AccountService(IAccountRepository repository)
            {
                this.repository = repository;
            }

            public string Register(string username,string email,string password,string displayName)
            {
                if (string.IsNullOrWhiteSpace(username) ||
                    username.Length < 3 || username.Length > 32)
                    return "FAIL|INVALID_USERNAME";
                if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || email.Contains("|") || email.Contains("\r") || email.Contains("\n") || !System.Text.RegularExpressions.Regex.IsMatch(email,@"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                {
                    return "FAIL|INVALID_EMAIL";
                }

                email = email.Trim();

                foreach (char c in username)
                {
                    bool valid =
                        (c >= 'a' && c <= 'z') ||
                        (c >= 'A' && c <= 'Z') ||
                        (c >= '0' && c <= '9') ||
                        c == '_' || c == '.';

                    if (!valid)
                        return "FAIL|INVALID_USERNAME";
                }

                if (string.IsNullOrEmpty(password) ||
                    password.Length < 8 || password.Length > 128 ||
                    password.Contains("|") ||
                    password.Contains("\r") ||
                    password.Contains("\n"))
                    return "FAIL|INVALID_PASSWORD";

                if (string.IsNullOrWhiteSpace(displayName) ||
                    displayName.Length > 32 ||
                    displayName.Contains("|") ||
                    displayName.Contains("\r") ||
                    displayName.Contains("\n"))
                    return "FAIL|INVALID_DISPLAY_NAME";

                username = username.Trim();
                displayName = displayName.Trim();

                // Tạo salt ngẫu nhiên cho từng tài khoản.
                byte[] salt = new byte[16];

                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }

                // Không lưu mật khẩu gốc.
                byte[] hash;

                using (var pbkdf2 =
                    new Rfc2898DeriveBytes(password, salt, 100000))
                {
                    hash = pbkdf2.GetBytes(32);
                }

                var account = new UserAccount
                {
                    Username = username,
                    Email = email,
                    DisplayName = displayName,
                    PasswordSalt = salt,
                    PasswordHash = hash
                };

                if (!repository.TryAdd(account))
                    return "FAIL|USERNAME_EXISTS";

                return "OK|REGISTERED";
            }

            public string Login(string username, string password)
            {
                if (string.IsNullOrWhiteSpace(username) ||
                    string.IsNullOrEmpty(password))
                {
                    return "FAIL|INVALID_CREDENTIALS";
                }

                UserAccount account;

                if (!repository.TryGet(username.Trim(), out account))
                {
                    return "FAIL|INVALID_CREDENTIALS";
                }

                byte[] inputHash;

                using (var pbkdf2 = new Rfc2898DeriveBytes(
                    password,
                    account.PasswordSalt,
                    100000))
                {
                    inputHash = pbkdf2.GetBytes(32);
                }

                // So sánh hash mà không dừng ngay ở byte khác biệt đầu tiên.
                bool match = inputHash.Length == account.PasswordHash.Length;
                int difference = 0;

                for (int i = 0; i < inputHash.Length; i++)
                {
                    difference |= inputHash[i] ^ account.PasswordHash[i];
                }

                match = match && difference == 0;

                if (!match)
                {
                    return "FAIL|INVALID_CREDENTIALS";
                }

                return $"OK|{account.Username}|{account.DisplayName}";
            }
        }
    }
}

//ConcurrentDictionary giúp thao tác thêm tài khoản an toàn khi nhiều client đăng ký đồng thời.
//StringComparer.OrdinalIgnoreCase khiến Maanx và maanx được xem là cùng username.
//PasswordSalt và PasswordHash được lưu thay cho mật khẩu gốc. Đoạn code sử dụng PBKDF2 với 100.000 vòng lặp để làm ví dụ ban đầu; trước khi triển khai thực tế, nên đánh giá lại tham số theo hướng dẫn bảo mật hiện hành.
//IAccountRepository giúp tách nghiệp vụ đăng ký khỏi cách lưu dữ liệu. Khi chuyển sang SQL Server, cần bổ sung phương thức lưu trữ phù hợp và ràng buộc UNIQUE cho username ở database.