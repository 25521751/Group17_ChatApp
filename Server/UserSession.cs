
using System;
using System.Net.Sockets;

namespace Server.ChatappZalu
{
    public class UserSession
    {
        public string Username { get; }
        public string DisplayName { get; }
        public TcpClient Client { get; }
        public DateTime LoginAt { get; }

        public UserSession(
            string username,
            string displayName,
            TcpClient client)
        {
            Username = username;
            DisplayName = displayName;
            Client = client;
            LoginAt = DateTime.Now;
        }
    }
}
