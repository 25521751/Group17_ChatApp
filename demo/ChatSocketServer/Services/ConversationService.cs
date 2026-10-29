using ChatSocketServer.Core;
using ChatSocketServer.Data;
using ChatSocketServer.Net;

namespace ChatSocketServer.Services;

/// <summary>Hội thoại 1-1 và nhóm: danh sách, tạo, thành viên, vai trò, tắt thông báo.</summary>
public sealed class ConversationService
{
    public const int MaxGroupMembers = 100;

    private readonly ServerContext _ctx;

    public ConversationService(ServerContext ctx) => _ctx = ctx;
    private Db Db => _ctx.Db;

    // ------------------------------------------------------------------ quyền

    /// <summary>Người dùng phải đang ở trong hội thoại, nếu không thì báo lỗi.</summary>
    public Membership RequireMember(long conversationId, long userId)
    {
        var member = Db.First(Membership.Select + "WHERE m.ConversationId = @conversationId AND m.UserId = @userId AND m.LeftAt IS NULL",
            new { conversationId, userId }, Membership.Map);
        if (member != null) return member;

        var exists = Db.Count("SELECT COUNT(*) FROM Conversations WHERE ConversationId = @conversationId", new { conversationId }) > 0;
        throw new AppException(exists ? "Bạn không phải thành viên của cuộc trò chuyện này." : "Không tìm thấy cuộc trò chuyện.");
    }

    /// <summary>Chỉ trưởng nhóm / phó nhóm.</summary>
    public Membership RequireGroupAdmin(long conversationId, long userId)
    {
        var me = RequireMember(conversationId, userId);
        if (!me.IsGroup) throw new AppException("Chức năng này chỉ dành cho nhóm chat.");
        if (!me.IsAdmin) throw new AppException("Chỉ trưởng nhóm hoặc phó nhóm mới làm được việc này.");
        return me;
    }

    public List<long> ActiveMemberIds(long conversationId) =>
        Db.Query("SELECT UserId FROM ConversationMembers WHERE ConversationId = @conversationId AND LeftAt IS NULL",
            new { conversationId }, r => r.Long("UserId"));

    // ------------------------------------------------------------------ đọc

    public List<ConversationDto> List(long me)
    {
        var memberships = Db.Query(Membership.Select + "WHERE m.UserId = @me AND m.LeftAt IS NULL", new { me }, Membership.Map);
        return memberships.Select(Build).OrderByDescending(c => c.LastMessageAt).ToList();
    }

    public ConversationDto Get(long me, long conversationId) => Build(RequireMember(conversationId, me));

    private ConversationDto Build(Membership me)
    {
        var memberRows = Db.Query(
            "SELECT UserId, Role, Nickname, JoinedAt, LastReadMessageId FROM ConversationMembers " +
            "WHERE ConversationId = @c AND LeftAt IS NULL ORDER BY JoinedAt",
            new { c = me.ConversationId },
            r => new { UserId = r.Long("UserId"), Role = r.Str("Role"), Nickname = r.StrN("Nickname"),
                       JoinedAt = r.Date("JoinedAt"), LastRead = r.LongN("LastReadMessageId") });
        var users = _ctx.Users.GetMany(memberRows.Select(m => m.UserId));
        var members = memberRows.Where(m => users.ContainsKey(m.UserId)).Select(m =>
        {
            var u = users[m.UserId];
            return new MemberDto(u.UserId, u.Username, u.DisplayName, u.AvatarUrl, u.IsOnline, u.LastSeenAt, u.IsBot,
                m.Role, m.Nickname, m.JoinedAt, m.LastRead);
        }).ToList();

        var lastRow = Db.Query($"SELECT {MessageRow.Columns} FROM Messages WHERE ConversationId = @c ORDER BY MessageId DESC" + Db.Limit("1"),
            new { c = me.ConversationId }, MessageRow.Map);
        var last = _ctx.Messages.BuildDtos(lastRow).FirstOrDefault();

        var unread = (int)Db.Count(
            "SELECT COUNT(*) FROM Messages WHERE ConversationId = @c AND MessageId > @lastRead " +
            "AND SenderId IS NOT NULL AND SenderId <> @me AND DeletedAt IS NULL",
            new { c = me.ConversationId, lastRead = me.LastReadMessageId ?? 0, me = me.UserId });

        string name;
        string? avatar;
        UserDto? other = null;
        if (me.IsGroup)
        {
            name = me.Name ?? "Nhóm";
            avatar = me.AvatarUrl;
        }
        else
        {
            var otherId = memberRows.Select(m => m.UserId).FirstOrDefault(id => id != me.UserId);
            if (users.TryGetValue(otherId, out var u)) other = UserDto.From(u);
            name = other?.DisplayName ?? "Người dùng";
            avatar = other?.AvatarUrl;
        }

        return new ConversationDto(me.ConversationId, me.Type, name, avatar, me.CreatedBy, me.Role, me.IsMuted, unread,
            me.LastMessageAt ?? me.CreatedAt, last, other, members);
    }

    // ------------------------------------------------------------------ chat 1-1

    /// <summary>Mở (hoặc tạo) cuộc trò chuyện 1-1. Chỉ nhắn được với bạn bè hoặc chatbot.</summary>
    public ConversationDto OpenDirect(long me, long otherId)
    {
        if (otherId == me) throw new AppException("Không thể tự nhắn tin với chính mình.");
        var other = _ctx.Users.Get(otherId) ?? throw new AppException("Không tìm thấy người dùng.");
        if (!other.IsBot && !_ctx.Friends.AreFriends(me, otherId)) throw new AppException("Hãy kết bạn trước khi nhắn tin.");

        var key = $"{Math.Min(me, otherId)}_{Math.Max(me, otherId)}";
        var existing = Db.Scalar("SELECT ConversationId FROM Conversations WHERE DirectKey = @key", new { key });
        if (existing != null) return Get(me, Convert.ToInt64(existing));

        var now = DateTime.UtcNow;
        long conversationId;
        try
        {
            conversationId = Db.Insert(
                "INSERT INTO Conversations (Type, CreatedBy, DirectKey, LastMessageAt, CreatedAt) VALUES ('direct', @me, @key, @now, @now)",
                new { me, key, now });
        }
        catch (Exception ex) when (Db.IsUniqueViolation(ex))
        {
            // Người kia vừa tạo cùng lúc
            return Get(me, Convert.ToInt64(Db.Scalar("SELECT ConversationId FROM Conversations WHERE DirectKey = @key", new { key })));
        }

        foreach (var userId in new[] { me, otherId })
            Db.Execute("INSERT INTO ConversationMembers (ConversationId, UserId, Role, JoinedAt, IsMuted) VALUES (@conversationId, @userId, 'member', @now, 0)",
                new { conversationId, userId, now });

        _ctx.Sessions.SendToUsers(new[] { me, otherId }, Events.ConversationCreated, new { conversationId });
        return Get(me, conversationId);
    }

    // ------------------------------------------------------------------ nhóm

    public ConversationDto CreateGroup(long me, string name, List<long> memberIds)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > 100) throw new AppException("Tên nhóm 1-100 ký tự.");
        var ids = memberIds.Where(id => id != me).Distinct().ToList();
        if (ids.Count < 1) throw new AppException("Nhóm cần ít nhất 2 người.");
        if (ids.Count + 1 > MaxGroupMembers) throw new AppException($"Nhóm tối đa {MaxGroupMembers} người.");
        var users = _ctx.Users.GetMany(ids);
        if (users.Count != ids.Count) throw new AppException("Có người dùng không tồn tại.");

        var now = DateTime.UtcNow;
        var conversationId = Db.Insert(
            "INSERT INTO Conversations (Type, Name, CreatedBy, LastMessageAt, CreatedAt) VALUES ('group', @name, @me, @now, @now)",
            new { name, me, now });
        Db.Execute("INSERT INTO ConversationMembers (ConversationId, UserId, Role, JoinedAt, IsMuted) VALUES (@conversationId, @me, 'owner', @now, 0)",
            new { conversationId, me, now });
        foreach (var userId in ids)
            Db.Execute(
                "INSERT INTO ConversationMembers (ConversationId, UserId, Role, AddedBy, JoinedAt, IsMuted) VALUES (@conversationId, @userId, 'member', @me, @now, 0)",
                new { conversationId, userId, me, now });

        var creator = NameOf(me);
        _ctx.Messages.SendSystem(conversationId, $"{creator} đã tạo nhóm \"{name}\"");
        foreach (var userId in ids)
            _ctx.Notifications.Notify(userId, NotificationType.AddedToGroup, me, conversationId, $"{creator} đã thêm bạn vào nhóm \"{name}\"");
        _ctx.Sessions.SendToUsers(ids.Append(me), Events.ConversationCreated, new { conversationId });
        return Get(me, conversationId);
    }

    public ConversationDto Rename(long me, long conversationId, string name)
    {
        RequireGroupAdmin(conversationId, me);
        name = name.Trim();
        if (name.Length == 0 || name.Length > 100) throw new AppException("Tên nhóm 1-100 ký tự.");
        Db.Execute("UPDATE Conversations SET Name = @name WHERE ConversationId = @conversationId", new { name, conversationId });
        _ctx.Messages.SendSystem(conversationId, $"{NameOf(me)} đã đổi tên nhóm thành \"{name}\"");
        _ctx.Sessions.SendToConversation(conversationId, Events.ConversationUpdated, new { conversationId });
        return Get(me, conversationId);
    }

    /// <summary>Gọi từ HTTP server sau khi ảnh nhóm đã được lưu.</summary>
    public ConversationDto SetAvatar(long me, long conversationId, string avatarUrl)
    {
        RequireGroupAdmin(conversationId, me);
        Db.Execute("UPDATE Conversations SET AvatarUrl = @avatarUrl WHERE ConversationId = @conversationId", new { avatarUrl, conversationId });
        _ctx.Messages.SendSystem(conversationId, $"{NameOf(me)} đã đổi ảnh nhóm");
        _ctx.Sessions.SendToConversation(conversationId, Events.ConversationUpdated, new { conversationId });
        return Get(me, conversationId);
    }

    /// <summary>Trưởng/phó nhóm thêm thành viên. Người từng rời nhóm được thêm lại.</summary>
    public ConversationDto AddMembers(long me, long conversationId, List<long> userIds)
    {
        var admin = RequireGroupAdmin(conversationId, me);
        var ids = userIds.Where(id => id != me).Distinct().ToList();
        if (ids.Count == 0) throw new AppException("Chưa chọn người để thêm.");
        var users = _ctx.Users.GetMany(ids);
        if (users.Count != ids.Count) throw new AppException("Có người dùng không tồn tại.");

        var existing = Db.Query(
            $"SELECT UserId, LeftAt FROM ConversationMembers WHERE ConversationId = @conversationId AND UserId IN ({Db.InList(ids)})",
            new { conversationId }, r => new { UserId = r.Long("UserId"), Left = r.DateN("LeftAt") != null })
            .ToDictionary(x => x.UserId, x => x.Left);
        var activeCount = Db.Count("SELECT COUNT(*) FROM ConversationMembers WHERE ConversationId = @conversationId AND LeftAt IS NULL",
            new { conversationId });

        var toAdd = ids.Where(id => !existing.TryGetValue(id, out var left) || left).ToList();
        if (toAdd.Count == 0) throw new AppException("Những người này đã ở trong nhóm.");
        if (activeCount + toAdd.Count > MaxGroupMembers) throw new AppException($"Nhóm tối đa {MaxGroupMembers} người.");

        var now = DateTime.UtcNow;
        foreach (var userId in toAdd)
        {
            if (existing.ContainsKey(userId))
                Db.Execute(
                    "UPDATE ConversationMembers SET LeftAt = NULL, JoinedAt = @now, Role = 'member', AddedBy = @me WHERE ConversationId = @conversationId AND UserId = @userId",
                    new { now, me, conversationId, userId });
            else
                Db.Execute(
                    "INSERT INTO ConversationMembers (ConversationId, UserId, Role, AddedBy, JoinedAt, IsMuted) VALUES (@conversationId, @userId, 'member', @me, @now, 0)",
                    new { conversationId, userId, me, now });
        }

        var actor = NameOf(me);
        _ctx.Messages.SendSystem(conversationId, $"{actor} đã thêm {string.Join(", ", toAdd.Select(id => users[id].DisplayName))} vào nhóm");
        foreach (var userId in toAdd)
            _ctx.Notifications.Notify(userId, NotificationType.AddedToGroup, me, conversationId, $"{actor} đã thêm bạn vào nhóm \"{admin.Name}\"");
        _ctx.Sessions.SendToUsers(toAdd, Events.ConversationCreated, new { conversationId });
        _ctx.Sessions.SendToConversation(conversationId, Events.ConversationUpdated, new { conversationId });
        return Get(me, conversationId);
    }

    /// <summary>Trưởng nhóm xoá được mọi người; phó nhóm chỉ xoá được thành viên thường.</summary>
    public void RemoveMember(long me, long conversationId, long targetId)
    {
        if (targetId == me) throw new AppException("Dùng GROUP_LEAVE để tự rời nhóm.");
        var admin = RequireGroupAdmin(conversationId, me);
        var target = Db.First(Membership.Select + "WHERE m.ConversationId = @conversationId AND m.UserId = @targetId AND m.LeftAt IS NULL",
                         new { conversationId, targetId }, Membership.Map)
                     ?? throw new AppException("Người này không ở trong nhóm.");
        if (target.Role == MemberRole.Owner) throw new AppException("Không thể xoá trưởng nhóm.");
        if (target.Role == MemberRole.Admin && admin.Role != MemberRole.Owner)
            throw new AppException("Chỉ trưởng nhóm mới xoá được phó nhóm.");

        Db.Execute("UPDATE ConversationMembers SET LeftAt = @now, Role = 'member' WHERE ConversationId = @conversationId AND UserId = @targetId",
            new { now = DateTime.UtcNow, conversationId, targetId });

        _ctx.Messages.SendSystem(conversationId, $"{NameOf(me)} đã xoá {NameOf(targetId)} khỏi nhóm");
        _ctx.Sessions.SendToUser(targetId, Events.ConversationRemoved, new { conversationId });
        _ctx.Sessions.SendToConversation(conversationId, Events.ConversationUpdated, new { conversationId });
    }

    /// <summary>Rời nhóm. Trưởng nhóm rời thì quyền chuyển cho phó nhóm / thành viên vào sớm nhất.</summary>
    public void Leave(long me, long conversationId)
    {
        var member = RequireMember(conversationId, me);
        if (!member.IsGroup) throw new AppException("Chỉ rời được nhóm chat.");

        var now = DateTime.UtcNow;
        Db.Execute("UPDATE ConversationMembers SET LeftAt = @now, Role = 'member' WHERE ConversationId = @conversationId AND UserId = @me",
            new { now, conversationId, me });

        string? newOwner = null;
        if (member.Role == MemberRole.Owner)
        {
            var successor = Db.Query(
                "SELECT m.UserId, m.Role FROM ConversationMembers m JOIN Users u ON u.UserId = m.UserId " +
                "WHERE m.ConversationId = @conversationId AND m.LeftAt IS NULL AND u.IsBot = 0 " +
                "ORDER BY CASE WHEN m.Role = 'admin' THEN 0 ELSE 1 END, m.JoinedAt",
                new { conversationId }, r => r.Long("UserId")).FirstOrDefault();
            if (successor != 0)
            {
                Db.Execute("UPDATE ConversationMembers SET Role = 'owner' WHERE ConversationId = @conversationId AND UserId = @successor",
                    new { conversationId, successor });
                newOwner = NameOf(successor);
            }
        }

        var name = NameOf(me);
        _ctx.Messages.SendSystem(conversationId, newOwner == null
            ? $"{name} đã rời nhóm"
            : $"{name} đã rời nhóm, {newOwner} trở thành trưởng nhóm");
        _ctx.Sessions.SendToUser(me, Events.ConversationRemoved, new { conversationId });
        _ctx.Sessions.SendToConversation(conversationId, Events.ConversationUpdated, new { conversationId });
    }

    /// <summary>Trưởng nhóm đặt vai trò admin / member, hoặc "owner" để chuyển quyền trưởng nhóm.</summary>
    public ConversationDto SetRole(long me, long conversationId, long targetId, string role)
    {
        role = role.Trim().ToLowerInvariant();
        if (role != MemberRole.Owner && role != MemberRole.Admin && role != MemberRole.Member)
            throw new AppException("Vai trò phải là owner, admin hoặc member.");
        var admin = RequireGroupAdmin(conversationId, me);
        if (admin.Role != MemberRole.Owner) throw new AppException("Chỉ trưởng nhóm mới đổi được vai trò.");
        if (targetId == me) throw new AppException("Không thể tự đổi vai trò của mình.");
        RequireMember(conversationId, targetId);

        Db.Execute("UPDATE ConversationMembers SET Role = @role WHERE ConversationId = @conversationId AND UserId = @targetId",
            new { role, conversationId, targetId });
        if (role == MemberRole.Owner)
            Db.Execute("UPDATE ConversationMembers SET Role = 'admin' WHERE ConversationId = @conversationId AND UserId = @me",
                new { conversationId, me });

        var targetName = NameOf(targetId);
        _ctx.Messages.SendSystem(conversationId, role switch
        {
            MemberRole.Owner => $"{targetName} đã trở thành trưởng nhóm",
            MemberRole.Admin => $"{targetName} đã được chỉ định làm phó nhóm",
            _ => $"{targetName} không còn là phó nhóm"
        });
        _ctx.Sessions.SendToConversation(conversationId, Events.ConversationUpdated, new { conversationId });
        return Get(me, conversationId);
    }

    public void SetMuted(long me, long conversationId, bool isMuted)
    {
        RequireMember(conversationId, me);
        Db.Execute("UPDATE ConversationMembers SET IsMuted = @isMuted WHERE ConversationId = @conversationId AND UserId = @me",
            new { isMuted, conversationId, me });
    }

    private string NameOf(long userId) =>
        Convert.ToString(Db.Scalar("SELECT DisplayName FROM Users WHERE UserId = @userId", new { userId })) ?? "Ai đó";
}
