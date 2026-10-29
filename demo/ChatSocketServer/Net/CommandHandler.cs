using ChatSocketServer.Core;
using ChatSocketServer.Security;

namespace ChatSocketServer.Net;

/// <summary>
/// Bảng lệnh: ánh xạ tên lệnh ("cmd") tới hàm xử lý. Mỗi hàm nhận phiên + dữ liệu,
/// trả về đối tượng sẽ nằm trong "data" của câu trả lời; ném AppException để báo lỗi.
/// </summary>
public sealed class CommandHandler
{
    private delegate object? CommandFn(ClientSession session, Args args);

    private readonly ServerContext _ctx;
    private readonly Dictionary<string, (bool RequireLogin, CommandFn Fn)> _commands = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Quiet = new(StringComparer.OrdinalIgnoreCase) { "PING", "TYPING", "READ" };

    public CommandHandler(ServerContext ctx)
    {
        _ctx = ctx;
        RegisterCommands();
    }

    public IEnumerable<string> CommandNames => _commands.Keys.Append("SECURE").OrderBy(x => x);

    private void Public(string name, CommandFn fn) => _commands[name] = (false, fn);
    private void Private(string name, CommandFn fn) => _commands[name] = (true, fn);

    private void RegisterCommands()
    {
        var c = _ctx;

        // ---------- không cần đăng nhập
        Public("PING", (s, a) => new { pong = DateTime.UtcNow });
        Public("REGISTER", (s, a) => c.Auth.Register(s, a));
        Public("LOGIN", (s, a) => c.Auth.Login(s, a));
        Public("LOGIN_TOKEN", (s, a) => c.Auth.LoginWithToken(s, a.Str("token")));
        Public("FORGOT_PASSWORD", (s, a) => { c.Auth.ForgotPassword(a.Str("email")); return null; });
        Public("RESET_PASSWORD", (s, a) => { c.Auth.ResetPassword(a.Str("email"), a.Str("code"), a.Str("newPassword")); return null; });

        // ---------- tài khoản
        Private("LOGOUT", (s, a) => { c.Auth.Logout(s); return null; });
        Private("CHANGE_PASSWORD", (s, a) => { c.Auth.ChangePassword(s, a.Str("currentPassword"), a.Str("newPassword")); return null; });
        Private("ME", (s, a) => c.Users.Me(s.Uid));
        Private("UPDATE_PROFILE", (s, a) => c.Users.UpdateProfile(s.Uid, a.Str("displayName"), a.StrOpt("bio"), a.StrOpt("dateOfBirth")));
        Private("GET_USER", (s, a) => c.Users.GetDto(a.Long("userId")));
        Private("SEARCH_USERS", (s, a) => c.Users.Search(s.Uid, a.Str("q")));

        // ---------- bạn bè
        Private("FRIEND_LIST", (s, a) => c.Friends.List(s.Uid));
        Private("FRIEND_REQUESTS", (s, a) => c.Friends.Requests(s.Uid));
        Private("FRIEND_REQUEST", (s, a) => c.Friends.Send(s.Uid, a.Long("receiverId")));
        Private("FRIEND_ACCEPT", (s, a) => c.Friends.Accept(s.Uid, a.Long("requestId")));
        Private("FRIEND_REJECT", (s, a) => c.Friends.Reject(s.Uid, a.Long("requestId")));
        Private("FRIEND_CANCEL", (s, a) => c.Friends.Cancel(s.Uid, a.Long("requestId")));
        Private("UNFRIEND", (s, a) => { c.Friends.Unfriend(s.Uid, a.Long("userId")); return null; });

        // ---------- hội thoại & nhóm
        Private("CONV_LIST", (s, a) => c.Conversations.List(s.Uid));
        Private("CONV_GET", (s, a) => c.Conversations.Get(s.Uid, a.Long("conversationId")));
        Private("CONV_DIRECT", (s, a) => c.Conversations.OpenDirect(s.Uid, a.Long("userId")));
        Private("CONV_MUTE", (s, a) => { c.Conversations.SetMuted(s.Uid, a.Long("conversationId"), a.Bool("isMuted")); return null; });
        Private("GROUP_CREATE", (s, a) => c.Conversations.CreateGroup(s.Uid, a.Str("name"), a.Longs("memberIds")));
        Private("GROUP_RENAME", (s, a) => c.Conversations.Rename(s.Uid, a.Long("conversationId"), a.Str("name")));
        Private("GROUP_ADD", (s, a) => c.Conversations.AddMembers(s.Uid, a.Long("conversationId"), a.Longs("userIds")));
        Private("GROUP_REMOVE", (s, a) => { c.Conversations.RemoveMember(s.Uid, a.Long("conversationId"), a.Long("userId")); return null; });
        Private("GROUP_LEAVE", (s, a) => { c.Conversations.Leave(s.Uid, a.Long("conversationId")); return null; });
        Private("GROUP_ROLE", (s, a) => c.Conversations.SetRole(s.Uid, a.Long("conversationId"), a.Long("userId"), a.Str("role")));

        // ---------- tin nhắn
        Private("HISTORY", (s, a) => c.Messages.History(s.Uid, a.Long("conversationId"), a.LongOpt("before"), a.Int("limit", 30)));
        Private("SEND", (s, a) => c.Messages.Send(s.Uid, a.Long("conversationId"), a.Str("clientMessageId"),
            a.StrOpt("type"), a.StrOpt("content"), (int?)a.LongOpt("stickerId"), a.LongOpt("replyToId")));
        Private("EDIT", (s, a) => c.Messages.Edit(s.Uid, a.Long("messageId"), a.Str("content")));
        Private("RECALL", (s, a) => c.Messages.Recall(s.Uid, a.Long("messageId")));
        Private("REACT", (s, a) => c.Messages.React(s.Uid, a.Long("messageId"), a.Str("emoji"), add: true));
        Private("UNREACT", (s, a) => c.Messages.React(s.Uid, a.Long("messageId"), a.Str("emoji"), add: false));
        Private("READ", (s, a) => { c.Messages.MarkRead(s.Uid, a.Long("conversationId"), a.Long("messageId")); return null; });
        Private("TYPING", (s, a) =>
        {
            var conversationId = a.Long("conversationId");
            c.Conversations.RequireMember(conversationId, s.Uid);
            c.Sessions.SendToConversationExcept(conversationId, s.Id, Events.Typing,
                new { conversationId, userId = s.Uid, isTyping = a.Bool("isTyping", true) });
            return null;
        });
        Private("STICKERS", (s, a) => c.Messages.Stickers());

        // ---------- thông báo
        Private("NOTIFICATIONS", (s, a) => c.Notifications.List(s.Uid, a.LongOpt("before"), a.Int("limit", 30)));
        Private("NOTIFICATION_READ", (s, a) => { c.Notifications.MarkRead(s.Uid, a.LongOpt("notificationId")); return null; });

        // ---------- cuộc gọi (tín hiệu qua TCP, âm thanh/hình ảnh qua UDP)
        Private("CALL_START", (s, a) => c.Calls.Start(s.Uid, a.Long("conversationId"), a.Str("type")));
        Private("CALL_ACCEPT", (s, a) => c.Calls.Accept(s.Uid, a.Long("callId")));
        Private("CALL_REJECT", (s, a) => c.Calls.Reject(s.Uid, a.Long("callId")));
        Private("CALL_LEAVE", (s, a) => c.Calls.Leave(s.Uid, a.Long("callId")));
        Private("CALL_HISTORY", (s, a) => c.Calls.History(s.Uid, a.Long("conversationId"), a.Int("limit", 20)));
    }

    public void Handle(ClientSession session, Request request)
    {
        var cmd = request.Cmd.Trim().ToUpperInvariant();

        if (cmd == "SECURE")
        {
            HandleSecure(session, request);
            return;
        }

        if (!_commands.TryGetValue(cmd, out var entry))
        {
            session.SendResponse(Response.Fail(cmd, request.Id, $"Lệnh không được hỗ trợ: {cmd}"));
            return;
        }
        if (_ctx.Config.RequireEncryption && !session.IsSecure && cmd != "PING")
        {
            session.SendResponse(Response.Fail(cmd, request.Id, "Server yêu cầu mã hoá: hãy gửi lệnh SECURE trước."));
            return;
        }
        if (entry.RequireLogin && session.UserId == null)
        {
            session.SendResponse(Response.Fail(cmd, request.Id, "Bạn chưa đăng nhập."));
            return;
        }

        try
        {
            var result = entry.Fn(session, new Args(request.Data));
            session.SendResponse(Response.Success(cmd, request.Id, result));
            if (!Quiet.Contains(cmd)) Log.Info($"{session.Name} → {cmd}");
        }
        catch (AppException ex)
        {
            session.SendResponse(Response.Fail(cmd, request.Id, ex.Message));
            Log.Warn($"{session.Name} → {cmd}: {ex.Message}");
        }
        catch (Exception ex)
        {
            session.SendResponse(Response.Fail(cmd, request.Id, "Lỗi máy chủ, vui lòng thử lại."));
            Log.Error($"{session.Name} → {cmd}", ex);
        }
    }

    /// <summary>Trao đổi khoá: data.key = Base64(RSA-OAEP(khoá AES)).</summary>
    private void HandleSecure(ClientSession session, Request request)
    {
        try
        {
            if (session.IsSecure) throw new AppException("Kênh đã được mã hoá.");
            var key = SecureChannel.DecryptSessionKey(new Args(request.Data).Str("key"));
            session.EnableEncryption(key, Response.Success("SECURE", request.Id, new { cipher = $"AES-{key.Length * 8}-CBC" }));
            Log.Net($"{session.Name}: đã bật mã hoá AES-{key.Length * 8}");
        }
        catch (AppException ex)
        {
            session.SendResponse(Response.Fail("SECURE", request.Id, ex.Message));
        }
    }
}
