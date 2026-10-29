using ChatSocketServer.Core;
using ChatSocketServer.Data;
using ChatSocketServer.Net;
using ChatSocketServer.Services;

namespace ChatSocketServer;

/// <summary>Nơi giữ mọi thành phần của server để các lớp gọi lẫn nhau.</summary>
public sealed class ServerContext
{
    public ServerConfig Config { get; }
    public Db Db { get; }
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public SessionManager Sessions { get; }
    public CommandHandler Handler { get; }
    public FileStore Files { get; }
    public MediaRelay Media { get; }

    public AuthService Auth { get; }
    public UserService Users { get; }
    public FriendService Friends { get; }
    public ConversationService Conversations { get; }
    public MessageService Messages { get; }
    public NotificationService Notifications { get; }
    public CallService Calls { get; }
    public Chatbot Bot { get; }
    public EmailService Email { get; }

    public ServerContext(ServerConfig config, Db db)
    {
        Config = config;
        Db = db;
        Sessions = new SessionManager(this);
        Files = new FileStore(config);
        Media = new MediaRelay(this);
        Auth = new AuthService(this);
        Users = new UserService(this);
        Friends = new FriendService(this);
        Conversations = new ConversationService(this);
        Messages = new MessageService(this);
        Notifications = new NotificationService(this);
        Calls = new CallService(this);
        Bot = new Chatbot(this);
        Email = new EmailService(config.Smtp);
        Handler = new CommandHandler(this);
    }
}
