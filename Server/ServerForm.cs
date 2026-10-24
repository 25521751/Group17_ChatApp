using Server.Chat_room_test_TCP;
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

            try
            {
                NetworkStream stream = client.GetStream();

                using (StreamReader reader = new StreamReader(
                    stream, Encoding.UTF8, false, 1024, true))
                using (StreamWriter writer = new StreamWriter(
                    stream, new UTF8Encoding(false), 1024, true))
                {
                    writer.AutoFlush = true;

                    string line;

                    // Đọc liên tục cho đến khi client ngắt kết nối
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        // Tách lệnh và dữ liệu tại dấu | đầu tiên
                        string[] parts = line.Split(
                            new char[] { '|' }, 2);

                        string command = parts[0];
                        string data = parts.Length > 1 ? parts[1] : "";

                        //làm giao thức của đăng ký

                        if (command == "REGISTER")
                        {
                            // DATA phải có đúng 3 trường:
                            // username|password|display_name
                            string[] fields = data.Split('|');

                            if (fields.Length != 3)
                            {
                                await writer.WriteLineAsync(
                                    "REGISTER_RESULT|FAIL|INVALID_DATA");
                                continue;
                            }

                            string username = fields[0];
                            string password = fields[1];
                            string displayName = fields[2];

                            string result = accountService.Register(
                                username, password, displayName);

                            await writer.WriteLineAsync(
                                "REGISTER_RESULT|" + result);

                            Server_ChatView.AppendText(
                                $"Register request: {username} - " +
                                $"{(result.StartsWith("OK|") ? "Success" : "Failed")}\r\n");
                        }
                        else if (command == "JOIN")
                        {

                            //

                            // JOIN: Client xin tham gia phòng chat
                            if (command == "JOIN")
                            {
                                if (joined)
                                {
                                    await writer.WriteLineAsync(
                                        "ERROR|ALREADY_JOINED|Already joined");
                                    continue;
                                }

                                name = data.Trim();

                                if (name.Length == 0 ||
                                    name.Length > 32 ||
                                    name.Contains("|") ||
                                    name.Contains("\r") ||
                                    name.Contains("\n"))
                                {
                                    await writer.WriteLineAsync(
                                        "ERROR|INVALID_NAME|Invalid display name");
                                    continue;
                                }

                                if (clientNames.ContainsValue(name))
                                {
                                    await writer.WriteLineAsync(
                                        "ERROR|NAME_IN_USE|Name is already in use");
                                    continue;
                                }

                                clientNames[client] = name;
                                clients.Add(client);
                                joined = true;

                                await writer.WriteLineAsync($"JOINED|{name}");

                                Server_ChatView.AppendText(
                                    $"{name} joined from " +
                                    $"{client.Client.RemoteEndPoint}\r\n");
                            }
                            // MSG: Client gửi tin nhắn
                            else if (command == "MSG")
                            {
                                if (!joined)
                                {
                                    await writer.WriteLineAsync(
                                        "ERROR|NOT_JOINED|Join first");
                                    continue;
                                }

                                if (data.Length == 0 || data.Length > 2000)
                                {
                                    await writer.WriteLineAsync(
                                        "ERROR|INVALID_MESSAGE|Invalid message");
                                    continue;
                                }

                                string outgoing = $"FROM|{name}|{data}";

                                // Gửi cho các client khác, không gửi lại người gửi
                                foreach (TcpClient other in clients.ToArray())
                                {
                                    if (other == client)
                                        continue;

                                    if (other.Connected)
                                    {
                                        try
                                        {
                                            NetworkStream otherStream =
                                                other.GetStream();

                                            using (StreamWriter otherWriter =
                                                new StreamWriter(
                                                    otherStream,
                                                    new UTF8Encoding(false),
                                                    1024,
                                                    true))
                                            {
                                                otherWriter.AutoFlush = true;
                                                await otherWriter.WriteLineAsync(
                                                    outgoing);
                                            }
                                        }
                                        catch (IOException)
                                        {
                                            // Client đích có thể đã ngắt kết nối.
                                        }
                                        catch (SocketException)
                                        {
                                            // Bỏ qua lỗi kết nối của client đích.
                                        }
                                    }
                                }

                                Server_ChatView.AppendText(
                                    $"{name}: {data}\r\n");
                            }
                            // LEAVE: Client chủ động rời phòng chat
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
                // Socket đã bị đóng, ví dụ khi server dừng.
            }
            finally
            {
                if (joined)
                {
                    clientNames.Remove(client);
                    clients.Remove(client);

                    Server_ChatView.AppendText(
                        $"{name} disconnected.\r\n");
                }

                client.Close();
            }
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
