using ChatSocketServer.Core;
using ChatSocketServer.Data;
using ChatSocketServer.Net;

namespace ChatSocketServer.Services;

/// <summary>Gửi, lưu, sửa, thu hồi tin nhắn; lịch sử; cảm xúc; đã đọc; tệp đính kèm.</summary>
public sealed class MessageService
{
    public const int MaxContentLength = 4000;

    private readonly ServerContext _ctx;

    public MessageService(ServerContext ctx) => _ctx = ctx;
    private Db Db => _ctx.Db;

    // ------------------------------------------------------------------ gửi

    /// <summary>
    /// Gửi tin văn bản hoặc sticker. Nếu clientMessageId đã có (client gửi lại sau khi mất kết nối)
    /// thì trả về tin đã lưu, không tạo bản trùng.
    /// </summary>
    public MessageDto Send(long me, long conversationId, string clientMessageId, string? type, string? content, int? stickerId, long? replyToId)
    {
        _ctx.Conversations.RequireMember(conversationId, me);
        ValidateClientMessageId(clientMessageId);
        var duplicate = FindByClientId(me, clientMessageId);
        if (duplicate != null) return duplicate;

        type = string.IsNullOrWhiteSpace(type) ? MessageType.Text : type.Trim().ToLowerInvariant();
        var row = new MessageRow { ConversationId = conversationId, SenderId = me, ClientMessageId = clientMessageId, Type = type };
        switch (type)
        {
            case MessageType.Text:
                row.Content = ValidateContent(content);
                break;
            case MessageType.Sticker:
                if (stickerId == null || Db.Count("SELECT COUNT(*) FROM Stickers WHERE StickerId = @stickerId", new { stickerId }) == 0)
                    throw new AppException("Sticker không tồn tại.");
                row.StickerId = stickerId;
                break;
            default:
                throw new AppException("Loại tin không hợp lệ. Hình ảnh và tệp gửi qua HTTP POST /upload.");
        }
        row.ReplyToId = ValidateReply(replyToId, conversationId);
        return Insert(row, null);
    }

    /// <summary>Gọi từ HTTP server sau khi tệp đã được lưu xuống đĩa.</summary>
    public MessageDto SendAttachment(long me, long conversationId, string clientMessageId, string fileName, string mimeType,
        long size, string storagePath, string? caption, long? replyToId)
    {
        var row = new MessageRow
        {
            ConversationId = conversationId,
            SenderId = me,
            ClientMessageId = clientMessageId,
            Type = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? MessageType.Image : MessageType.File,
            Content = string.IsNullOrWhiteSpace(caption) ? null : ValidateContent(caption),
            ReplyToId = ValidateReply(replyToId, conversationId)
        };
        var attachment = new AttachmentRow
        {
            UploaderId = me,
            FileName = fileName.Length > 255 ? fileName[..255] : fileName,
            StoragePath = storagePath,
            MimeType = mimeType,
            FileSize = size
        };
        return Insert(row, attachment);
    }

    /// <summary>Tin hệ thống: "A đã thêm B vào nhóm"...</summary>
    public MessageDto SendSystem(long conversationId, string content) =>
        Insert(new MessageRow { ConversationId = conversationId, ClientMessageId = Guid.NewGuid().ToString("N"), Type = MessageType.System, Content = content }, null);

    /// <summary>Dòng ghi lại cuộc gọi trong lịch sử trò chuyện.</summary>
    public MessageDto SendCallLog(long conversationId, long callerId, string content) =>
        Insert(new MessageRow { ConversationId = conversationId, SenderId = callerId, ClientMessageId = Guid.NewGuid().ToString("N"), Type = MessageType.Call, Content = content }, null);

    public MessageDto SendAsBot(long conversationId, long botId, string content) =>
        Insert(new MessageRow { ConversationId = conversationId, SenderId = botId, ClientMessageId = Guid.NewGuid().ToString("N"), Type = MessageType.Text, Content = content }, null);

    private MessageDto Insert(MessageRow row, AttachmentRow? attachment)
    {
        var now = DateTime.UtcNow;
        long messageId;
        try
        {
            messageId = Db.Insert(
                @"INSERT INTO Messages (ConversationId, SenderId, ClientMessageId, Type, Content, StickerId, ReplyToId, CreatedAt)
                  VALUES (@ConversationId, @SenderId, @ClientMessageId, @Type, @Content, @StickerId, @ReplyToId, @now)",
                new { row.ConversationId, row.SenderId, row.ClientMessageId, row.Type, row.Content, row.StickerId, row.ReplyToId, now });
        }
        catch (Exception ex) when (Db.IsUniqueViolation(ex) && row.SenderId != null)
        {
            // Hai lần gửi cùng clientMessageId tới gần như đồng thời
            return FindByClientId(row.SenderId.Value, row.ClientMessageId) ?? throw new AppException("Không lưu được tin nhắn.");
        }

        if (attachment != null)
            Db.Execute(
                @"INSERT INTO Attachments (MessageId, UploaderId, FileName, StoragePath, MimeType, FileSize, CreatedAt)
                  VALUES (@messageId, @UploaderId, @FileName, @StoragePath, @MimeType, @FileSize, @now)",
                new { messageId, attachment.UploaderId, attachment.FileName, attachment.StoragePath, attachment.MimeType, attachment.FileSize, now });

        Db.Execute("UPDATE Conversations SET LastMessageAt = @now WHERE ConversationId = @c", new { now, c = row.ConversationId });
        if (row.SenderId != null)   // người gửi coi như đã đọc tin của mình
            Db.Execute("UPDATE ConversationMembers SET LastReadMessageId = @messageId WHERE ConversationId = @c AND UserId = @s",
                new { messageId, c = row.ConversationId, s = row.SenderId });

        var dto = LoadDto(messageId);
        _ctx.Sessions.SendToConversation(row.ConversationId, Events.MessageNew, dto);

        if (row.SenderId != null && row.Type != MessageType.Call)
        {
            NotifyOfflineMembers(row, dto);
            TriggerChatbot(row);
        }
        return dto;
    }

    /// <summary>
    /// Người đang online đã nhận MESSAGE_NEW. Người offline được lưu MỘT thông báo "tin nhắn mới"
    /// cho mỗi hội thoại (không tạo thêm nếu thông báo cũ chưa đọc) để thấy khi đăng nhập lại.
    /// </summary>
    private void NotifyOfflineMembers(MessageRow row, MessageDto dto)
    {
        var recipients = Db.Query(
            "SELECT m.UserId FROM ConversationMembers m JOIN Users u ON u.UserId = m.UserId " +
            "WHERE m.ConversationId = @c AND m.LeftAt IS NULL AND m.IsMuted = 0 AND u.IsBot = 0 AND m.UserId <> @s",
            new { c = row.ConversationId, s = row.SenderId }, r => r.Long("UserId"));
        var offline = recipients.Where(id => !_ctx.Sessions.IsOnline(id)).ToList();
        if (offline.Count == 0) return;

        var already = Db.Query(
            $"SELECT UserId FROM Notifications WHERE UserId IN ({Db.InList(offline)}) AND IsRead = 0 AND Type = 'new_message' AND ReferenceId = @c",
            new { c = row.ConversationId }, r => r.Long("UserId")).ToHashSet();

        var conversation = Db.First("SELECT Type, Name FROM Conversations WHERE ConversationId = @c", new { c = row.ConversationId },
            r => new { Type = r.Str("Type"), Name = r.StrN("Name") });
        var sender = dto.Sender?.DisplayName ?? "Ai đó";
        var prefix = conversation?.Type == ConversationType.Group ? $"{sender} ({conversation.Name})" : sender;
        var content = Truncate($"{prefix}: {Preview(row)}", 255);
        var now = DateTime.UtcNow;

        foreach (var userId in offline.Where(id => !already.Contains(id)))
            Db.Execute(
                "INSERT INTO Notifications (UserId, ActorId, Type, ReferenceId, Content, IsRead, CreatedAt) VALUES (@userId, @s, 'new_message', @c, @content, 0, @now)",
                new { userId, s = row.SenderId, c = row.ConversationId, content, now });
    }

    /// <summary>Chat 1-1 với chatbot: bot trả lời mọi tin. Trong nhóm có bot: chỉ khi tin chứa "@chatbot".</summary>
    private void TriggerChatbot(MessageRow row)
    {
        if (row.Type != MessageType.Text || string.IsNullOrWhiteSpace(row.Content)) return;
        if (_ctx.Users.BotId is not long botId || row.SenderId == botId) return;

        var membership = Db.First(Membership.Select + "WHERE m.ConversationId = @c AND m.UserId = @botId AND m.LeftAt IS NULL",
            new { c = row.ConversationId, botId }, Membership.Map);
        if (membership == null) return;
        if (membership.IsGroup && !row.Content.Contains("@" + DatabaseSetup.BotUsername, StringComparison.OrdinalIgnoreCase)) return;

        _ctx.Bot.Reply(row.ConversationId, botId, row.Content);
    }

    // ------------------------------------------------------------------ đọc

    /// <summary>Lịch sử theo trang: lần đầu bỏ trống before; cuộn lên thì before = MessageId nhỏ nhất đang có.</summary>
    public PageResult<MessageDto> History(long me, long conversationId, long? before, int limit)
    {
        _ctx.Conversations.RequireMember(conversationId, me);
        limit = Math.Clamp(limit, 1, 100);

        var sql = $"SELECT {MessageRow.Columns} FROM Messages WHERE ConversationId = @conversationId"
                  + (before != null ? " AND MessageId < @before" : "")
                  + " ORDER BY MessageId DESC" + Db.Limit("@take");
        var rows = Db.Query(sql, new { conversationId, before, take = limit + 1 }, MessageRow.Map);

        var hasMore = rows.Count > limit;
        var page = rows.Take(limit).OrderBy(r => r.MessageId).ToList();
        return new PageResult<MessageDto>(BuildDtos(page), hasMore);
    }

    public MessageDto? FindByClientId(long senderId, string clientMessageId)
    {
        var id = Db.Scalar("SELECT MessageId FROM Messages WHERE SenderId = @senderId AND ClientMessageId = @clientMessageId",
            new { senderId, clientMessageId });
        return id == null ? null : LoadDto(Convert.ToInt64(id));
    }

    public MessageDto LoadDto(long messageId) =>
        BuildDtos(Db.Query($"SELECT {MessageRow.Columns} FROM Messages WHERE MessageId = @messageId", new { messageId }, MessageRow.Map))
            .FirstOrDefault() ?? throw new AppException("Không tìm thấy tin nhắn.");

    /// <summary>Ghép tin nhắn với người gửi, tệp, sticker, cảm xúc, tin được trả lời (mỗi loại một truy vấn IN).</summary>
    public List<MessageDto> BuildDtos(List<MessageRow> rows)
    {
        if (rows.Count == 0) return new List<MessageDto>();
        var ids = Db.InList(rows.Select(r => r.MessageId));

        var attachments = Db.Query($"SELECT {AttachmentRow.Columns} FROM Attachments WHERE MessageId IN ({ids})", null, AttachmentRow.Map)
            .ToLookup(a => a.MessageId);
        var reactions = Db.Query($"SELECT MessageId, UserId, Emoji FROM MessageReactions WHERE MessageId IN ({ids})", null,
                r => new { MessageId = r.Long("MessageId"), UserId = r.Long("UserId"), Emoji = r.Str("Emoji") })
            .ToLookup(x => x.MessageId);

        var replyIds = rows.Where(r => r.ReplyToId != null).Select(r => r.ReplyToId!.Value).ToList();
        var replies = replyIds.Count == 0
            ? new Dictionary<long, MessageRow>()
            : Db.Query($"SELECT {MessageRow.Columns} FROM Messages WHERE MessageId IN ({Db.InList(replyIds)})", null, MessageRow.Map)
                .ToDictionary(r => r.MessageId);

        var stickerIds = rows.Where(r => r.StickerId != null).Select(r => (long)r.StickerId!.Value).ToList();
        var stickers = stickerIds.Count == 0
            ? new Dictionary<int, StickerDto>()
            : Db.Query($"SELECT StickerId, PackId, ImageUrl, Keyword FROM Stickers WHERE StickerId IN ({Db.InList(stickerIds)})", null,
                    r => new StickerDto(r.Int("StickerId"), r.Int("PackId"), r.Str("ImageUrl"), r.StrN("Keyword")))
                .ToDictionary(s => s.StickerId);

        var userIds = rows.Where(r => r.SenderId != null).Select(r => r.SenderId!.Value)
            .Concat(replies.Values.Where(r => r.SenderId != null).Select(r => r.SenderId!.Value));
        var users = _ctx.Users.GetMany(userIds);

        return rows.Select(m =>
        {
            var deleted = m.DeletedAt != null;
            ReplyPreviewDto? reply = null;
            if (m.ReplyToId != null && replies.TryGetValue(m.ReplyToId.Value, out var r))
            {
                var replySender = r.SenderId != null && users.TryGetValue(r.SenderId.Value, out var rs) ? rs.DisplayName : null;
                reply = new ReplyPreviewDto(r.MessageId, r.SenderId, replySender, r.Type,
                    r.DeletedAt != null ? null : Preview(r), r.DeletedAt != null);
            }

            return new MessageDto(
                m.MessageId, m.ConversationId, m.ClientMessageId,
                m.SenderId != null && users.TryGetValue(m.SenderId.Value, out var sender) ? UserDto.From(sender) : null,
                m.Type,
                deleted ? null : m.Content,
                !deleted && m.StickerId != null && stickers.TryGetValue(m.StickerId.Value, out var sticker) ? sticker : null,
                reply,
                deleted ? new List<AttachmentDto>() : attachments[m.MessageId]
                    .Select(a => new AttachmentDto(a.AttachmentId, a.FileName, a.MimeType, a.FileSize, $"/files/{a.AttachmentId}")).ToList(),
                reactions[m.MessageId].GroupBy(x => x.Emoji)
                    .Select(g => new ReactionDto(g.Key, g.Count(), g.Select(x => x.UserId).ToList())).ToList(),
                m.CreatedAt, m.EditedAt, deleted);
        }).ToList();
    }

    // ------------------------------------------------------------------ sửa / thu hồi

    public MessageDto Edit(long me, long messageId, string content)
    {
        var row = GetOwn(me, messageId);
        if (row.Type != MessageType.Text) throw new AppException("Chỉ sửa được tin nhắn văn bản.");
        Db.Execute("UPDATE Messages SET Content = @content, EditedAt = @now WHERE MessageId = @messageId",
            new { content = ValidateContent(content), now = DateTime.UtcNow, messageId });
        var dto = LoadDto(messageId);
        _ctx.Sessions.SendToConversation(row.ConversationId, Events.MessageUpdated, dto);
        return dto;
    }

    public MessageDto Recall(long me, long messageId)
    {
        var row = GetOwn(me, messageId);
        Db.Execute("UPDATE Messages SET DeletedAt = @now WHERE MessageId = @messageId", new { now = DateTime.UtcNow, messageId });
        var dto = LoadDto(messageId);
        _ctx.Sessions.SendToConversation(row.ConversationId, Events.MessageUpdated, dto);
        return dto;
    }

    private MessageRow GetOwn(long me, long messageId)
    {
        var row = GetRow(messageId);
        _ctx.Conversations.RequireMember(row.ConversationId, me);
        if (row.SenderId != me) throw new AppException("Bạn chỉ sửa hoặc thu hồi được tin của mình.");
        if (row.DeletedAt != null) throw new AppException("Tin nhắn đã được thu hồi.");
        return row;
    }

    private MessageRow GetRow(long messageId) =>
        Db.First($"SELECT {MessageRow.Columns} FROM Messages WHERE MessageId = @messageId", new { messageId }, MessageRow.Map)
        ?? throw new AppException("Không tìm thấy tin nhắn.");

    // ------------------------------------------------------------------ cảm xúc

    public List<ReactionDto> React(long me, long messageId, string emoji, bool add)
    {
        emoji = emoji.Trim();
        if (emoji.Length == 0 || emoji.Length > 16) throw new AppException("Emoji không hợp lệ.");
        var row = GetRow(messageId);
        _ctx.Conversations.RequireMember(row.ConversationId, me);
        if (row.DeletedAt != null) throw new AppException("Tin nhắn đã được thu hồi.");

        if (add)
        {
            try
            {
                Db.Execute("INSERT INTO MessageReactions (MessageId, UserId, Emoji, CreatedAt) VALUES (@messageId, @me, @emoji, @now)",
                    new { messageId, me, emoji, now = DateTime.UtcNow });
            }
            catch (Exception ex) when (Db.IsUniqueViolation(ex))
            {
                // đã thả rồi
            }
        }
        else
        {
            Db.Execute("DELETE FROM MessageReactions WHERE MessageId = @messageId AND UserId = @me AND Emoji = @emoji",
                new { messageId, me, emoji });
        }

        var reactions = LoadDto(messageId).Reactions;
        _ctx.Sessions.SendToConversation(row.ConversationId, Events.ReactionsUpdated,
            new { conversationId = row.ConversationId, messageId, reactions });
        return reactions;
    }

    // ------------------------------------------------------------------ đã đọc

    public void MarkRead(long me, long conversationId, long messageId)
    {
        var member = _ctx.Conversations.RequireMember(conversationId, me);
        if (Db.Count("SELECT COUNT(*) FROM Messages WHERE MessageId = @messageId AND ConversationId = @conversationId",
                new { messageId, conversationId }) == 0)
            throw new AppException("Tin nhắn không thuộc cuộc trò chuyện này.");

        if (member.LastReadMessageId == null || member.LastReadMessageId < messageId)
        {
            Db.Execute("UPDATE ConversationMembers SET LastReadMessageId = @messageId WHERE ConversationId = @conversationId AND UserId = @me",
                new { messageId, conversationId, me });
            _ctx.Sessions.SendToConversation(conversationId, Events.ReadReceipt, new { conversationId, userId = me, messageId });
        }
        _ctx.Notifications.MarkConversationRead(me, conversationId);
    }

    // ------------------------------------------------------------------ tệp, sticker

    /// <summary>Chỉ thành viên đang ở trong hội thoại mới tải được tệp.</summary>
    public (string FullPath, string MimeType, string FileName) GetAttachment(long me, long attachmentId)
    {
        var attachment = Db.First($"SELECT {AttachmentRow.Columns} FROM Attachments WHERE AttachmentId = @attachmentId",
                             new { attachmentId }, AttachmentRow.Map)
                         ?? throw new AppException("Không tìm thấy tệp.");
        var message = GetRow(attachment.MessageId);
        _ctx.Conversations.RequireMember(message.ConversationId, me);
        if (message.DeletedAt != null) throw new AppException("Tệp đã bị thu hồi.");

        var path = _ctx.Files.AttachmentFullPath(attachment.StoragePath);
        if (!File.Exists(path)) throw new AppException("Tệp không còn trên máy chủ.");
        return (path, attachment.MimeType, attachment.FileName);
    }

    public List<StickerPackDto> Stickers()
    {
        var stickers = Db.Query("SELECT StickerId, PackId, ImageUrl, Keyword FROM Stickers ORDER BY StickerId", null,
            r => new StickerDto(r.Int("StickerId"), r.Int("PackId"), r.Str("ImageUrl"), r.StrN("Keyword"))).ToLookup(s => s.PackId);
        return Db.Query("SELECT PackId, Name, ThumbnailUrl FROM StickerPacks ORDER BY PackId", null,
            r => new StickerPackDto(r.Int("PackId"), r.Str("Name"), r.StrN("ThumbnailUrl"), new List<StickerDto>()))
            .Select(p => p with { Stickers = stickers[p.PackId].ToList() }).ToList();
    }

    // ------------------------------------------------------------------ hỗ trợ

    public static string Preview(MessageRow m)
    {
        if (m.DeletedAt != null) return "Tin nhắn đã được thu hồi";
        return m.Type switch
        {
            MessageType.Image => "[Hình ảnh]",
            MessageType.File => "[Tệp đính kèm]",
            MessageType.Sticker => "[Sticker]",
            _ => Truncate(m.Content ?? "", 120)
        };
    }

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public static void ValidateClientMessageId(string clientMessageId)
    {
        if (string.IsNullOrWhiteSpace(clientMessageId) || clientMessageId.Length > 64)
            throw new AppException("Thiếu clientMessageId (chuỗi Guid do client tạo, tối đa 64 ký tự).");
    }

    private long? ValidateReply(long? replyToId, long conversationId)
    {
        if (replyToId == null) return null;
        if (Db.Count("SELECT COUNT(*) FROM Messages WHERE MessageId = @replyToId AND ConversationId = @conversationId",
                new { replyToId, conversationId }) == 0)
            throw new AppException("Tin được trả lời không thuộc cuộc trò chuyện này.");
        return replyToId;
    }

    private static string ValidateContent(string? content)
    {
        var text = content?.Trim() ?? "";
        if (text.Length == 0) throw new AppException("Nội dung tin nhắn trống.");
        if (text.Length > MaxContentLength) throw new AppException($"Tin nhắn tối đa {MaxContentLength} ký tự.");
        return text;
    }
}

/// <summary>Thông báo: lưu vào CSDL và đẩy ngay nếu người nhận đang online.</summary>
public sealed class NotificationService
{
    private readonly ServerContext _ctx;

    public NotificationService(ServerContext ctx) => _ctx = ctx;
    private Db Db => _ctx.Db;

    public void Notify(long userId, string type, long? actorId, long? referenceId, string content)
    {
        var now = DateTime.UtcNow;
        content = MessageService.Truncate(content, 255);
        var id = Db.Insert(
            "INSERT INTO Notifications (UserId, ActorId, Type, ReferenceId, Content, IsRead, CreatedAt) VALUES (@userId, @actorId, @type, @referenceId, @content, 0, @now)",
            new { userId, actorId, type, referenceId, content, now });
        var actor = actorId == null ? null : _ctx.Users.Get(actorId.Value);
        _ctx.Sessions.SendToUser(userId, Events.Notification,
            new NotificationDto(id, type, referenceId, content, false, now, actor == null ? null : UserDto.From(actor)));
    }

    public PageResult<NotificationDto> List(long me, long? before, int limit)
    {
        limit = Math.Clamp(limit, 1, 100);
        var sql = "SELECT NotificationId, ActorId, Type, ReferenceId, Content, IsRead, CreatedAt FROM Notifications WHERE UserId = @me"
                  + (before != null ? " AND NotificationId < @before" : "")
                  + " ORDER BY NotificationId DESC" + Db.Limit("@take");
        var rows = Db.Query(sql, new { me, before, take = limit + 1 }, r => new
        {
            Id = r.Long("NotificationId"), ActorId = r.LongN("ActorId"), Type = r.Str("Type"), Ref = r.LongN("ReferenceId"),
            Content = r.StrN("Content"), IsRead = r.Bool("IsRead"), CreatedAt = r.Date("CreatedAt")
        });
        var actors = _ctx.Users.GetMany(rows.Where(x => x.ActorId != null).Select(x => x.ActorId!.Value));
        var items = rows.Take(limit).Select(x => new NotificationDto(x.Id, x.Type, x.Ref, x.Content, x.IsRead, x.CreatedAt,
            x.ActorId != null && actors.TryGetValue(x.ActorId.Value, out var a) ? UserDto.From(a) : null)).ToList();
        var unread = Db.Count("SELECT COUNT(*) FROM Notifications WHERE UserId = @me AND IsRead = 0", new { me });
        return new PageResult<NotificationDto>(items, rows.Count > limit) { UnreadCount = unread };
    }

    /// <summary>notificationId = null: đánh dấu tất cả.</summary>
    public void MarkRead(long me, long? notificationId)
    {
        if (notificationId == null)
            Db.Execute("UPDATE Notifications SET IsRead = 1 WHERE UserId = @me AND IsRead = 0", new { me });
        else
            Db.Execute("UPDATE Notifications SET IsRead = 1 WHERE UserId = @me AND NotificationId = @notificationId", new { me, notificationId });
    }

    public void MarkConversationRead(long me, long conversationId) =>
        Db.Execute("UPDATE Notifications SET IsRead = 1 WHERE UserId = @me AND IsRead = 0 AND Type = 'new_message' AND ReferenceId = @conversationId",
            new { me, conversationId });
}
