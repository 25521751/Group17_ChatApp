using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ChatSocketServer.Core;

namespace ChatSocketServer.Net;

/// <summary>
/// Server TCP (chương 3): TcpListener lắng nghe, mỗi client được AcceptTcpClient()
/// rồi giao cho một ClientSession chạy trên luồng riêng.
/// </summary>
public sealed class TcpChatServer
{
    private readonly ServerContext _ctx;
    private TcpListener? _listener;
    private volatile bool _running;

    public TcpChatServer(ServerContext ctx) => _ctx = ctx;

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, _ctx.Config.TcpPort);
        _listener.Start();
        _running = true;
        new Thread(AcceptLoop) { IsBackground = true, Name = "TCP accept" }.Start();
        Log.Success($"TCP chat server đang lắng nghe cổng {_ctx.Config.TcpPort}");
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                TcpClient client = _listener!.AcceptTcpClient();   // chờ client kết nối
                var session = new ClientSession(client, _ctx);
                _ctx.Sessions.Add(session);
                session.Start();
            }
            catch (SocketException) when (!_running)
            {
                break;   // Stop() đã gọi _listener.Stop()
            }
            catch (Exception ex)
            {
                Log.Error("Lỗi khi nhận kết nối", ex);
            }
        }
    }

    public void Stop()
    {
        _running = false;
        _listener?.Stop();
        foreach (var session in _ctx.Sessions.All()) session.Kick("Server đang tắt.");
    }
}

/// <summary>
/// Quản lý các phiên đang kết nối: ai đang online, gửi sự kiện tới một người / nhiều người / cả hội thoại.
/// Một người có thể đăng nhập nhiều client cùng lúc (máy tính + điện thoại).
/// </summary>
public sealed class SessionManager
{
    private readonly ServerContext _ctx;
    private readonly ConcurrentDictionary<Guid, ClientSession> _sessions = new();
    private readonly object _presenceLock = new();

    public SessionManager(ServerContext ctx) => _ctx = ctx;

    public int Count => _sessions.Count;

    public void Add(ClientSession session) => _sessions[session.Id] = session;

    public List<ClientSession> All() => _sessions.Values.ToList();

    public List<ClientSession> OfUser(long userId) => _sessions.Values.Where(s => s.UserId == userId).ToList();

    public bool IsOnline(long userId) => _sessions.Values.Any(s => s.UserId == userId);

    public List<long> OnlineUserIds() =>
        _sessions.Values.Where(s => s.UserId != null).Select(s => s.UserId!.Value).Distinct().ToList();

    /// <summary>Gắn người dùng vào phiên sau khi đăng nhập thành công.</summary>
    public void Attach(ClientSession session, long userId, string username, string tokenHash)
    {
        if (session.UserId != null && session.UserId != userId) Detach(session);

        bool cameOnline;
        lock (_presenceLock)
        {
            cameOnline = !IsOnline(userId);
            session.UserId = userId;
            session.Username = username;
            session.TokenHash = tokenHash;
        }
        if (cameOnline) _ctx.Users.SetPresence(userId, true);
    }

    /// <summary>Gỡ người dùng khỏi phiên (đăng xuất hoặc mất kết nối).</summary>
    public void Detach(ClientSession session)
    {
        long? userId;
        bool wentOffline;
        lock (_presenceLock)
        {
            userId = session.UserId;
            session.UserId = null;
            session.TokenHash = null;
            wentOffline = userId != null && !IsOnline(userId.Value);
        }
        if (!wentOffline) return;

        _ctx.Users.SetPresence(userId!.Value, false);
        _ctx.Calls.HandleUserOffline(userId.Value);   // mất kết nối => tự rời cuộc gọi
    }

    public void Remove(ClientSession session)
    {
        _sessions.TryRemove(session.Id, out _);
        try
        {
            Detach(session);
        }
        catch (Exception ex)
        {
            Log.Error("Lỗi khi cập nhật trạng thái offline", ex);
        }
    }

    public void SendToUser(long userId, string evt, object? data)
    {
        foreach (var session in OfUser(userId)) session.SendEvent(evt, data);
    }

    public void SendToUsers(IEnumerable<long> userIds, string evt, object? data)
    {
        var targets = userIds.ToHashSet();
        foreach (var session in _sessions.Values)
            if (session.UserId is long id && targets.Contains(id))
                session.SendEvent(evt, data);
    }

    /// <summary>Gửi tới mọi thành viên đang ở trong hội thoại.</summary>
    public void SendToConversation(long conversationId, string evt, object? data) =>
        SendToUsers(_ctx.Conversations.ActiveMemberIds(conversationId), evt, data);

    /// <summary>Gửi tới thành viên hội thoại trừ một phiên (ví dụ "đang nhập" không gửi lại cho chính người nhập).</summary>
    public void SendToConversationExcept(long conversationId, Guid exceptSession, string evt, object? data)
    {
        var targets = _ctx.Conversations.ActiveMemberIds(conversationId).ToHashSet();
        foreach (var session in _sessions.Values)
            if (session.Id != exceptSession && session.UserId is long id && targets.Contains(id))
                session.SendEvent(evt, data);
    }
}
