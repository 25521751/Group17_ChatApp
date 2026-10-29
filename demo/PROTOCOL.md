# Giao thức trao đổi dữ liệu Client – Server

Phần này là kết quả mục **"Xác định giao thức trao đổi dữ liệu giữa Client và Server"** (Giai đoạn 2) trong Timeline.

| Kênh | Cổng | Lớp .NET phía client | Dùng cho |
|---|---|---|---|
| TCP | 9000 | `TcpClient`, `NetworkStream`, `StreamReader`/`StreamWriter` | Mọi lệnh và sự kiện chat |
| HTTP | 8080 | `WebClient` (`UploadData`, `DownloadFile`) hoặc `HttpClient` | Gửi/tải tệp, ảnh đại diện |
| UDP | 9001 | `UdpClient` (broadcast) | Tìm server trong mạng LAN |
| UDP | 9002 | `UdpClient` | Âm thanh, hình ảnh khi gọi |

## 1. TCP – gói tin JSON theo dòng

Mỗi gói tin là **một dòng JSON UTF-8 kết thúc bằng `\n`**: phía gửi dùng `StreamWriter.WriteLine` rồi `Flush`, phía nhận dùng `StreamReader.ReadLine`. Nhờ vậy không phải tự cắt gói như khi gửi mảng byte thô.

Có ba loại gói:

```jsonc
// Client → Server: yêu cầu. "id" do client tự đánh số tăng dần.
{"cmd":"LOGIN","id":1,"data":{"login":"alice","password":"123456"}}

// Server → Client: trả lời đúng "id" đó
{"cmd":"LOGIN","id":1,"ok":true,"data":{"token":"9f2c...","user":{...}}}
{"cmd":"LOGIN","id":1,"ok":false,"error":"Tên đăng nhập hoặc mật khẩu không đúng."}

// Server → Client: sự kiện server tự đẩy xuống, không có "id"
{"event":"MESSAGE_NEW","data":{...}}
```

Client cần một luồng chạy nền đọc liên tục: gói có `"event"` thì xử lý như sự kiện; gói có `"id"` thì trao kết quả cho lệnh đang chờ (xem `ChatClientDemo/ChatConnection.cs`).

### Bắt tay và mã hoá (tuỳ chọn, bắt buộc nếu `RequireEncryption = true`)

```
Server → {"event":"HELLO","data":{"serverName":..., "publicKey":"<RSAKeyValue>...", "encryptionRequired":false, "httpPort":8080, "mediaPort":9002}}
Client:  tạo khoá AES-256 ngẫu nhiên (32 byte)
         mã hoá bằng khoá công khai: RSA.FromXmlString(publicKey); rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA1)
         (.NET Framework: RSACryptoServiceProvider.Encrypt(key, true))
Client → {"cmd":"SECURE","id":1,"data":{"key":"<Base64>"}}
Server → {"cmd":"SECURE","id":1,"ok":true,...}          ← dòng cuối cùng chưa mã hoá
Từ đây, mỗi dòng hai chiều = Base64( IV 16 byte ngẫu nhiên + AES-256-CBC-PKCS7(JSON) )
```

Đây là mô hình **khoá phiên** của chương 10: RSA (bất đối xứng) chỉ dùng để trao khoá, còn dữ liệu được mã hoá bằng AES (đối xứng, nhanh).

### Giữ kết nối

Server ngắt kết nối nếu 90 giây không nhận được gói nào. Client gửi `{"cmd":"PING","id":..}` khoảng 30 giây một lần. Khi mất kết nối, client kết nối lại, gửi `LOGIN_TOKEN` với token cũ (không cần nhập lại mật khẩu), rồi gửi lại các tin chưa có trả lời **với `clientMessageId` cũ**.

## 2. Danh sách lệnh (`cmd`)

`*` = không cần đăng nhập. Kiểu dữ liệu trả về xem phần 4.

| Lệnh | `data` gửi lên | Trả về |
|---|---|---|
| `PING`* | – | `{pong}` |
| `SECURE`* | `{key}` | `{cipher}` |
| `REGISTER`* | `{username, email, password, displayName, deviceInfo?}` | `LoginResult` |
| `LOGIN`* | `{login (tên hoặc email), password, deviceInfo?}` | `LoginResult` |
| `LOGIN_TOKEN`* | `{token}` | `LoginResult` |
| `FORGOT_PASSWORD`* | `{email}` | – (gửi mã 6 số qua email) |
| `RESET_PASSWORD`* | `{email, code, newPassword}` | – |
| `LOGOUT` | – | – |
| `CHANGE_PASSWORD` | `{currentPassword, newPassword}` | – (các thiết bị khác bị đăng xuất) |
| `ME` | – | `{user, email, createdAt}` |
| `UPDATE_PROFILE` | `{displayName, bio?, dateOfBirth? "yyyy-MM-dd"}` | `UserDto` |
| `GET_USER` | `{userId}` | `UserDto` |
| `SEARCH_USERS` | `{q}` | `[{user, relationship: friend/request_sent/request_received/none, requestId?}]` |
| `FRIEND_LIST` | – | `UserDto[]` |
| `FRIEND_REQUESTS` | – | `{incoming: FriendRequestDto[], outgoing: [...]}` |
| `FRIEND_REQUEST` | `{receiverId}` | `FriendRequestDto` (tự chấp nhận nếu người kia đã mời mình) |
| `FRIEND_ACCEPT` / `FRIEND_REJECT` / `FRIEND_CANCEL` | `{requestId}` | `FriendRequestDto` |
| `UNFRIEND` | `{userId}` | – |
| `CONV_LIST` | – | `ConversationDto[]` (mới nhất trước) |
| `CONV_GET` | `{conversationId}` | `ConversationDto` |
| `CONV_DIRECT` | `{userId}` | `ConversationDto` (mở hoặc tạo chat 1-1, phải là bạn bè) |
| `CONV_MUTE` | `{conversationId, isMuted}` | – |
| `GROUP_CREATE` | `{name, memberIds: [..]}` | `ConversationDto` |
| `GROUP_RENAME` | `{conversationId, name}` | `ConversationDto` (trưởng/phó nhóm) |
| `GROUP_ADD` | `{conversationId, userIds: [..]}` | `ConversationDto` (trưởng/phó nhóm) |
| `GROUP_REMOVE` | `{conversationId, userId}` | – |
| `GROUP_LEAVE` | `{conversationId}` | – (trưởng nhóm rời thì quyền chuyển cho người khác) |
| `GROUP_ROLE` | `{conversationId, userId, role: owner/admin/member}` | `ConversationDto` (chỉ trưởng nhóm) |
| `HISTORY` | `{conversationId, before?, limit? (30)}` | `{items: MessageDto[], hasMore}` |
| `SEND` | `{conversationId, clientMessageId, type: text/sticker, content?, stickerId?, replyToId?}` | `MessageDto` |
| `EDIT` | `{messageId, content}` | `MessageDto` |
| `RECALL` | `{messageId}` | `MessageDto` (`isDeleted = true`) |
| `REACT` / `UNREACT` | `{messageId, emoji}` | `ReactionDto[]` |
| `READ` | `{conversationId, messageId}` | – |
| `TYPING` | `{conversationId, isTyping}` | – |
| `STICKERS` | – | `StickerPackDto[]` |
| `NOTIFICATIONS` | `{before?, limit?}` | `{items: NotificationDto[], hasMore, unreadCount}` |
| `NOTIFICATION_READ` | `{notificationId?}` (bỏ trống = tất cả) | – |
| `CALL_START` | `{conversationId, type: voice/video}` | `{call: CallDto, media: {udpPort, mediaToken}}` |
| `CALL_ACCEPT` | `{callId}` | `{call, media}` |
| `CALL_REJECT` / `CALL_LEAVE` | `{callId}` | `CallDto` |
| `CALL_HISTORY` | `{conversationId, limit?}` | `CallDto[]` |

## 3. Sự kiện server đẩy xuống (`event`)

| Sự kiện | `data` | Khi nào |
|---|---|---|
| `HELLO` | thông tin server, khoá công khai | Ngay khi kết nối |
| `MESSAGE_NEW` | `MessageDto` | Có tin mới (kể cả tin của chính mình, để đồng bộ nhiều thiết bị) |
| `MESSAGE_UPDATED` | `MessageDto` | Tin bị sửa / thu hồi |
| `REACTIONS_UPDATED` | `{conversationId, messageId, reactions}` | Có người thả/bỏ cảm xúc |
| `READ_RECEIPT` | `{conversationId, userId, messageId}` | Ai đó đã đọc tới tin này |
| `TYPING` | `{conversationId, userId, isTyping}` | Đang nhập |
| `PRESENCE` | `{userId, isOnline, lastSeenAt}` | Bạn bè online/offline |
| `USER_UPDATED` | `UserDto` | Bạn bè đổi tên/ảnh |
| `NOTIFICATION` | `NotificationDto` | Lời mời kết bạn, được chấp nhận, được thêm vào nhóm, cuộc gọi nhỡ |
| `FRIEND_REQUEST_RECEIVED` / `FRIEND_REQUEST_UPDATED` | `FriendRequestDto` | |
| `FRIEND_ADDED` / `FRIEND_REMOVED` | `UserDto` / `{userId}` | |
| `CONV_CREATED` / `CONV_UPDATED` | `{conversationId}` | Gửi `CONV_GET` để lấy bản mới |
| `CONV_REMOVED` | `{conversationId}` | Bị xoá khỏi nhóm / đã rời nhóm |
| `CALL_INCOMING` | `CallDto` | Có cuộc gọi đến → `CALL_ACCEPT` hoặc `CALL_REJECT` |
| `CALL_UPDATED` / `CALL_ENDED` | `CallDto` | |
| `KICKED` | `{reason}` | Bị ngắt (đổi mật khẩu ở máy khác, quản trị viên, server tắt) |

### Gửi tin an toàn khi mạng chập chờn

1. Client tạo `clientMessageId = Guid.NewGuid().ToString()` và hiện tin ở trạng thái "đang gửi".
2. Gửi `SEND`. Nhận `ok:true` → đổi thành "đã gửi".
3. Sự kiện `MESSAGE_NEW` cũng quay về cho chính người gửi. So `clientMessageId` để không hiện trùng.
4. Hết thời gian chờ / mất mạng → hiện "gửi lỗi"; khi kết nối lại thì gửi lại **cùng `clientMessageId`**. Server thấy đã lưu rồi sẽ trả về tin cũ thay vì lưu thêm bản mới.

## 4. Kiểu dữ liệu

Thời gian là chuỗi ISO 8601 theo UTC (`"2026-10-29T15:20:11.123Z"`); client đổi sang giờ máy bằng `ToLocalTime()`.

```jsonc
UserDto          {userId, username, displayName, avatarUrl?, bio?, dateOfBirth?, isOnline, lastSeenAt?, isBot}
LoginResult      {token, expiresAt, user: UserDto, email}
FriendRequestDto {requestId, sender: UserDto, receiver: UserDto, status, createdAt, respondedAt?}
ConversationDto  {conversationId, type: direct/group, name, avatarUrl?, createdBy, myRole, isMuted,
                  unreadCount, lastMessageAt, lastMessage?: MessageDto, otherUser?: UserDto, members: MemberDto[]}
MemberDto        {userId, username, displayName, avatarUrl?, isOnline, lastSeenAt?, isBot, role, nickname?, joinedAt, lastReadMessageId?}
MessageDto       {messageId, conversationId, clientMessageId, sender?: UserDto, type: text/image/file/sticker/call/system,
                  content?, sticker?: {stickerId, packId, imageUrl, keyword}, replyTo?: {messageId, senderId, senderName, type, content, isDeleted},
                  attachments: [{attachmentId, fileName, mimeType, fileSize, url: "/files/{id}"}],
                  reactions: [{emoji, count, userIds}], createdAt, editedAt?, isDeleted}
CallDto          {callId, conversationId, callerId, type, status: ringing/ongoing/ended/missed/rejected/failed,
                  startedAt, answeredAt?, endedAt?, participants: [{userId, displayName, avatarUrl, status, joinedAt?, leftAt?}]}
NotificationDto  {notificationId, type, referenceId, content, isRead, createdAt, actor?: UserDto}
```

`referenceId` của thông báo: `friend_request`/`friend_accepted` → requestId; `added_to_group`/`new_message`/`missed_call` → conversationId.

## 5. HTTP (cổng 8080)

Xác thực bằng token nhận từ `LOGIN`: header `Authorization: Bearer <token>` hoặc `?token=<token>`.

| Yêu cầu | Mô tả |
|---|---|
| `POST /upload?conversationId=&clientMessageId=&fileName=[&caption=&replyToId=]` | Thân request = **nội dung tệp** (`WebClient.UploadData(url, "POST", bytes)`). Trả về `MessageDto` (ảnh → `type: image`, còn lại `file`); mọi thành viên nhận `MESSAGE_NEW` qua TCP. |
| `GET /files/{attachmentId}` | Tải tệp (`WebClient.DownloadFile`). Chỉ thành viên đang ở trong hội thoại. |
| `POST /avatar?fileName=` | Đổi ảnh đại diện (ảnh, ≤ 5 MB). Trả về `UserDto`. |
| `POST /group-avatar?conversationId=&fileName=` | Đổi ảnh nhóm (trưởng/phó nhóm). |
| `GET /avatars/{tên}`, `GET /stickers/{tên}` | Ảnh công khai, không cần token. `avatarUrl` / `imageUrl` là đường dẫn tương đối, ghép với `http://<server>:8080`. |
| `GET /` | Trang trạng thái server (HTML). |
| `GET /api/status` | Trạng thái dạng JSON. |

Lỗi trả về `{"status":400,"error":"..."}` với mã 400/401/404/413/500.

## 6. UDP – tìm server (cổng 9001)

Client gửi chuỗi `CHAT_DISCOVER` tới `255.255.255.255:9001` (`UdpClient.EnableBroadcast = true`). Server trả về:

```json
{"serverName":"ChatApp Server - NT106","tcpPort":9000,"httpPort":8080,"mediaPort":9002,"online":3}
```

Địa chỉ IP của server lấy từ `remoteEndPoint` của gói trả lời.

## 7. UDP – âm thanh / hình ảnh cuộc gọi (cổng 9002)

Server chỉ **chuyển tiếp** gói của một người tới những người khác trong cùng cuộc gọi, không xử lý nội dung. Số nguyên 8 byte theo **thứ tự byte mạng** (big-endian): `IPAddress.HostToNetworkOrder` khi gửi, `IPAddress.NetworkToHostOrder` khi nhận.

```
Client → Server                         Server → Client
[0]      loại gói                        [0]      loại gói
[1..8]   callId                          [1..8]   callId
[9..24]  mediaToken (16 byte)            [9..16]  userId người gửi
[25..]   dữ liệu                         [17..]   dữ liệu

Loại: 1 = JOIN/giữ kết nối (server trả lại gói loại 1 để xác nhận)
      2 = âm thanh (PCM 16-bit mono, gợi ý 16 kHz, mỗi gói 20-40 ms)
      3 = hình ảnh (một khung JPEG, nên < 60 KB)
      4 = nhạc nền phát trong cuộc gọi nhóm
```

Luồng một cuộc gọi:

```
A: CALL_START {conversationId, "voice"} → {call, media:{udpPort:9002, mediaToken}}    B nhận CALL_INCOMING
A: gửi UDP loại 1 (và 5 giây một lần)
B: CALL_ACCEPT {callId} → {call, media}                                                A, B nhận CALL_UPDATED (ongoing)
B: gửi UDP loại 1
A, B: gửi UDP loại 2/3 liên tục, server chuyển cho người kia
A hoặc B: CALL_LEAVE {callId}                                                          cả hai nhận CALL_ENDED
```

Không ai nghe sau 45 giây thì cuộc gọi thành "nhỡ": người được gọi nhận thông báo `missed_call`, lịch sử chat có dòng "Cuộc gọi thoại nhỡ".
