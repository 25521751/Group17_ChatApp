using ChatSocketServer.Core;
using ChatSocketServer.Data;
using ChatSocketServer.Net;

namespace ChatSocketServer.Services;

/// <summary>
/// Gọi thoại / video.
/// - Tín hiệu (gọi, nghe, từ chối, gác máy) đi qua TCP như các lệnh khác.
/// - Âm thanh / hình ảnh đi qua UDP cổng MediaPort: client gửi gói lên server, server chuyển tiếp
///   cho những người khác trong cuộc gọi (MediaRelay). Mỗi người có một mediaToken để server nhận diện.
/// Mọi thay đổi trạng thái cuộc gọi được khoá bằng _lock để tránh hai luồng sửa cùng lúc.
/// </summary>
public sealed class CallService
{
    public static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(45);

    private readonly ServerContext _ctx;
    private readonly object _lock = new();

    public CallService(ServerContext ctx) => _ctx = ctx;
    private Db Db => _ctx.Db;

    public CallJoinResult Start(long callerId, long conversationId, string type)
    {
        type = type.Trim().ToLowerInvariant();
        if (type != CallType.Voice && type != CallType.Video) throw new AppException("Loại cuộc gọi phải là voice hoặc video.");
        _ctx.Conversations.RequireMember(conversationId, callerId);

        lock (_lock)
        {
            if (Db.Count("SELECT COUNT(*) FROM Calls WHERE ConversationId = @conversationId AND Status IN ('ringing','ongoing')",
                    new { conversationId }) > 0)
                throw new AppException("Cuộc trò chuyện này đang có cuộc gọi khác.");
            if (Db.Count(
                    "SELECT COUNT(*) FROM CallParticipants p JOIN Calls c ON c.CallId = p.CallId " +
                    "WHERE p.UserId = @callerId AND p.Status = 'joined' AND c.Status IN ('ringing','ongoing')",
                    new { callerId }) > 0)
                throw new AppException("Bạn đang ở trong một cuộc gọi khác.");

            var invitees = Db.Query(
                "SELECT m.UserId FROM ConversationMembers m JOIN Users u ON u.UserId = m.UserId " +
                "WHERE m.ConversationId = @conversationId AND m.LeftAt IS NULL AND m.UserId <> @callerId AND u.IsBot = 0",
                new { conversationId, callerId }, r => r.Long("UserId"));
            if (invitees.Count == 0) throw new AppException("Không có ai để gọi.");

            var now = DateTime.UtcNow;
            var callId = Db.Insert(
                "INSERT INTO Calls (ConversationId, CallerId, Type, Status, StartedAt) VALUES (@conversationId, @callerId, @type, 'ringing', @now)",
                new { conversationId, callerId, type, now });
            Db.Execute("INSERT INTO CallParticipants (CallId, UserId, Status, JoinedAt) VALUES (@callId, @callerId, 'joined', @now)",
                new { callId, callerId, now });
            foreach (var userId in invitees)
                Db.Execute("INSERT INTO CallParticipants (CallId, UserId, Status) VALUES (@callId, @userId, 'invited')", new { callId, userId });

            var dto = LoadDto(callId);
            _ctx.Sessions.SendToUsers(invitees, Events.CallIncoming, dto);
            Log.Info($"Cuộc gọi {type} #{callId} trong hội thoại {conversationId}");
            return new CallJoinResult(dto, _ctx.Media.Register(callId, callerId));
        }
    }

    /// <summary>Nghe máy: nhận mediaToken để bắt đầu gửi/nhận UDP.</summary>
    public CallJoinResult Accept(long userId, long callId)
    {
        lock (_lock)
        {
            var (call, me) = LoadForParticipant(callId, userId);
            if (!call.IsActive) throw new AppException("Cuộc gọi đã kết thúc.");

            var now = DateTime.UtcNow;
            Db.Execute("UPDATE CallParticipants SET Status = 'joined', JoinedAt = @now, LeftAt = NULL WHERE CallId = @callId AND UserId = @userId",
                new { now, callId, userId });
            if (call.Status == CallStatus.Ringing)
                Db.Execute("UPDATE Calls SET Status = 'ongoing', AnsweredAt = @now WHERE CallId = @callId", new { now, callId });

            var media = _ctx.Media.Register(callId, userId);
            return new CallJoinResult(BroadcastUpdate(callId), media);
        }
    }

    public CallDto Reject(long userId, long callId)
    {
        lock (_lock)
        {
            var (call, me) = LoadForParticipant(callId, userId);
            if (!call.IsActive) return LoadDto(callId);
            if (me.Status != ParticipantStatus.Invited) throw new AppException("Bạn không còn ở trạng thái được mời.");
            Db.Execute("UPDATE CallParticipants SET Status = 'declined' WHERE CallId = @callId AND UserId = @userId", new { callId, userId });
            return EndIfNeeded(callId);
        }
    }

    /// <summary>Gác máy / rời cuộc gọi / người gọi huỷ khi đang đổ chuông.</summary>
    public CallDto Leave(long userId, long callId)
    {
        lock (_lock)
        {
            var (call, me) = LoadForParticipant(callId, userId);
            if (!call.IsActive) return LoadDto(callId);

            if (me.Status == ParticipantStatus.Joined)
                Db.Execute("UPDATE CallParticipants SET Status = 'left', LeftAt = @now WHERE CallId = @callId AND UserId = @userId",
                    new { now = DateTime.UtcNow, callId, userId });
            else if (me.Status == ParticipantStatus.Invited)
                Db.Execute("UPDATE CallParticipants SET Status = 'declined' WHERE CallId = @callId AND UserId = @userId", new { callId, userId });

            _ctx.Media.Unregister(callId, userId);
            return EndIfNeeded(callId);
        }
    }

    /// <summary>Người dùng mất kết nối hoàn toàn => tự rời các cuộc gọi đang tham gia.</summary>
    public void HandleUserOffline(long userId)
    {
        var callIds = Db.Query(
            "SELECT p.CallId FROM CallParticipants p JOIN Calls c ON c.CallId = p.CallId " +
            "WHERE p.UserId = @userId AND p.Status = 'joined' AND c.Status IN ('ringing','ongoing')",
            new { userId }, r => r.Long("CallId"));
        foreach (var callId in callIds) Leave(userId, callId);
    }

    /// <summary>Chạy mỗi 10 giây: cuộc gọi đổ chuông quá 45 giây => cuộc gọi nhỡ.</summary>
    public void ExpireRinging()
    {
        var deadline = DateTime.UtcNow - RingTimeout;
        var callIds = Db.Query("SELECT CallId FROM Calls WHERE Status = 'ringing' AND StartedAt < @deadline",
            new { deadline }, r => r.Long("CallId"));
        foreach (var callId in callIds)
            lock (_lock) Finish(callId);
    }

    public List<CallDto> History(long me, long conversationId, int limit)
    {
        _ctx.Conversations.RequireMember(conversationId, me);
        limit = Math.Clamp(limit, 1, 100);
        var ids = Db.Query("SELECT CallId FROM Calls WHERE ConversationId = @conversationId ORDER BY CallId DESC" + Db.Limit("@limit"),
            new { conversationId, limit }, r => r.Long("CallId"));
        return ids.Select(LoadDto).ToList();
    }

    // ------------------------------------------------------------------ nội bộ (gọi khi đang giữ _lock)

    private CallDto EndIfNeeded(long callId)
    {
        var call = LoadRow(callId);
        var participants = LoadParticipants(callId);
        var joined = participants.Count(p => p.Status == ParticipantStatus.Joined);
        var invited = participants.Count(p => p.Status == ParticipantStatus.Invited);

        var shouldEnd = call.Status == CallStatus.Ringing
            ? joined == 0 || invited == 0     // người gọi huỷ, hoặc mọi người đều từ chối
            : joined <= 1;                    // chỉ còn một người
        return shouldEnd ? Finish(callId) : BroadcastUpdate(callId);
    }

    private CallDto Finish(long callId)
    {
        var call = LoadRow(callId);
        if (!call.IsActive) return LoadDto(callId);
        var participants = LoadParticipants(callId);
        var now = DateTime.UtcNow;

        var answered = call.AnsweredAt != null;
        var anyDeclined = participants.Any(p => p.Status == ParticipantStatus.Declined);
        var anyInvited = participants.Any(p => p.Status == ParticipantStatus.Invited);
        var status = answered ? CallStatus.Ended : anyDeclined && !anyInvited ? CallStatus.Rejected : CallStatus.Missed;

        Db.Execute("UPDATE Calls SET Status = @status, EndedAt = @now WHERE CallId = @callId", new { status, now, callId });
        Db.Execute("UPDATE CallParticipants SET Status = 'left', LeftAt = @now WHERE CallId = @callId AND Status = 'joined'", new { now, callId });
        var missed = participants.Where(p => p.Status == ParticipantStatus.Invited).Select(p => p.UserId).ToList();
        Db.Execute("UPDATE CallParticipants SET Status = 'missed' WHERE CallId = @callId AND Status = 'invited'", new { callId });
        _ctx.Media.UnregisterCall(callId);

        var kind = call.Type == CallType.Video ? "video" : "thoại";
        var callerName = _ctx.Users.Get(call.CallerId)?.DisplayName ?? "Ai đó";
        foreach (var userId in missed)
            _ctx.Notifications.Notify(userId, NotificationType.MissedCall, call.CallerId, call.ConversationId, $"Cuộc gọi {kind} nhỡ từ {callerName}");

        string log;
        if (answered)
        {
            var duration = now - call.AnsweredAt!.Value;
            log = $"Cuộc gọi {kind} · {(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
        }
        else log = status == CallStatus.Rejected ? $"Cuộc gọi {kind} bị từ chối" : $"Cuộc gọi {kind} nhỡ";
        _ctx.Messages.SendCallLog(call.ConversationId, call.CallerId, log);

        var dto = LoadDto(callId);
        _ctx.Sessions.SendToUsers(participants.Select(p => p.UserId), Events.CallEnded, dto);
        Log.Info($"Cuộc gọi #{callId} kết thúc: {status}");
        return dto;
    }

    private CallDto BroadcastUpdate(long callId)
    {
        var dto = LoadDto(callId);
        _ctx.Sessions.SendToUsers(dto.Participants.Select(p => p.UserId), Events.CallUpdated, dto);
        return dto;
    }

    private (CallRow Call, ParticipantRow Me) LoadForParticipant(long callId, long userId)
    {
        var call = Db.First($"SELECT {CallRow.Columns} FROM Calls WHERE CallId = @callId", new { callId }, CallRow.Map)
                   ?? throw new AppException("Không tìm thấy cuộc gọi.");
        var me = LoadParticipants(callId).FirstOrDefault(p => p.UserId == userId)
                 ?? throw new AppException("Bạn không ở trong cuộc gọi này.");
        return (call, me);
    }

    private CallRow LoadRow(long callId) =>
        Db.First($"SELECT {CallRow.Columns} FROM Calls WHERE CallId = @callId", new { callId }, CallRow.Map)!;

    private List<ParticipantRow> LoadParticipants(long callId) =>
        Db.Query("SELECT CallId, UserId, Status, JoinedAt, LeftAt FROM CallParticipants WHERE CallId = @callId", new { callId }, ParticipantRow.Map);

    private CallDto LoadDto(long callId)
    {
        var call = LoadRow(callId);
        var participants = LoadParticipants(callId);
        var users = _ctx.Users.GetMany(participants.Select(p => p.UserId));
        return new CallDto(call.CallId, call.ConversationId, call.CallerId, call.Type, call.Status, call.StartedAt, call.AnsweredAt, call.EndedAt,
            participants.Select(p => new CallParticipantDto(p.UserId,
                users.TryGetValue(p.UserId, out var u) ? u.DisplayName : "",
                users.TryGetValue(p.UserId, out var u2) ? u2.AvatarUrl : null,
                p.Status, p.JoinedAt, p.LeftAt)).ToList());
    }
}
