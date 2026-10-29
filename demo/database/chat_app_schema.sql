-- =====================================================================
--  CƠ SỞ DỮ LIỆU ỨNG DỤNG CHAT (Client–Server)
--  Dựa trên Timeline đồ án: tài khoản, kết bạn, nhắn tin thời gian thực,
--  nhóm chat, tệp đính kèm/sticker, gọi thoại/video, thông báo, phân quyền.
--  Hệ quản trị: Microsoft SQL Server (T-SQL)
-- =====================================================================

CREATE DATABASE ChatApp;
GO
USE ChatApp;
GO

-- ---------------------------------------------------------------------
-- 1. NGƯỜI DÙNG  (Giai đoạn 3 + 5: đăng ký, đăng nhập, hồ sơ, ảnh đại diện)
-- ---------------------------------------------------------------------
CREATE TABLE Users (
    UserId          BIGINT IDENTITY(1,1) PRIMARY KEY,
    Username        VARCHAR(50)    NOT NULL UNIQUE,
    Email           VARCHAR(255)   NOT NULL UNIQUE,
    PasswordHash    VARCHAR(255)   NOT NULL,          -- bcrypt/PBKDF2, KHÔNG lưu mật khẩu gốc
    DisplayName     NVARCHAR(100)  NOT NULL,
    AvatarUrl       VARCHAR(500)   NULL,
    Bio             NVARCHAR(500)  NULL,
    DateOfBirth     DATE           NULL,
    IsBot           BIT            NOT NULL DEFAULT 0, -- dùng cho Chatbot (tính năng bổ sung)
    IsOnline        BIT            NOT NULL DEFAULT 0,
    LastSeenAt      DATETIME2      NULL,
    CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_Users_DisplayName ON Users(DisplayName);   -- tìm kiếm người dùng

-- Phiên đăng nhập (đăng nhập / đăng xuất, nhiều thiết bị)
CREATE TABLE UserSessions (
    SessionId       BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId          BIGINT         NOT NULL REFERENCES Users(UserId) ON DELETE CASCADE,
    TokenHash       VARCHAR(255)   NOT NULL UNIQUE,
    DeviceInfo      NVARCHAR(200)  NULL,
    IpAddress       VARCHAR(45)    NULL,
    CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt       DATETIME2      NOT NULL,
    RevokedAt       DATETIME2      NULL                -- khác NULL = đã đăng xuất
);
CREATE INDEX IX_UserSessions_User ON UserSessions(UserId);

-- ---------------------------------------------------------------------
-- 2. BẠN BÈ  (Giai đoạn 3: lời mời, chấp nhận/từ chối, danh sách bạn bè)
-- ---------------------------------------------------------------------
CREATE TABLE FriendRequests (
    RequestId       BIGINT IDENTITY(1,1) PRIMARY KEY,
    SenderId        BIGINT         NOT NULL REFERENCES Users(UserId),
    ReceiverId      BIGINT         NOT NULL REFERENCES Users(UserId),
    Status          VARCHAR(10)    NOT NULL DEFAULT 'pending'
                    CHECK (Status IN ('pending','accepted','rejected','cancelled')),
    CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    RespondedAt     DATETIME2      NULL,
    CONSTRAINT CK_FriendRequests_NotSelf CHECK (SenderId <> ReceiverId)
);
-- Chỉ một lời mời đang chờ cho mỗi cặp (chống trùng lặp)
CREATE UNIQUE INDEX UX_FriendRequests_Pending
    ON FriendRequests(SenderId, ReceiverId) WHERE Status = 'pending';
CREATE INDEX IX_FriendRequests_Receiver ON FriendRequests(ReceiverId, Status);

-- Quan hệ bạn bè: lưu 1 dòng/cặp, quy ước UserId1 < UserId2
CREATE TABLE Friendships (
    UserId1         BIGINT         NOT NULL REFERENCES Users(UserId),
    UserId2         BIGINT         NOT NULL REFERENCES Users(UserId),
    CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Friendships PRIMARY KEY (UserId1, UserId2),
    CONSTRAINT CK_Friendships_Order CHECK (UserId1 < UserId2)
);
CREATE INDEX IX_Friendships_User2 ON Friendships(UserId2);

-- ---------------------------------------------------------------------
-- 3. HỘI THOẠI  (Giai đoạn 4 + 5: chat 1-1, nhóm chat, quyền quản trị)
-- ---------------------------------------------------------------------
CREATE TABLE Conversations (
    ConversationId  BIGINT IDENTITY(1,1) PRIMARY KEY,
    Type            VARCHAR(10)    NOT NULL CHECK (Type IN ('direct','group')),
    Name            NVARCHAR(100)  NULL,               -- chỉ dùng cho nhóm
    AvatarUrl       VARCHAR(500)   NULL,
    CreatedBy       BIGINT         NOT NULL REFERENCES Users(UserId),
    DirectKey       VARCHAR(50)    NULL,               -- 'minId_maxId' cho chat 1-1
    LastMessageAt   DATETIME2      NULL,               -- sắp xếp danh sách hội thoại
    CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
-- Mỗi cặp người dùng chỉ có 1 hội thoại 1-1
CREATE UNIQUE INDEX UX_Conversations_DirectKey
    ON Conversations(DirectKey) WHERE DirectKey IS NOT NULL;

CREATE TABLE ConversationMembers (
    ConversationId  BIGINT         NOT NULL REFERENCES Conversations(ConversationId) ON DELETE CASCADE,
    UserId          BIGINT         NOT NULL REFERENCES Users(UserId),
    Role            VARCHAR(10)    NOT NULL DEFAULT 'member'
                    CHECK (Role IN ('owner','admin','member')),
    Nickname        NVARCHAR(100)  NULL,
    AddedBy         BIGINT         NULL REFERENCES Users(UserId),
    JoinedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    LeftAt          DATETIME2      NULL,               -- khác NULL = đã rời/bị xoá khỏi nhóm
    LastReadMessageId BIGINT       NULL,               -- đánh dấu đã đọc, đếm tin chưa đọc
    IsMuted         BIT            NOT NULL DEFAULT 0, -- tắt thông báo
    CONSTRAINT PK_ConversationMembers PRIMARY KEY (ConversationId, UserId)
);
CREATE INDEX IX_ConversationMembers_User ON ConversationMembers(UserId);

-- ---------------------------------------------------------------------
-- 4. TIN NHẮN  (Giai đoạn 4 + 5: lưu tin, lịch sử, gửi thất bại, emoji/sticker/tệp)
-- ---------------------------------------------------------------------
CREATE TABLE StickerPacks (
    PackId          INT IDENTITY(1,1) PRIMARY KEY,
    Name            NVARCHAR(100)  NOT NULL,
    ThumbnailUrl    VARCHAR(500)   NULL
);

CREATE TABLE Stickers (
    StickerId       INT IDENTITY(1,1) PRIMARY KEY,
    PackId          INT            NOT NULL REFERENCES StickerPacks(PackId) ON DELETE CASCADE,
    ImageUrl        VARCHAR(500)   NOT NULL,
    Keyword         NVARCHAR(50)   NULL
);

CREATE TABLE Messages (
    MessageId       BIGINT IDENTITY(1,1) PRIMARY KEY,
    ConversationId  BIGINT         NOT NULL REFERENCES Conversations(ConversationId) ON DELETE CASCADE,
    SenderId        BIGINT         NULL REFERENCES Users(UserId),   -- NULL = tin hệ thống
    ClientMessageId VARCHAR(64)    NOT NULL,           -- UUID do client sinh: gửi lại không bị trùng
    Type            VARCHAR(10)    NOT NULL DEFAULT 'text'
                    CHECK (Type IN ('text','image','file','sticker','call','system')),
    Content         NVARCHAR(MAX)  NULL,               -- văn bản + emoji (Unicode)
    StickerId       INT            NULL REFERENCES Stickers(StickerId),
    ReplyToId       BIGINT         NULL REFERENCES Messages(MessageId),
    CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    EditedAt        DATETIME2      NULL,
    DeletedAt       DATETIME2      NULL                -- thu hồi (xoá mềm)
);
-- Lấy lịch sử trò chuyện theo trang (mới nhất trước)
CREATE INDEX IX_Messages_Conversation ON Messages(ConversationId, MessageId DESC);
-- Chống lưu trùng khi client gửi lại sau mất kết nối
CREATE UNIQUE INDEX UX_Messages_ClientId ON Messages(SenderId, ClientMessageId)
    WHERE SenderId IS NOT NULL;

ALTER TABLE ConversationMembers
    ADD CONSTRAINT FK_Members_LastRead FOREIGN KEY (LastReadMessageId) REFERENCES Messages(MessageId);

CREATE TABLE Attachments (
    AttachmentId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    MessageId       BIGINT         NOT NULL REFERENCES Messages(MessageId) ON DELETE CASCADE,
    UploaderId      BIGINT         NOT NULL REFERENCES Users(UserId),
    FileName        NVARCHAR(255)  NOT NULL,
    StoragePath     VARCHAR(500)   NOT NULL,           -- đường dẫn trên server, không public trực tiếp
    MimeType        VARCHAR(100)   NOT NULL,
    FileSize        BIGINT         NOT NULL,
    Width           INT            NULL,               -- cho ảnh
    Height          INT            NULL,
    CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_Attachments_Message ON Attachments(MessageId);

CREATE TABLE MessageReactions (
    MessageId       BIGINT         NOT NULL REFERENCES Messages(MessageId) ON DELETE CASCADE,
    UserId          BIGINT         NOT NULL REFERENCES Users(UserId),
    Emoji           NVARCHAR(16)   NOT NULL,
    CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MessageReactions PRIMARY KEY (MessageId, UserId, Emoji)
);

-- ---------------------------------------------------------------------
-- 5. CUỘC GỌI  (Giai đoạn 5: gọi thoại, gọi video, gọi nhóm)
-- ---------------------------------------------------------------------
CREATE TABLE Calls (
    CallId          BIGINT IDENTITY(1,1) PRIMARY KEY,
    ConversationId  BIGINT         NOT NULL REFERENCES Conversations(ConversationId) ON DELETE CASCADE,
    CallerId        BIGINT         NOT NULL REFERENCES Users(UserId),
    Type            VARCHAR(10)    NOT NULL CHECK (Type IN ('voice','video')),
    Status          VARCHAR(10)    NOT NULL DEFAULT 'ringing'
                    CHECK (Status IN ('ringing','ongoing','ended','missed','rejected','failed')),
    StartedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    AnsweredAt      DATETIME2      NULL,
    EndedAt         DATETIME2      NULL
);
CREATE INDEX IX_Calls_Conversation ON Calls(ConversationId, StartedAt DESC);

CREATE TABLE CallParticipants (
    CallId          BIGINT         NOT NULL REFERENCES Calls(CallId) ON DELETE CASCADE,
    UserId          BIGINT         NOT NULL REFERENCES Users(UserId),
    Status          VARCHAR(10)    NOT NULL DEFAULT 'invited'
                    CHECK (Status IN ('invited','joined','declined','missed','left')),
    JoinedAt        DATETIME2      NULL,
    LeftAt          DATETIME2      NULL,
    CONSTRAINT PK_CallParticipants PRIMARY KEY (CallId, UserId)
);

-- ---------------------------------------------------------------------
-- 6. THÔNG BÁO  (Giai đoạn 5: tin nhắn mới, lời mời kết bạn, cuộc gọi nhỡ...)
-- ---------------------------------------------------------------------
CREATE TABLE Notifications (
    NotificationId  BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId          BIGINT         NOT NULL REFERENCES Users(UserId) ON DELETE CASCADE, -- người nhận
    ActorId         BIGINT         NULL REFERENCES Users(UserId),                       -- người gây ra
    Type            VARCHAR(20)    NOT NULL
                    CHECK (Type IN ('new_message','friend_request','friend_accepted',
                                    'added_to_group','missed_call')),
    ReferenceId     BIGINT         NULL,   -- MessageId / RequestId / ConversationId / CallId tuỳ Type
    Content         NVARCHAR(255)  NULL,
    IsRead          BIT            NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_Notifications_User ON Notifications(UserId, IsRead, CreatedAt DESC);
GO

-- =====================================================================
-- TRUY VẤN MẪU
-- =====================================================================
-- Danh sách bạn bè của @me:
--   SELECT u.* FROM Friendships f
--   JOIN Users u ON u.UserId = CASE WHEN f.UserId1 = @me THEN f.UserId2 ELSE f.UserId1 END
--   WHERE @me IN (f.UserId1, f.UserId2);
--
-- 30 tin nhắn trước tin @beforeId trong hội thoại @cid (cuộn lên xem lịch sử):
--   SELECT TOP 30 * FROM Messages
--   WHERE ConversationId = @cid AND MessageId < @beforeId AND DeletedAt IS NULL
--   ORDER BY MessageId DESC;
--
-- Số tin chưa đọc của @me trong từng hội thoại:
--   SELECT m.ConversationId, COUNT(*) AS Unread
--   FROM ConversationMembers cm
--   JOIN Messages m ON m.ConversationId = cm.ConversationId
--                  AND m.MessageId > ISNULL(cm.LastReadMessageId, 0)
--                  AND m.SenderId <> @me
--   WHERE cm.UserId = @me AND cm.LeftAt IS NULL
--   GROUP BY m.ConversationId;
