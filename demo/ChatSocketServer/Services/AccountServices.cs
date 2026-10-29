using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using ChatSocketServer.Core;
using ChatSocketServer.Data;
using ChatSocketServer.Net;
using ChatSocketServer.Security;

namespace ChatSocketServer.Services;

// =====================================================================================
// ĐĂNG KÝ / ĐĂNG NHẬP / ĐĂNG XUẤT / MẬT KHẨU
// =====================================================================================
public sealed class AuthService
{
    private static readonly Regex UsernameRule = new("^[a-zA-Z0-9_.]{3,50}$", RegexOptions.Compiled);

    private readonly ServerContext _ctx;
    private readonly ConcurrentDictionary<string, ResetCode> _resetCodes = new(StringComparer.OrdinalIgnoreCase);

    private sealed record ResetCode(string Code, DateTime ExpiresAt, int Attempts);

    public AuthService(ServerContext ctx) => _ctx = ctx;
    private Db Db => _ctx.Db;

    public LoginResult Register(ClientSession session, Args a)
    {
        var username = a.Str("username").Trim().ToLowerInvariant();
        var email = a.Str("email").Trim().ToLowerInvariant();
        var password = a.Str("password");
        var displayName = a.Str("displayName").Trim();

        if (!UsernameRule.IsMatch(username))
            throw new AppException("Tên đăng nhập 3-50 ký tự, chỉ gồm chữ không dấu, số, dấu _ hoặc dấu chấm.");
        if (!IsValidEmail(email)) throw new AppException("Email không hợp lệ.");
        ValidatePassword(password);
        if (displayName.Length == 0 || displayName.Length > 100) throw new AppException("Tên hiển thị 1-100 ký tự.");

        if (Db.Count("SELECT COUNT(*) FROM Users WHERE Username = @username", new { username }) > 0)
            throw new AppException("Tên đăng nhập đã được sử dụng.");
        if (Db.Count("SELECT COUNT(*) FROM Users WHERE Email = @email", new { email }) > 0)
            throw new AppException("Email đã được sử dụng.");

        var now = DateTime.UtcNow;
        long userId;
        try
        {
            userId = Db.Insert(
                @"INSERT INTO Users (Username, Email, PasswordHash, DisplayName, IsBot, IsOnline, CreatedAt, UpdatedAt)
                  VALUES (@username, @email, @hash, @displayName, 0, 0, @now, @now)",
                new { username, email, hash = PasswordHasher.Hash(password), displayName, now });
        }
        catch (Exception ex) when (Db.IsUniqueViolation(ex))
        {
            throw new AppException("Tên đăng nhập hoặc email đã được sử dụng.");
        }

        // Tài khoản mới tự là bạn với chatbot để có người trò chuyện thử
        if (_ctx.Users.BotId is long botId) _ctx.Friends.AddFriendship(userId, botId);

        _ctx.Email.Send(email, "Chào mừng bạn đến với ChatApp",
            $"Xin chào {displayName},\n\nTài khoản \"{username}\" đã được tạo thành công.\n\nChatApp");

        Log.Success($"Tài khoản mới: {username}");
        return CreateSession(session, _ctx.Users.Get(userId)!, a.StrOpt("deviceInfo"));
    }

    public LoginResult Login(ClientSession session, Args a)
    {
        var login = a.Str("login").Trim().ToLowerInvariant();
        var password = a.Str("password");
        var user = Db.First($"SELECT {User.Columns} FROM Users WHERE Username = @login OR Email = @login", new { login }, User.Map);

        if (user == null || user.IsBot || !PasswordHasher.Verify(password, user.PasswordHash))
            throw new AppException("Tên đăng nhập hoặc mật khẩu không đúng.");
        return CreateSession(session, user, a.StrOpt("deviceInfo"));
    }

    /// <summary>Đăng nhập lại bằng token cũ, dùng khi client tự kết nối lại sau khi mất mạng.</summary>
    public LoginResult LoginWithToken(ClientSession session, string token)
    {
        var hash = Tokens.Sha256(token);
        var now = DateTime.UtcNow;
        var row = Db.First(
            "SELECT UserId, ExpiresAt FROM UserSessions WHERE TokenHash = @hash AND RevokedAt IS NULL AND ExpiresAt > @now",
            new { hash, now }, r => new { UserId = r.Long("UserId"), ExpiresAt = r.Date("ExpiresAt") });
        if (row == null) throw new AppException("Phiên đăng nhập đã hết hạn, vui lòng đăng nhập lại.");

        var user = _ctx.Users.Get(row.UserId) ?? throw new AppException("Tài khoản không tồn tại.");
        _ctx.Sessions.Attach(session, user.UserId, user.Username, hash);
        user.IsOnline = true;
        return new LoginResult(token, row.ExpiresAt, UserDto.From(user), user.Email);
    }

    /// <summary>Kiểm tra token cho các yêu cầu HTTP (tải tệp lên/xuống).</summary>
    public long? ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var now = DateTime.UtcNow;
        var id = Db.Scalar("SELECT UserId FROM UserSessions WHERE TokenHash = @hash AND RevokedAt IS NULL AND ExpiresAt > @now",
            new { hash = Tokens.Sha256(token), now });
        return id == null ? null : Convert.ToInt64(id);
    }

    private LoginResult CreateSession(ClientSession session, User user, string? deviceInfo)
    {
        var token = Tokens.NewSessionToken();
        var hash = Tokens.Sha256(token);
        var now = DateTime.UtcNow;
        var expires = now.AddDays(_ctx.Config.SessionDays);
        var device = string.IsNullOrWhiteSpace(deviceInfo) ? "Không rõ" : deviceInfo.Trim();

        Db.Execute(
            @"INSERT INTO UserSessions (UserId, TokenHash, DeviceInfo, IpAddress, CreatedAt, ExpiresAt)
              VALUES (@userId, @hash, @device, @ip, @now, @expires)",
            new
            {
                userId = user.UserId, hash,
                device = device.Length > 200 ? device[..200] : device,
                ip = session.RemoteAddress.Length > 45 ? session.RemoteAddress[..45] : session.RemoteAddress,
                now, expires
            });

        _ctx.Sessions.Attach(session, user.UserId, user.Username, hash);
        user.IsOnline = true;
        Log.Success($"{user.Username} đăng nhập từ {session.RemoteAddress}");
        return new LoginResult(token, expires, UserDto.From(user), user.Email);
    }

    public void Logout(ClientSession session)
    {
        if (session.TokenHash != null)
            Db.Execute("UPDATE UserSessions SET RevokedAt = @now WHERE TokenHash = @hash",
                new { now = DateTime.UtcNow, hash = session.TokenHash });
        Log.Info($"{session.Name} đăng xuất");
        _ctx.Sessions.Detach(session);
        session.Username = null;
    }

    /// <summary>Đổi mật khẩu; mọi phiên khác của tài khoản bị đăng xuất.</summary>
    public void ChangePassword(ClientSession session, string currentPassword, string newPassword)
    {
        var user = _ctx.Users.Get(session.Uid)!;
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash)) throw new AppException("Mật khẩu hiện tại không đúng.");
        ValidatePassword(newPassword);

        var now = DateTime.UtcNow;
        Db.Execute("UPDATE Users SET PasswordHash = @hash, UpdatedAt = @now WHERE UserId = @id",
            new { hash = PasswordHasher.Hash(newPassword), now, id = user.UserId });
        Db.Execute("UPDATE UserSessions SET RevokedAt = @now WHERE UserId = @id AND RevokedAt IS NULL AND TokenHash <> @current",
            new { now, id = user.UserId, current = session.TokenHash ?? "" });

        foreach (var other in _ctx.Sessions.OfUser(user.UserId).Where(s => s.Id != session.Id))
            other.Kick("Mật khẩu đã được đổi trên thiết bị khác. Vui lòng đăng nhập lại.");
    }

    /// <summary>Gửi mã 6 số qua email (chương 5: SmtpClient). Chưa cấu hình SMTP thì mã in ra console server.</summary>
    public void ForgotPassword(string email)
    {
        email = email.Trim().ToLowerInvariant();
        var user = Db.First($"SELECT {User.Columns} FROM Users WHERE Email = @email AND IsBot = 0", new { email }, User.Map);
        if (user == null) return;   // không tiết lộ email có tồn tại hay không

        var code = Tokens.NewNumericCode();
        _resetCodes[email] = new ResetCode(code, DateTime.UtcNow.AddMinutes(10), 0);
        _ctx.Email.Send(email, "Mã đặt lại mật khẩu ChatApp",
            $"Xin chào {user.DisplayName},\n\nMã đặt lại mật khẩu của bạn là: {code}\nMã có hiệu lực trong 10 phút.\n\nNếu bạn không yêu cầu, hãy bỏ qua email này.");
    }

    public void ResetPassword(string email, string code, string newPassword)
    {
        email = email.Trim().ToLowerInvariant();
        if (!_resetCodes.TryGetValue(email, out var entry) || entry.ExpiresAt < DateTime.UtcNow)
            throw new AppException("Mã không đúng hoặc đã hết hạn.");
        if (entry.Attempts >= 5)
        {
            _resetCodes.TryRemove(email, out _);
            throw new AppException("Nhập sai quá nhiều lần, hãy yêu cầu mã mới.");
        }
        if (entry.Code != code.Trim())
        {
            _resetCodes[email] = entry with { Attempts = entry.Attempts + 1 };
            throw new AppException("Mã không đúng hoặc đã hết hạn.");
        }
        ValidatePassword(newPassword);

        var now = DateTime.UtcNow;
        var userId = Convert.ToInt64(Db.Scalar("SELECT UserId FROM Users WHERE Email = @email", new { email }));
        Db.Execute("UPDATE Users SET PasswordHash = @hash, UpdatedAt = @now WHERE UserId = @userId",
            new { hash = PasswordHasher.Hash(newPassword), now, userId });
        Db.Execute("UPDATE UserSessions SET RevokedAt = @now WHERE UserId = @userId AND RevokedAt IS NULL", new { now, userId });
        _resetCodes.TryRemove(email, out _);

        foreach (var s in _ctx.Sessions.OfUser(userId)) s.Kick("Mật khẩu đã được đặt lại. Vui lòng đăng nhập lại.");
        Log.Info($"Đặt lại mật khẩu cho {email}");
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 6 || password.Length > 100) throw new AppException("Mật khẩu 6-100 ký tự.");
    }

    private static bool IsValidEmail(string email)
    {
        if (email.Length > 255) return false;
        try
        {
            return new MailAddress(email).Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

// =====================================================================================
// NGƯỜI DÙNG: hồ sơ, tìm kiếm, trạng thái online
// =====================================================================================
public sealed class UserService
{
    private readonly ServerContext _ctx;
    private long? _botId;

    public UserService(ServerContext ctx) => _ctx = ctx;
    private Db Db => _ctx.Db;

    public long? BotId
    {
        get
        {
            if (_botId == null)
            {
                var id = Db.Scalar("SELECT UserId FROM Users WHERE Username = @u", new { u = DatabaseSetup.BotUsername });
                _botId = id == null ? null : Convert.ToInt64(id);
            }
            return _botId;
        }
    }

    public User? Get(long id) =>
        Db.First($"SELECT {User.Columns} FROM Users WHERE UserId = @id", new { id }, User.Map);

    public Dictionary<long, User> GetMany(IEnumerable<long> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<long, User>();
        return Db.Query($"SELECT {User.Columns} FROM Users WHERE UserId IN ({Db.InList(list)})", null, User.Map)
            .ToDictionary(u => u.UserId);
    }

    public UserDto GetDto(long id) => UserDto.From(Get(id) ?? throw new AppException("Không tìm thấy người dùng."));

    public object Me(long id)
    {
        var user = Get(id) ?? throw new AppException("Tài khoản không tồn tại.");
        return new { user = UserDto.From(user), email = user.Email, createdAt = user.CreatedAt };
    }

    /// <summary>Tìm theo tên đăng nhập / tên hiển thị (gần đúng) hoặc email (chính xác), kèm quan hệ với mình.</summary>
    public List<UserSearchResult> Search(long me, string q)
    {
        var keyword = q.Trim().Replace("%", "").Replace("_", "").Replace("[", "");
        if (keyword.Length == 0) return new List<UserSearchResult>();

        var users = Db.Query(
            $"SELECT {User.Columns} FROM Users WHERE UserId <> @me AND (Username LIKE @pattern OR DisplayName LIKE @pattern OR Email = @exact) " +
            "ORDER BY DisplayName" + Db.Limit("@take"),
            new { me, pattern = $"%{keyword}%", exact = keyword.ToLowerInvariant(), take = 20 }, User.Map);

        var friendIds = _ctx.Friends.FriendIds(me).ToHashSet();
        var ids = Db.InList(users.Select(u => u.UserId));
        var pending = Db.Query(
            $"SELECT {FriendRequestRow.Columns} FROM FriendRequests WHERE Status = 'pending' AND " +
            $"((SenderId = @me AND ReceiverId IN ({ids})) OR (ReceiverId = @me AND SenderId IN ({ids})))",
            new { me }, FriendRequestRow.Map);

        return users.Select(u =>
        {
            if (friendIds.Contains(u.UserId)) return new UserSearchResult(UserDto.From(u), "friend", null);
            var sent = pending.FirstOrDefault(r => r.SenderId == me && r.ReceiverId == u.UserId);
            if (sent != null) return new UserSearchResult(UserDto.From(u), "request_sent", sent.RequestId);
            var received = pending.FirstOrDefault(r => r.ReceiverId == me && r.SenderId == u.UserId);
            if (received != null) return new UserSearchResult(UserDto.From(u), "request_received", received.RequestId);
            return new UserSearchResult(UserDto.From(u), "none", null);
        }).ToList();
    }

    public UserDto UpdateProfile(long me, string displayName, string? bio, string? dateOfBirth)
    {
        displayName = displayName.Trim();
        if (displayName.Length == 0 || displayName.Length > 100) throw new AppException("Tên hiển thị 1-100 ký tự.");
        bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        if (bio != null && bio.Length > 500) throw new AppException("Giới thiệu tối đa 500 ký tự.");

        DateTime? dob = null;
        if (!string.IsNullOrWhiteSpace(dateOfBirth))
        {
            if (!DateTime.TryParseExact(dateOfBirth, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsed))
                throw new AppException("Ngày sinh theo dạng yyyy-MM-dd, ví dụ 2005-03-21.");
            dob = parsed;
        }

        Db.Execute("UPDATE Users SET DisplayName = @displayName, Bio = @bio, DateOfBirth = @dob, UpdatedAt = @now WHERE UserId = @me",
            new { displayName, bio, dob, now = DateTime.UtcNow, me });
        return BroadcastProfile(me);
    }

    /// <summary>Gọi từ HTTP server sau khi ảnh đại diện đã được lưu.</summary>
    public UserDto SetAvatar(long me, string avatarUrl)
    {
        Db.Execute("UPDATE Users SET AvatarUrl = @avatarUrl, UpdatedAt = @now WHERE UserId = @me",
            new { avatarUrl, now = DateTime.UtcNow, me });
        return BroadcastProfile(me);
    }

    private UserDto BroadcastProfile(long userId)
    {
        var dto = GetDto(userId);
        _ctx.Sessions.SendToUsers(_ctx.Friends.FriendIds(userId).Append(userId), Events.UserUpdated, dto);
        return dto;
    }

    /// <summary>Cập nhật online/offline và báo cho bạn bè.</summary>
    public void SetPresence(long userId, bool online)
    {
        var now = DateTime.UtcNow;
        if (online) Db.Execute("UPDATE Users SET IsOnline = 1 WHERE UserId = @userId", new { userId });
        else Db.Execute("UPDATE Users SET IsOnline = 0, LastSeenAt = @now WHERE UserId = @userId", new { userId, now });

        _ctx.Sessions.SendToUsers(_ctx.Friends.FriendIds(userId), Events.Presence,
            new { userId, isOnline = online, lastSeenAt = online ? (DateTime?)null : now });
    }
}

// =====================================================================================
// BẠN BÈ
// =====================================================================================
public sealed class FriendService
{
    private readonly ServerContext _ctx;

    public FriendService(ServerContext ctx) => _ctx = ctx;
    private Db Db => _ctx.Db;

    public bool AreFriends(long a, long b) =>
        Db.Count("SELECT COUNT(*) FROM Friendships WHERE UserId1 = @x AND UserId2 = @y",
            new { x = Math.Min(a, b), y = Math.Max(a, b) }) > 0;

    public List<long> FriendIds(long userId) =>
        Db.Query("SELECT UserId1, UserId2 FROM Friendships WHERE UserId1 = @userId OR UserId2 = @userId",
            new { userId }, r => r.Long("UserId1") == userId ? r.Long("UserId2") : r.Long("UserId1"));

    public void AddFriendship(long a, long b)
    {
        if (a == b || AreFriends(a, b)) return;
        Db.Execute("INSERT INTO Friendships (UserId1, UserId2, CreatedAt) VALUES (@x, @y, @now)",
            new { x = Math.Min(a, b), y = Math.Max(a, b), now = DateTime.UtcNow });
    }

    public List<UserDto> List(long me) =>
        _ctx.Users.GetMany(FriendIds(me)).Values
            .OrderByDescending(u => u.IsOnline).ThenBy(u => u.DisplayName)
            .Select(UserDto.From).ToList();

    public object Requests(long me)
    {
        var rows = Db.Query(
            $"SELECT {FriendRequestRow.Columns} FROM FriendRequests WHERE Status = 'pending' AND (SenderId = @me OR ReceiverId = @me) ORDER BY CreatedAt DESC",
            new { me }, FriendRequestRow.Map);
        var dtos = ToDtos(rows);
        return new
        {
            incoming = dtos.Where(d => d.Receiver.UserId == me).ToList(),
            outgoing = dtos.Where(d => d.Sender.UserId == me).ToList()
        };
    }

    /// <summary>Gửi lời mời. Nếu người kia đã mời mình trước thì chấp nhận luôn.</summary>
    public FriendRequestDto Send(long me, long receiverId)
    {
        if (receiverId == me) throw new AppException("Không thể kết bạn với chính mình.");
        var receiver = _ctx.Users.Get(receiverId) ?? throw new AppException("Không tìm thấy người dùng.");
        if (receiver.IsBot) throw new AppException("Không thể gửi lời mời tới chatbot.");
        if (AreFriends(me, receiverId)) throw new AppException("Hai người đã là bạn bè.");

        var reverse = Db.First(
            $"SELECT {FriendRequestRow.Columns} FROM FriendRequests WHERE SenderId = @receiverId AND ReceiverId = @me AND Status = 'pending'",
            new { receiverId, me }, FriendRequestRow.Map);
        if (reverse != null) return AcceptInternal(reverse);

        long requestId;
        try
        {
            requestId = Db.Insert(
                "INSERT INTO FriendRequests (SenderId, ReceiverId, Status, CreatedAt) VALUES (@me, @receiverId, 'pending', @now)",
                new { me, receiverId, now = DateTime.UtcNow });
        }
        catch (Exception ex) when (Db.IsUniqueViolation(ex))
        {
            throw new AppException("Bạn đã gửi lời mời cho người này rồi.");
        }

        var dto = Load(requestId);
        _ctx.Sessions.SendToUser(receiverId, Events.FriendRequestReceived, dto);
        _ctx.Notifications.Notify(receiverId, NotificationType.FriendRequest, me, requestId,
            $"{dto.Sender.DisplayName} đã gửi cho bạn lời mời kết bạn");
        return dto;
    }

    public FriendRequestDto Accept(long me, long requestId)
    {
        var request = GetPending(requestId);
        if (request.ReceiverId != me) throw new AppException("Chỉ người nhận mới chấp nhận được lời mời.");
        return AcceptInternal(request);
    }

    public FriendRequestDto Reject(long me, long requestId)
    {
        var request = GetPending(requestId);
        if (request.ReceiverId != me) throw new AppException("Chỉ người nhận mới từ chối được lời mời.");
        return Close(request, FriendRequestStatus.Rejected, request.SenderId);
    }

    public FriendRequestDto Cancel(long me, long requestId)
    {
        var request = GetPending(requestId);
        if (request.SenderId != me) throw new AppException("Chỉ người gửi mới huỷ được lời mời.");
        return Close(request, FriendRequestStatus.Cancelled, request.ReceiverId);
    }

    public void Unfriend(long me, long other)
    {
        var deleted = Db.Execute("DELETE FROM Friendships WHERE UserId1 = @x AND UserId2 = @y",
            new { x = Math.Min(me, other), y = Math.Max(me, other) });
        if (deleted == 0) throw new AppException("Hai người chưa là bạn bè.");
        _ctx.Sessions.SendToUser(other, Events.FriendRemoved, new { userId = me });
        _ctx.Sessions.SendToUser(me, Events.FriendRemoved, new { userId = other });
    }

    private FriendRequestDto AcceptInternal(FriendRequestRow request)
    {
        Db.Execute("UPDATE FriendRequests SET Status = 'accepted', RespondedAt = @now WHERE RequestId = @id",
            new { now = DateTime.UtcNow, id = request.RequestId });
        AddFriendship(request.SenderId, request.ReceiverId);

        var dto = Load(request.RequestId);
        _ctx.Sessions.SendToUsers(new[] { request.SenderId, request.ReceiverId }, Events.FriendRequestUpdated, dto);
        _ctx.Sessions.SendToUser(request.SenderId, Events.FriendAdded, dto.Receiver);
        _ctx.Sessions.SendToUser(request.ReceiverId, Events.FriendAdded, dto.Sender);
        _ctx.Notifications.Notify(request.SenderId, NotificationType.FriendAccepted, request.ReceiverId, request.RequestId,
            $"{dto.Receiver.DisplayName} đã chấp nhận lời mời kết bạn");
        return dto;
    }

    private FriendRequestDto Close(FriendRequestRow request, string status, long notifyUserId)
    {
        Db.Execute("UPDATE FriendRequests SET Status = @status, RespondedAt = @now WHERE RequestId = @id",
            new { status, now = DateTime.UtcNow, id = request.RequestId });
        var dto = Load(request.RequestId);
        _ctx.Sessions.SendToUser(notifyUserId, Events.FriendRequestUpdated, dto);
        return dto;
    }

    private FriendRequestRow GetPending(long requestId)
    {
        var request = Db.First($"SELECT {FriendRequestRow.Columns} FROM FriendRequests WHERE RequestId = @requestId",
                          new { requestId }, FriendRequestRow.Map)
                      ?? throw new AppException("Không tìm thấy lời mời.");
        if (request.Status != FriendRequestStatus.Pending) throw new AppException("Lời mời đã được xử lý.");
        return request;
    }

    private FriendRequestDto Load(long requestId) =>
        ToDtos(Db.Query($"SELECT {FriendRequestRow.Columns} FROM FriendRequests WHERE RequestId = @requestId",
            new { requestId }, FriendRequestRow.Map)).Single();

    private List<FriendRequestDto> ToDtos(List<FriendRequestRow> rows)
    {
        var users = _ctx.Users.GetMany(rows.SelectMany(r => new[] { r.SenderId, r.ReceiverId }));
        return rows.Select(r => new FriendRequestDto(r.RequestId, UserDto.From(users[r.SenderId]), UserDto.From(users[r.ReceiverId]),
            r.Status, r.CreatedAt, r.RespondedAt)).ToList();
    }
}

// =====================================================================================
// EMAIL (chương 5: SmtpClient + MailMessage)
// =====================================================================================
public sealed class EmailService
{
    private readonly SmtpConfig _config;

    public EmailService(SmtpConfig config) => _config = config;

    /// <summary>Gửi trên luồng nền để không làm chậm client. Chưa cấu hình SMTP thì in nội dung ra console.</summary>
    public void Send(string to, string subject, string body)
    {
        if (!_config.Enabled)
        {
            Log.Warn($"[Email - SMTP chưa cấu hình] Tới: {to} | {subject}\n{body}");
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                using var client = new SmtpClient(_config.Host, _config.Port)
                {
                    EnableSsl = _config.EnableSsl,
                    Credentials = new NetworkCredential(_config.Username, _config.Password)
                };
                using var message = new MailMessage(_config.From, to, subject, body)
                {
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                };
                client.Send(message);
                Log.Info($"Đã gửi email tới {to}: {subject}");
            }
            catch (Exception ex)
            {
                Log.Error($"Gửi email tới {to} thất bại", ex);
            }
        });
    }
}
