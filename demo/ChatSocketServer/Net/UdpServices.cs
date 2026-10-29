using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ChatSocketServer.Core;
using ChatSocketServer.Data;

namespace ChatSocketServer.Net;

/// <summary>
/// Tìm server trong mạng LAN bằng UDP broadcast (chương 3: UdpClient).
/// Client gửi chuỗi "CHAT_DISCOVER" tới 255.255.255.255:9001,
/// server trả lời một chuỗi JSON chứa tên server và các cổng.
/// => Client không cần gõ địa chỉ IP của server.
/// </summary>
public sealed class UdpDiscovery
{
    public const string Request = "CHAT_DISCOVER";
    private readonly ServerContext _ctx;
    private UdpClient? _udp;

    public UdpDiscovery(ServerContext ctx) => _ctx = ctx;

    public void Start()
    {
        _udp = new UdpClient(_ctx.Config.DiscoveryPort) { EnableBroadcast = true };
        new Thread(Loop) { IsBackground = true, Name = "UDP discovery" }.Start();
        Log.Success($"UDP discovery đang nghe cổng {_ctx.Config.DiscoveryPort} (client broadcast \"{Request}\")");
    }

    private void Loop()
    {
        while (true)
        {
            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var data = _udp!.Receive(ref remote);          // chờ gói UDP
                var text = Encoding.UTF8.GetString(data).Trim();
                if (text != Request) continue;

                var reply = Encoding.UTF8.GetBytes(Json.Serialize(new
                {
                    serverName = _ctx.Config.ServerName,
                    tcpPort = _ctx.Config.TcpPort,
                    httpPort = _ctx.Config.HttpPort,
                    mediaPort = _ctx.Config.MediaPort,
                    online = _ctx.Sessions.OnlineUserIds().Count
                }));
                _udp.Send(reply, reply.Length, remote);
                Log.Net($"UDP discovery: trả lời {remote}");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Windows báo lỗi này khi gói trước gửi tới cổng đã đóng; bỏ qua
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("UDP discovery", ex);
            }
        }
    }
}

/// <summary>
/// Chuyển tiếp âm thanh / hình ảnh của cuộc gọi qua UDP (nhanh, chấp nhận mất gói - chương 3: so sánh TCP và UDP).
///
/// Gói client -> server:
///   [0]      loại: 1 = JOIN/giữ kết nối, 2 = âm thanh, 3 = hình ảnh (1 khung JPEG), 4 = nhạc nền (call nhóm)
///   [1..8]   callId (Int64, thứ tự byte mạng - big-endian, dùng IPAddress.HostToNetworkOrder)
///   [9..24]  mediaToken (16 byte, nhận được khi CALL_START / CALL_ACCEPT)
///   [25..]   dữ liệu
///
/// Gói server -> client:
///   [0]      loại (giống trên; loại 1 = server xác nhận JOIN)
///   [1..8]   callId
///   [9..16]  userId người gửi (Int64 big-endian)
///   [17..]   dữ liệu
///
/// Client phải gửi gói loại 1 ngay khi vào cuộc gọi (và mỗi vài giây) để server biết địa chỉ IP:port UDP của mình.
/// </summary>
public sealed class MediaRelay
{
    public const int HeaderIn = 25;
    public const int HeaderOut = 17;
    public const byte KindJoin = 1, KindAudio = 2, KindVideo = 3, KindMusic = 4;

    private sealed class Peer
    {
        public long CallId;
        public long UserId;
        public IPEndPoint? EndPoint;
        public DateTime LastSeen;
    }

    private readonly ServerContext _ctx;
    private readonly ConcurrentDictionary<string, Peer> _peers = new();   // khoá = token dạng hex
    private UdpClient? _udp;
    private long _relayedPackets;

    public MediaRelay(ServerContext ctx) => _ctx = ctx;

    public long RelayedPackets => Interlocked.Read(ref _relayedPackets);
    public int ActivePeers => _peers.Count;

    public void Start()
    {
        _udp = new UdpClient(_ctx.Config.MediaPort);
        new Thread(Loop) { IsBackground = true, Name = "UDP media relay" }.Start();
        Log.Success($"UDP media relay đang nghe cổng {_ctx.Config.MediaPort}");
    }

    /// <summary>Cấp token cho một người trong cuộc gọi.</summary>
    public MediaInfo Register(long callId, long userId)
    {
        Unregister(callId, userId);
        var token = Security.Tokens.NewMediaToken();
        _peers[Convert.ToHexString(token)] = new Peer { CallId = callId, UserId = userId, LastSeen = DateTime.UtcNow };
        return new MediaInfo(_ctx.Config.MediaPort, Convert.ToBase64String(token));
    }

    public void Unregister(long callId, long userId)
    {
        foreach (var pair in _peers.Where(p => p.Value.CallId == callId && p.Value.UserId == userId).ToList())
            _peers.TryRemove(pair.Key, out _);
    }

    public void UnregisterCall(long callId)
    {
        foreach (var pair in _peers.Where(p => p.Value.CallId == callId).ToList())
            _peers.TryRemove(pair.Key, out _);
    }

    private void Loop()
    {
        while (true)
        {
            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var data = _udp!.Receive(ref remote);
                if (data.Length < HeaderIn) continue;

                var kind = data[0];
                var callId = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(data, 1));
                var tokenHex = Convert.ToHexString(data, 9, 16);
                if (!_peers.TryGetValue(tokenHex, out var sender) || sender.CallId != callId) continue;   // gói lạ: bỏ qua

                sender.EndPoint = remote;
                sender.LastSeen = DateTime.UtcNow;

                if (kind == KindJoin)
                {
                    var ack = BuildPacket(KindJoin, callId, sender.UserId, data, 0);
                    _udp.Send(ack, ack.Length, remote);
                    continue;
                }

                var packet = BuildPacket(kind, callId, sender.UserId, data, data.Length - HeaderIn);
                foreach (var peer in _peers.Values)
                {
                    if (peer.CallId != callId || peer.UserId == sender.UserId || peer.EndPoint == null) continue;
                    _udp.Send(packet, packet.Length, peer.EndPoint);
                    Interlocked.Increment(ref _relayedPackets);
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // một client vừa thoát; bỏ qua
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("UDP media relay", ex);
            }
        }
    }

    private static byte[] BuildPacket(byte kind, long callId, long senderUserId, byte[] source, int payloadLength)
    {
        var packet = new byte[HeaderOut + payloadLength];
        packet[0] = kind;
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(callId)).CopyTo(packet, 1);
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(senderUserId)).CopyTo(packet, 9);
        if (payloadLength > 0) Buffer.BlockCopy(source, HeaderIn, packet, HeaderOut, payloadLength);
        return packet;
    }
}
