using System.Globalization;
using ChatSocketServer.Core;
using ChatSocketServer.Net;

namespace ChatSocketServer.Services;

/// <summary>
/// Chatbot demo (tính năng bổ sung trong Timeline). Trả lời trên ThreadPool để không chặn luồng của client gửi tin.
/// Muốn bot thông minh hơn: thay ReplyFor bằng lời gọi HttpClient tới một API AI.
/// </summary>
public sealed class Chatbot
{
    private static readonly TimeZoneInfo VietnamTime = FindVietnamTimeZone();
    private readonly ServerContext _ctx;

    public Chatbot(ServerContext ctx) => _ctx = ctx;

    public void Reply(long conversationId, long botId, string text)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                _ctx.Sessions.SendToConversation(conversationId, Events.Typing, new { conversationId, userId = botId, isTyping = true });
                Thread.Sleep(700);
                _ctx.Messages.SendAsBot(conversationId, botId, ReplyFor(text));
            }
            catch (Exception ex)
            {
                Log.Error("Chatbot không trả lời được", ex);
            }
        });
    }

    public static string ReplyFor(string input)
    {
        var text = input.Trim().ToLowerInvariant().Replace("@chatbot", "").Trim();
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTime);

        if (Has(text, "giúp", "help", "lệnh"))
            return "Mình hiểu các câu: \"chào\", \"mấy giờ\", \"hôm nay ngày mấy\", \"tung xu\", \"đổ xúc xắc\", \"cảm ơn\".";
        if (Has(text, "chào", "hello", "xin chào") || text == "hi")
            return "Chào bạn 👋 Mình là trợ lý ChatApp. Gõ \"giúp\" để xem mình làm được gì.";
        if (Has(text, "mấy giờ", "giờ"))
            return $"Bây giờ là {now:HH:mm} (giờ Việt Nam).";
        if (Has(text, "ngày mấy", "hôm nay", "ngày"))
            return $"Hôm nay là {now.ToString("dddd, dd/MM/yyyy", new CultureInfo("vi-VN"))}.";
        if (Has(text, "tung xu", "đồng xu"))
            return Random.Shared.Next(2) == 0 ? "🪙 Mặt sấp!" : "🪙 Mặt ngửa!";
        if (Has(text, "xúc xắc", "xuc xac", "dice"))
            return $"🎲 Ra {Random.Shared.Next(1, 7)}.";
        if (Has(text, "cảm ơn", "cám ơn", "thanks"))
            return "Không có gì 😊";
        return $"Mình đã nhận: \"{MessageService.Truncate(input.Trim(), 200)}\". Gõ \"giúp\" để xem các lệnh mình hiểu.";
    }

    private static bool Has(string text, params string[] keywords) => keywords.Any(k => text.Contains(k));

    private static TimeZoneInfo FindVietnamTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        return TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    }
}
