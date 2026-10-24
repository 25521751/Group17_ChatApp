
using System;

namespace ChatappZalu
{
    internal static class ChatClientManager
    {
        private static readonly ChatClient client = new ChatClient();

        public static ChatClient Client
        {
            get { return client; }
        }

        public static void Shutdown()
        {
            client.Dispose();
        }
    }
}
