# ChatApp Server – Lập trình mạng căn bản (NT106)

Server cho đồ án ứng dụng chat, viết bằng **C# (.NET 8)** và chỉ dùng các lớp mạng học trong môn: `TcpListener`/`TcpClient`, `Thread`, `NetworkStream` + `StreamReader`/`StreamWriter`, `UdpClient`, `HttpListener`, `WebClient`, `SmtpClient`, `RSA`/`Aes`. Không dùng ASP.NET hay SignalR, nên mọi bước kết nối, đóng gói và gửi nhận đều nằm trong code, dễ giải thích khi báo cáo.

```
NT106Chat/
├── ChatSocketServer/      server (console app)
├── ChatClientDemo/        client console để thử + lớp ChatConnection dùng lại cho WinForms
├── database/              chat_app_schema.sql (SQL Server)
├── PROTOCOL.md            giao thức Client–Server (lệnh, sự kiện, định dạng gói)
└── README.md
```

## Kiến thức từng chương được dùng ở đâu

| Chương | Nội dung | Trong project |
|---|---|---|
| 2 – I/O | Stream, FileStream, StreamReader/Writer, Encoding UTF-8, Serialization | Gói tin JSON đọc/ghi bằng `ReadLine`/`WriteLine` (`Net/ClientSession.cs`); lưu tệp bằng `FileStream` (`Services/FileStore.cs`); `System.Text.Json` |
| 3 – Sockets | IPAddress, IPEndPoint, Dns, TcpListener/TcpClient, UdpClient, so sánh TCP–UDP | Chat qua TCP 9000 (`Net/TcpChatServer.cs`); tìm server bằng UDP broadcast 9001; âm thanh/hình gọi qua UDP 9002 (`Net/UdpServices.cs`); `IPAddress.HostToNetworkOrder`; in IP máy bằng `Dns.GetHostEntry` |
| 3B – Thread | Đa luồng, lock | Mỗi client một `Thread`; `lock` khi ghi socket và khi đổi trạng thái cuộc gọi; `ThreadPool` cho HTTP, chatbot, email |
| 4 – Web server | HTTP request/response, header, mã trạng thái, HttpListener, WebClient | `Net/HttpFileServer.cs` (HttpListener) để tải tệp lên/xuống và hiện trang trạng thái; client dùng `WebClient.UploadData` / `DownloadFile` |
| 5 – Email | SMTP, SmtpClient, MailMessage | Quên mật khẩu: gửi mã 6 số qua email (`EmailService`) |
| 6 – File server | Truyền tệp | Gửi ảnh/tệp qua HTTP, kiểm tra quyền trước khi cho tải |
| 7, 10 – Bảo mật, mã hoá | Băm, mã hoá đối xứng/bất đối xứng, khoá phiên | Mật khẩu băm PBKDF2-SHA256 có salt; kênh TCP mã hoá RSA-2048 trao khoá + AES-256 (`Security/Crypto.cs`) |

## Đối chiếu với Timeline

| Giai đoạn | Việc | Lệnh / thành phần |
|---|---|---|
| 2 | Kiến trúc Client–Server, giao thức trao đổi | `PROTOCOL.md` |
| 3 | Đăng ký, đăng nhập, đăng xuất, lưu tài khoản | `REGISTER`, `LOGIN`, `LOGOUT`, bảng `Users`, `UserSessions` |
| 3 | Tìm người dùng, lời mời kết bạn, danh sách bạn bè | `SEARCH_USERS`, `FRIEND_*`, `FRIEND_LIST` |
| 4 | Kết nối, gửi/nhận realtime, lưu tin, lịch sử | TCP + `SEND` / `MESSAGE_NEW`, `HISTORY` |
| 4 | Mất kết nối, gửi thất bại | `clientMessageId` chống trùng, `LOGIN_TOKEN` để kết nối lại, PING + timeout 90 giây, tự rời cuộc gọi |
| 5 | Nhóm chat, thêm thành viên, quyền quản trị | `GROUP_*`, vai trò owner/admin/member |
| 5 | Ảnh, sticker, emoji, tệp | HTTP `/upload`, `type: sticker`, `REACT` |
| 5 | Gọi thoại, gọi video | `CALL_*` qua TCP + chuyển tiếp UDP |
| 5 | Thông báo | `NOTIFICATION`, lưu cho người offline |
| 5 | Quyền truy cập tệp | `/files/{id}` chỉ cho thành viên còn trong nhóm |
| 5 | Sửa hồ sơ, ảnh đại diện | `UPDATE_PROFILE`, HTTP `/avatar` |
| 6 | Kiểm thử nhiều tài khoản đồng thời | Mở nhiều `ChatClientDemo`; trang `http://localhost:8080/` hiện mọi kết nối |
| Bổ sung | Chatbot | Tài khoản `chatbot`, tự là bạn của mọi người dùng mới |
| Bổ sung | Phát nhạc trong call nhóm | Gói UDP loại 4 (nhạc nền) |

## Chạy server

Cần **.NET 8 SDK** (Visual Studio 2022 đã có sẵn).

```bash
cd ChatSocketServer
dotnet run
```

Hoặc mở `ChatSocketServer.csproj` bằng Visual Studio rồi nhấn F5. Server in ra:

```
[21:30:00] Cơ sở dữ liệu: Sqlite
[21:30:00] TCP chat server đang lắng nghe cổng 9000
[21:30:00] HTTP server (HttpListener) đang chạy: http://+:8080/
[21:30:00] UDP discovery đang nghe cổng 9001
[21:30:00] UDP media relay đang nghe cổng 9002
[21:30:00] Máy chủ: LAPTOP-NHOM1 | IP: 192.168.1.10
```

- Lần đầu chạy, server tự tạo `data/chatapp.db` (SQLite), bộ sticker, tài khoản `chatbot` và 3 tài khoản mẫu **alice / bob / carol, mật khẩu `123456`**. Ba tài khoản này đã là bạn bè và có sẵn nhóm "Nhóm đồ án NT106".
- Mở http://localhost:8080/ để xem trang trạng thái: số người online, các kết nối, nhật ký, và chính HTTP request trình duyệt vừa gửi.
- Lệnh gõ vào cửa sổ server: `status`, `sessions`, `kick <username>`, `commands`, `quit`.

### Cho máy khác trong LAN kết nối (Windows)

1. Mở cổng trên tường lửa (PowerShell quyền admin):
   ```powershell
   New-NetFirewallRule -DisplayName "ChatApp" -Direction Inbound -Protocol TCP -LocalPort 9000,8080 -Action Allow
   New-NetFirewallRule -DisplayName "ChatApp UDP" -Direction Inbound -Protocol UDP -LocalPort 9001,9002 -Action Allow
   ```
2. `HttpListener` cần quyền để nghe trên mọi địa chỉ. Chạy một lần (quyền admin):
   ```
   netsh http add urlacl url=http://+:8080/ user=Everyone
   ```
   Nếu chưa chạy lệnh này, server vẫn chạy nhưng HTTP chỉ nhận từ chính máy đó (in cảnh báo màu vàng).

## Dùng SQL Server thay cho SQLite

1. Chạy `database/chat_app_schema.sql` trong SSMS (tạo database `ChatApp` và 14 bảng).
2. Sửa `ChatSocketServer/appsettings.json`:
   ```json
   "DatabaseProvider": "SqlServer",
   "SqlServerConnection": "Server=localhost;Database=ChatApp;Trusted_Connection=True;TrustServerCertificate=True"
   ```
   Dùng tài khoản SQL: `Server=localhost;Database=ChatApp;User Id=sa;Password=...;TrustServerCertificate=True`. Với SQL Express: `Server=localhost\\SQLEXPRESS;...`.
3. `dotnet run`. Server tự thêm chatbot, sticker và tài khoản mẫu.

Truy cập CSDL viết bằng ADO.NET thuần (`DbConnection`, `DbCommand`, `DbDataReader`, tham số `@Ten`) trong `Data/Db.cs`, cùng một câu SQL chạy được trên cả hai hệ quản trị.

## Thử bằng client console

```bash
cd ChatClientDemo
dotnet run                    # tự tìm server bằng UDP broadcast
dotnet run -- 192.168.1.10    # hoặc chỉ định IP
```

Mở hai cửa sổ, đăng nhập `alice` và `bob`, gõ `/list`, `/open 1` rồi nhắn qua lại. Các lệnh khác: `/friends`, `/search`, `/add`, `/accept`, `/dm`, `/group`, `/sticker 1`, `/file C:\anh.png`, `/get <id> D:\luu.png`, `/call`, `/answer <callId>`, `/beep` (gửi thử âm thanh qua UDP), `/hangup`. Nhắn `/dm` với chatbot rồi gõ "giúp" để thử chatbot.

## Viết client WinForms

Chép `ChatClientDemo/ChatConnection.cs` vào project WinForms của nhóm:

```csharp
private readonly ChatConnection _chat = new();

private async void btnLogin_Click(object sender, EventArgs e)
{
    _chat.EventReceived += OnServerEvent;
    await Task.Run(() => _chat.Connect(txtServer.Text, 9000, secure: true));
    try
    {
        var result = await _chat.RequestAsync("LOGIN", new { login = txtUser.Text, password = txtPass.Text });
        _chat.Token = result.GetProperty("token").GetString();
    }
    catch (ChatException ex) { MessageBox.Show(ex.Message); }
}

// Sự kiện chạy trên luồng nền -> cập nhật giao diện qua Invoke (chương 2: InvokeRequired)
private void OnServerEvent(string evt, JsonElement data)
{
    if (evt != "MESSAGE_NEW") return;
    Invoke(() => lstMessages.Items.Add($"{data.GetProperty("sender").GetProperty("displayName")}: {data.GetProperty("content")}"));
}

private async void btnSend_Click(object sender, EventArgs e) =>
    await _chat.RequestAsync("SEND", new { conversationId = _currentId, clientMessageId = Guid.NewGuid().ToString(), type = "text", content = txtMessage.Text });
```

- Gửi ảnh/tệp: `_chat.UploadFile(conversationId, path)`. Tải tệp: `_chat.DownloadFile(attachmentId, savePath)`.
- Ảnh đại diện và sticker: `pictureBox.Load($"http://{server}:8080{avatarUrl}")`.
- Gọi thoại: lớp `MediaChannel` đã gửi/nhận gói UDP; thu và phát âm thanh dùng thư viện **NAudio** (`WaveInEvent` → `SendAudio`, `Received` → `BufferedWaveProvider` → `WaveOutEvent`). Gọi video: chụp webcam (AForge/OpenCvSharp), nén JPEG, gửi bằng `SendVideoFrame`.

## Cấu hình (`ChatSocketServer/appsettings.json`)

| Khoá | Mặc định | Ý nghĩa |
|---|---|---|
| `TcpPort` / `HttpPort` / `DiscoveryPort` / `MediaPort` | 9000 / 8080 / 9001 / 9002 | Các cổng |
| `DatabaseProvider` | `Sqlite` | `Sqlite` hoặc `SqlServer` |
| `RequireEncryption` | `false` | `true` = client phải gửi `SECURE` trước khi đăng nhập |
| `SeedDemoData` | `true` | Tạo alice/bob/carol |
| `MaxFileMB` | 25 | Dung lượng tệp tối đa |
| `ClientTimeoutSeconds` | 90 | Không nhận được gói nào trong khoảng này thì ngắt kết nối |
| `Smtp` | trống | Điền để gửi email thật (Gmail: `smtp.gmail.com`, cổng 587, mật khẩu ứng dụng). Để trống thì mã quên mật khẩu in ra cửa sổ server. |

## Cấu trúc server

```
ChatSocketServer/
├── Program.cs                 khởi động các thành phần, lệnh quản trị trên console
├── ServerContext.cs           nơi giữ các service
├── Core/Core.cs               cấu hình, log, JSON, đọc tham số lệnh
├── Net/
│   ├── Protocol.cs            định dạng gói tin, tên sự kiện
│   ├── TcpChatServer.cs       TcpListener + SessionManager (ai online, gửi tới ai)
│   ├── ClientSession.cs       luồng đọc/ghi của từng client, mã hoá AES
│   ├── CommandHandler.cs      bảng lệnh -> hàm xử lý
│   ├── HttpFileServer.cs      HttpListener: tệp, ảnh, trang trạng thái
│   └── UdpServices.cs         UDP discovery + chuyển tiếp âm thanh/hình
├── Security/Crypto.cs         PBKDF2, token, RSA + AES
├── Data/                      ADO.NET (Db.cs), model, tạo bảng SQLite + dữ liệu mẫu
├── Services/                  tài khoản, bạn bè, hội thoại, tin nhắn, thông báo, cuộc gọi, chatbot, email, lưu tệp
└── wwwroot/stickers/          ảnh sticker
```
