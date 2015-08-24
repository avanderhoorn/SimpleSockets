using System;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace Server
{
    public interface IWebSocketHandler
    {
        Action OnOpenAction { get; set; }

        Action<string> OnMessageTextAction { get; set; }

        Action<byte[]> OnMessageByteAction { get; set; }

        Action OnCloseAction { get; set; }

        Action<Exception> OnErrorAction { get; set; }

        Task SendAsync(string message);

        Task CloseAsync();

        Task ProcessWebSocketRequestAsync(WebSocket webSocket);
    }
}