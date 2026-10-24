using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatappZalu
{
    public class ChatClient : IDisposable
    {
        private TcpClient? tcpClient;
        private NetworkStream? stream;
        private StreamReader? reader;
        private StreamWriter? writer;

        private CancellationTokenSource? receiveCancellation;
        private Task? receiveTask;

        private readonly SemaphoreSlim sendLock =
            new SemaphoreSlim(1, 1);

        private readonly object stateLock = new object();

        private bool isConnected;
        private bool isDisposed;

        public bool IsConnected
        {
            get
            {
                lock (stateLock)
                {
                    return isConnected;
                }
            }
        }

        // Server gửi về một dòng giao thức.
        public event Action<string>? LineReceived;

        // Thông báo trạng thái kết nối.
        public event Action<string>? ConnectionStatusChanged;

        // Thông báo lỗi mạng.
        public event Action<string>? ConnectionError;

        public async Task ConnectAsync(
            string ipAddress,
            int port)
        {
            if (isDisposed)
                throw new ObjectDisposedException("ChatClient");

            if (IsConnected)
                throw new InvalidOperationException(
                    "Client is already connected.");

            TcpClient newClient = new TcpClient();

            try
            {
                await newClient.ConnectAsync(ipAddress, port);

                NetworkStream newStream = newClient.GetStream();

                var newReader = new StreamReader(
                    newStream,
                    Encoding.UTF8,
                    false,
                    1024,
                    true);

                var newWriter = new StreamWriter(
                    newStream,
                    new UTF8Encoding(false),
                    1024,
                    true);

                newWriter.AutoFlush = true;

                lock (stateLock)
                {
                    tcpClient = newClient;
                    stream = newStream;
                    reader = newReader;
                    writer = newWriter;

                    receiveCancellation =
                        new CancellationTokenSource();

                    isConnected = true;
                }

                ConnectionStatusChanged?.Invoke(
                    "Connected to server.");

                // Chỉ có MỘT vòng lặp đọc dữ liệu từ server.
                receiveTask = ReceiveLoopAsync(
                    newClient,
                    newReader,
                    receiveCancellation.Token);
            }
            catch
            {
                newClient.Close();
                throw;
            }
        }

        public async Task SendLineAsync(string message)
        {
            if (string.IsNullOrEmpty(message) ||
                message.Contains("\r") ||
                message.Contains("\n"))
            {
                throw new ArgumentException(
                    "Message must contain exactly one protocol line.");
            }

            await sendLock.WaitAsync();

            try
            {
                StreamWriter currentWriter;

                lock (stateLock)
                {
                    if (!isConnected || writer == null)
                    {
                        throw new InvalidOperationException(
                            "Client is not connected.");
                    }

                    currentWriter = writer;
                }

                await currentWriter.WriteLineAsync(message);
            }
            catch (IOException ex)
            {
                ConnectionError?.Invoke(ex.Message);
                throw;
            }
            catch (ObjectDisposedException ex)
            {
                ConnectionError?.Invoke(ex.Message);
                throw;
            }
            finally
            {
                sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(
            TcpClient owner,
            StreamReader currentReader,
            CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    string? line =
                        await currentReader.ReadLineAsync();

                    if (line == null)
                        break;

                    LineReceived?.Invoke(line);
                }
            }
            catch (IOException ex)
            {
                if (!token.IsCancellationRequested)
                    ConnectionError?.Invoke(ex.Message);
            }
            catch (ObjectDisposedException)
            {
                // Kết nối đã được đóng.
            }
            finally
            {
                bool wasCurrentConnection = false;
                CancellationTokenSource? oldCancellation = null;

                lock (stateLock)
                {
                    if (ReferenceEquals(tcpClient, owner))
                    {
                        isConnected = false;

                        tcpClient = null;
                        stream = null;
                        reader = null;
                        writer = null;

                        oldCancellation = receiveCancellation;
                        receiveCancellation = null;

                        wasCurrentConnection = true;
                    }
                }

                if (wasCurrentConnection)
                {
                    if (oldCancellation != null)
                        oldCancellation.Dispose();

                    owner.Close();

                    ConnectionStatusChanged?.Invoke(
                        "Disconnected from server.");
                }
            }
        }

        public void Disconnect()
        {
            TcpClient? oldClient;
            CancellationTokenSource? oldCancellation;

            lock (stateLock)
            {
                oldClient = tcpClient;
                oldCancellation = receiveCancellation;

                tcpClient = null;
                stream = null;
                reader = null;
                writer = null;
                receiveCancellation = null;

                isConnected = false;
            }

            if (oldCancellation != null)
            {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }

            if (oldClient != null)
                oldClient.Close();

            ConnectionStatusChanged?.Invoke(
                "Disconnected from server.");
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            Disconnect();

            sendLock.Dispose();
        }
    }
}