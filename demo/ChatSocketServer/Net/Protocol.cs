using System.Text.Json;

namespace ChatSocketServer.Net;

/*
 * GIAO THỨC (xem chi tiết trong PROTOCOL.md)
 * ------------------------------------------------------------------
 * - Kết nối TCP tới cổng 9000. Mỗi gói tin là MỘT DÒNG JSON (UTF-8) kết thúc bằng '\n'
 *   => client dùng StreamWriter.WriteLine / StreamReader.ReadLine.
 * - Client gửi yêu cầu:   {"cmd":"LOGIN","id":1,"data":{"login":"alice","password":"123456"}}
 * - Server trả lời:       {"cmd":"LOGIN","id":1,"ok":true,"data":{...}}
 *                         {"cmd":"LOGIN","id":1,"ok":false,"error":"Sai mật khẩu"}
 *   "id" do client tự đánh số để ghép câu trả lời với câu hỏi.
 * - Server chủ động đẩy:  {"event":"MESSAGE_NEW","data":{...}}
 * - Sau khi trao đổi khoá (lệnh SECURE), mỗi dòng là Base64(IV + AES(JSON)).
 */

/// <summary>Gói client gửi lên.</summary>
public class Request
{
    public string Cmd { get; set; } = "";
    public long Id { get; set; }
    public JsonElement Data { get; set; }
}

/// <summary>Gói server trả lời cho một yêu cầu.</summary>
public class Response
{
    public string Cmd { get; set; } = "";
    public long Id { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }

    public static Response Success(string cmd, long id, object? data) => new() { Cmd = cmd, Id = id, Ok = true, Data = data };
    public static Response Fail(string cmd, long id, string error) => new() { Cmd = cmd, Id = id, Ok = false, Error = error };
}

/// <summary>Gói server chủ động gửi xuống (không phải câu trả lời).</summary>
public class EventPacket
{
    public string Event { get; set; }
    public object? Data { get; set; }

    public EventPacket(string evt, object? data)
    {
        Event = evt;
        Data = data;
    }
}

/// <summary>Tên các sự kiện server đẩy xuống client.</summary>
public static class Events
{
    public const string Hello = "HELLO";
    public const string MessageNew = "MESSAGE_NEW";
    public const string MessageUpdated = "MESSAGE_UPDATED";
    public const string ReactionsUpdated = "REACTIONS_UPDATED";
    public const string ReadReceipt = "READ_RECEIPT";
    public const string Typing = "TYPING";
    public const string Presence = "PRESENCE";
    public const string UserUpdated = "USER_UPDATED";
    public const string Notification = "NOTIFICATION";
    public const string FriendRequestReceived = "FRIEND_REQUEST_RECEIVED";
    public const string FriendRequestUpdated = "FRIEND_REQUEST_UPDATED";
    public const string FriendAdded = "FRIEND_ADDED";
    public const string FriendRemoved = "FRIEND_REMOVED";
    public const string ConversationCreated = "CONV_CREATED";
    public const string ConversationUpdated = "CONV_UPDATED";
    public const string ConversationRemoved = "CONV_REMOVED";
    public const string CallIncoming = "CALL_INCOMING";
    public const string CallUpdated = "CALL_UPDATED";
    public const string CallEnded = "CALL_ENDED";
    public const string Kicked = "KICKED";
}
