using Server.Chat_room_test_TCP;
using Server.ChatappZalu;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Chat_room_test_TCP
{
    public partial class ServerForm : Form
    {
        public ServerForm()
        {
            InitializeComponent();
        }

        private TcpListener server;
        private bool isRunning = false;
        private List<TcpClient> clients = new List<TcpClient>();
        private Dictionary<TcpClient, string> clientNames = new Dictionary<TcpClient, string>();

        private readonly object clientStateLock = new object();

        private readonly Dictionary<TcpClient, SemaphoreSlim> clientWriteLocks = new Dictionary<TcpClient, SemaphoreSlim>();

        private readonly Dictionary<TcpClient, UserSession> sessions = new Dictionary<TcpClient, UserSession>(); //Key — TcpClient: xác định kết nối TCP cụ thể của thiết bị.
                                                                                                                 //Value — UserSession: lưu tài khoản đang đăng nhập trên kết nối đó.
                                                                                                                 //Một tài khoản có thể đăng nhập từ nhiều thiết bị, vì mỗi thiết bị có một kết nối TCP riêng

        private readonly AccountService accountService = new AccountService(new InMemoryAccountRepository()); //ket noi account service vao server
        private async Task AcceptClientAsync()
        {
            while (isRunning)
            {
                TcpClient client;
                try
                {
                    client = await server.AcceptTcpClientAsync();
                }
                catch (SocketException ex)
                {
                    if (isRunning)
                    {
                        Server_ChatView.AppendText($"Socket error: {ex.Message}\r\n");
                    }
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                if (!isRunning)
                {
                    client.Close();
                    break;
                }
                //clients.Add(client);
                //ChatView.AppendText($"TCP connected: {client.Client.RemoteEndPoint}\r\n");
                _ = HandleClientAsync(client); //?
            }
        }



        private async Task HandleClientAsync(TcpClient client)
        {
            string name = null;
            bool joined = false;

            NetworkStream stream = null;
            SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

            lock (clientStateLock)
            {
                clientWriteLocks[client] = writeLock;
            }

            try
            {
                stream = client.GetStream();

                using (StreamReader reader = new StreamReader(
                    stream, Encoding.UTF8, false, 1024, true))
                using (StreamWriter writer = new StreamWriter(
                    stream, new UTF8Encoding(false), 1024, true))
                {
                    writer.AutoFlush = true;

                    string line;

                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        string[] parts = line.Split(
                            new char[] { '|' }, 2);

                        string command = parts[0];
                        string data = parts.Length > 1 ? parts[1] : "";

                        // REGISTER: username|email|password
                        if (command == "REGISTER")
                        {
                            string[] fields = data.Split('|');

                            if (fields.Length != 3)
                            {
                                await writer.WriteLineAsync(
                                    "REGISTER_RESULT|FAIL|INVALID_DATA");
                                continue;
                            }

                            string username = fields[0];
                            string email = fields[1];
                            string password = fields[2];

                            string result = accountService.Register(
                                username, email, password, username);

                            await writer.WriteLineAsync(
                                "REGISTER_RESULT|" + result);

                            LogServer(
                                $"Register request: {username} - " +
                                $"{(result.StartsWith("OK|") ? "Success" : "Failed")}");

                            continue;
                        }


                        // LOGIN: xác thực tài khoản

                        else if (command == "LOGIN")
                        {
                            string[] fields = data.Split('|');

                            if (fields.Length != 2)
                            {
                                await writer.WriteLineAsync(
                                    "LOGIN_RESULT|FAIL|INVALID_DATA");
                                continue;
                            }

                            string username = fields[0];
                            string password = fields[1];

                            // Không cho đăng nhập lại trên cùng một kết nối.
                            bool alreadyLoggedIn;

                            lock (clientStateLock)
                            {
                                alreadyLoggedIn = sessions.ContainsKey(client);
                            }

                            if (alreadyLoggedIn)
                            {
                                await writer.WriteLineAsync(
                                    "LOGIN_RESULT|FAIL|ALREADY_LOGGED_IN");
                                continue;
                            }

                            // Kiểm tra tài khoản và mật khẩu.
                            string result = accountService.Login(username, password);

                            if (result.StartsWith("OK|"))
                            {
                                string[] accountFields = result.Split('|');

                                if (accountFields.Length < 3)
                                {
                                    await writer.WriteLineAsync(
                                        "LOGIN_RESULT|FAIL|INVALID_ACCOUNT_DATA");
                                    continue;
                                }

                                string authenticatedUsername = accountFields[1];
                                string displayName = accountFields[2];

                                // Chỉ tạo phiên khi xác thực thành công.
                                lock (clientStateLock)
                                {
                                    sessions[client] = new UserSession(
                                        authenticatedUsername,
                                        displayName,
                                        client);
                                }

                                await writer.WriteLineAsync(
                                    $"LOGIN_RESULT|OK|{authenticatedUsername}|{displayName}");

                                LogServer(
                                    $"Login success: {authenticatedUsername} - " +
                                    $"{client.Client.RemoteEndPoint}");
                            }
                            else
                            {
                                await writer.WriteLineAsync(
                                    "LOGIN_RESULT|" + result);

                                LogServer($"Login failed: {username}");
                            }

                            continue;
                        }


                        // LOGOUT: đăng xuất khỏi tài khoản
                        //Theo code server hiện tại 29/10/2026, LOGOUT sẽ đóng kết nối TCP, chứ chưa có hệ thống phiên đăng nhập để thu hồi.
                        //Vì vậy, đây là đăng xuất ở mức giao thức/kết nối, chưa phải quản lý phiên tài khoản hoàn chỉnh.


                        else if (command == "LOGOUT")
                        {
                            UserSession session = null;

                            // Chỉ xóa phiên của kết nối đang gửi LOGOUT.
                            lock (clientStateLock)
                            {
                                if (sessions.TryGetValue(client, out session))
                                {
                                    sessions.Remove(client);
                                }
                            }

                            if (session == null)
                            {
                                await writer.WriteLineAsync(
                                    "LOGOUT_RESULT|FAIL|NOT_LOGGED_IN");
                                continue;
                            }

                            await writer.WriteLineAsync(
                                "LOGOUT_RESULT|OK|LOGGED_OUT");

                            LogServer(
                                $"Logout success: {session.Username} - " +
                                $"{client.Client.RemoteEndPoint}");

                            // Kết thúc kết nối này; không ảnh hưởng thiết bị khác.
                            break;
                        }


                        // JOIN: tham gia phòng chat

                        else if (command == "JOIN")
                        {
                            // 1. Kiểm tra kết nối đã đăng nhập chưa.
                            UserSession session = null;

                            lock (clientStateLock)
                            {
                                sessions.TryGetValue(client, out session);
                            }

                            if (session == null)
                            {
                                await writer.WriteLineAsync(
                                    "ERROR|NOT_AUTHENTICATED|Login first");
                                continue;
                            }

                            // 2. Không cho tham gia phòng hai lần trên cùng kết nối.
                            if (joined)
                            {
                                await writer.WriteLineAsync(
                                    "ERROR|ALREADY_JOINED|Already joined");
                                continue;
                            }

                            // 3. Lấy tên hiển thị từ phiên đã xác thực.
                            // Không tin tên do client tự gửi lên.
                            name = session.DisplayName;

                            if (string.IsNullOrWhiteSpace(name) ||
                                name.Length > 32 ||
                                name.Contains("|") ||
                                name.Contains("\r") ||
                                name.Contains("\n"))
                            {
                                await writer.WriteLineAsync(
                                    "ERROR|INVALID_NAME|Invalid display name");
                                continue;
                            }

                            // 4. Kiểm tra tên đã có người sử dụng trong phòng chưa.
                            bool nameInUse;

                            lock (clientStateLock)
                            {
                                nameInUse = clientNames.ContainsValue(name);

                                if (!nameInUse)
                                {
                                    clientNames[client] = name;
                                    clients.Add(client);
                                    joined = true;
                                }
                            }

                            if (nameInUse)
                            {
                                await writer.WriteLineAsync(
                                    "ERROR|NAME_IN_USE|Name is already in use");
                                continue;
                            }

                            // 5. Thông báo tham gia phòng thành công.
                            await writer.WriteLineAsync($"JOINED|{name}");

                            LogServer(
                                $"{name} joined from {client.Client.RemoteEndPoint}");

                            continue;
                        }


                        // MSG: gửi tin nhắn đến các client khác

                        else if (command == "MSG")
                        {
                            // 1. Kiểm tra phiên đăng nhập.
                            UserSession session = null;

                            lock (clientStateLock)
                            {
                                sessions.TryGetValue(client, out session);
                            }

                            if (session == null)
                            {
                                await writer.WriteLineAsync(
                                    "ERROR|NOT_AUTHENTICATED|Login first");
                                continue;
                            }

                            // 2. Kiểm tra đã tham gia phòng chat chưa.
                            if (!joined)
                            {
                                await writer.WriteLineAsync(
                                    "ERROR|NOT_JOINED|Join first");
                                continue;
                            }

                            // 3. Kiểm tra nội dung tin nhắn.
                            if (string.IsNullOrWhiteSpace(data) || data.Length > 2000)
                            {
                                await writer.WriteLineAsync(
                                    "ERROR|INVALID_MESSAGE|Invalid message");
                                continue;
                            }

                            // 4. Dùng tên hiển thị từ phiên đã xác thực.
                            string outgoing = $"FROM|{session.DisplayName}|{data}";
                            TcpClient[] recipients;

                            lock (clientStateLock)
                            {
                                recipients = clients
                                    .Where(c => c != client)
                                    .ToArray();
                            }

                            // 5. Gửi tin nhắn đến các client khác trong phòng.
                            foreach (TcpClient other in recipients)
                            {
                                SemaphoreSlim otherLock;

                                lock (clientStateLock)
                                {
                                    if (!clientWriteLocks.TryGetValue(
                                        other, out otherLock))
                                    {
                                        continue;
                                    }
                                }

                                await otherLock.WaitAsync();

                                try
                                {
                                    if (!other.Connected)
                                    {
                                        continue;
                                    }

                                    using (StreamWriter otherWriter = new StreamWriter(
                                        other.GetStream(),
                                        new UTF8Encoding(false),
                                        1024,
                                        true))
                                    {
                                        otherWriter.AutoFlush = true;
                                        await otherWriter.WriteLineAsync(outgoing);
                                    }
                                }
                                catch (IOException)
                                {
                                    // Client nhận có thể đã ngắt kết nối.
                                }
                                catch (SocketException)
                                {
                                    // Bỏ qua lỗi kết nối của client nhận.
                                }
                                catch (ObjectDisposedException)
                                {
                                    // Kết nối nhận đã bị đóng.
                                }
                                finally
                                {
                                    otherLock.Release();
                                }
                            }

                            LogServer($"{session.DisplayName}: {data}");

                            continue;
                        }


                        // LEAVE: rời phòng chat
                        else if (command == "LEAVE")
                        {
                            if (!joined)
                            {
                                await writer.WriteLineAsync(
                                    "ERROR|NOT_JOINED|Join first");
                                continue;
                            }

                            await writer.WriteLineAsync("LEFT");
                            break;
                        }

                        // Lệnh không được hỗ trợ
                        else
                        {
                            await writer.WriteLineAsync(
                                "ERROR|UNKNOWN_COMMAND|Unknown command");
                        }
                    }
                }
            }
            catch (IOException)
            {
                // Client có thể đã ngắt kết nối.
            }
            catch (SocketException)
            {
                // Lỗi kết nối mạng.
            }
            catch (ObjectDisposedException)
            {
                // Socket đã bị đóng.
            }

            finally
            {
                //Xóa phiên đăng nhập của kết nối này.
                UserSession endedSession = null;

                lock (clientStateLock)
                {
                    if (sessions.TryGetValue(client, out endedSession))
                    {
                        sessions.Remove(client);
                    }

                    // Dọn trạng thái phòng chat và write lock.
                    clients.Remove(client);
                    clientNames.Remove(client);
                    clientWriteLocks.Remove(client);
                }

                // Ghi log nếu kết nối kết thúc khi còn phiên đăng nhập.
                if (endedSession != null)
                {
                    LogServer(
                        $"Session ended: {endedSession.Username} - " +
                        $"{client.Client.RemoteEndPoint}");
                }

                //Đóng kết nối và giải phóng tài nguyên.
                client.Close();
                writeLock.Dispose();

                if (joined)
                {
                    LogServer($"{name} disconnected.");
                }
            }

        }


        private void LogServer(string message)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => LogServer(message)));
                return;
            }

            Server_ChatView.AppendText(message + Environment.NewLine);
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void Server_Load(object sender, EventArgs e)
        {

        }

        private void inputPortBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void inputIPBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void inputPassWordBox_TextChanged(object sender, EventArgs e)
        {

        }


        private void ExitButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void SendButton_Click(object sender, EventArgs e)
        {

        }

        private void UserChat_TextChanged(object sender, EventArgs e)
        {

        }

        private void DisconnectButton_Click(object sender, EventArgs e) //nút tắt server
        {
            isRunning = false;

            server?.Stop();
            server = null;

            foreach (TcpClient client in clients)
            {
                client.Close();
            }

            clients.Clear();
            clientNames.Clear();

            Server_ChatView.AppendText("Server stopped.\r\n");
        }

        private void ChatView_TextChanged(object sender, EventArgs e)
        {

        }

        private async void Start_Server_Button_Click(object sender, EventArgs e)
        {
            IPAddress ip = IPAddress.Parse("127.0.0.1"); //127.0.0.1 để tự chạy trên máy
            int port = 8080;

            server = new TcpListener(ip, port);
            server.Start(); //khởi động tcp serer từ ip và port đã cung cấp

            isRunning = true;
            Server_ChatView.AppendText($"Server started at {ip}:{port}\n");
            await AcceptClientAsync();
        }

        private void Exit_button_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void Close_Server_Button_Click(object sender, EventArgs e)
        {
            isRunning = false;

            server?.Stop();
            server = null;

            foreach (TcpClient client in clients)
            {
                client.Close();
            }

            clients.Clear();
            clientNames.Clear();

            Server_ChatView.AppendText("Server stopped.\r\n");
        }
    }
}
