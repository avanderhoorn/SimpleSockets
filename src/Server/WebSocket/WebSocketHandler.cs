using System;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Framework.Logging;

namespace Server
{
    public class WebSocketHandler : IWebSocketHandler
    {
        private static readonly TimeSpan _closeTimeout = TimeSpan.FromMilliseconds(250);  // Wait 250 ms before giving up on a Close
        private const int _receiveLoopBufferSize = 2 * 1024; // 4KB default fragment size (we expect most messages to be very short)
        private readonly int? _maxIncomingMessageSize;
        private readonly ILogger _logger;
        private readonly TaskQueue _sendQueue = new TaskQueue(); // Queue for sending messages
        private readonly MemoryPool _memoryPool;
        private WebSocket _webSocket;

        public WebSocketHandler(MemoryPool memoryPool, ILoggerFactory loggerFactory)
            : this(memoryPool, loggerFactory, null)
        {
            
        }

        public WebSocketHandler(MemoryPool memoryPool, ILoggerFactory loggerFactory, int? maxIncomingMessageSize)
        {
            _maxIncomingMessageSize = maxIncomingMessageSize;
            _memoryPool = memoryPool;
            _logger = loggerFactory.CreateLogger<WebSocketHandler>();
            
            OnOpenAction = () => { };
            OnMessageTextAction = msg => { };
            OnMessageByteAction = msg => { };
            OnCloseAction = () => { };
            OnErrorAction = e => { };
        }

        public int? MaxIncomingMessageSize { get { return _maxIncomingMessageSize; } }
        
        public Exception Error { get; set; }
        
        public Action OnOpenAction { get; set; }

        public Action<string> OnMessageTextAction { get; set; }

        public Action<byte[]> OnMessageByteAction { get; set; }

        public Action OnCloseAction { get; set; }

        public Action<Exception> OnErrorAction { get; set; }


        
        public virtual void OnOpen()
        {
            OnCloseAction();
        }

        public virtual void OnMessageText(string message)
        {
            OnMessageTextAction(message);
        }

        public virtual void OnMessageByte(byte[] message)
        {
            OnMessageByteAction(message);
        }

        public virtual void OnError()
        {
            OnErrorAction(Error);
        }

        public virtual void OnClose()
        {
            OnCloseAction();
        }
        
        public Task SendAsync(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            return SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text);
        }

        public virtual Task SendAsync(ArraySegment<byte> message, WebSocketMessageType messageType, bool endOfMessage = true)
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                return TaskAsyncHelper.Empty;
            }

            var sendContext = new SendContext(this, message, messageType, endOfMessage);

            return _sendQueue.Enqueue(async state =>
                {
                    var context = (SendContext)state;

                    if (context.Handler._webSocket.State != WebSocketState.Open)
                    {
                        return;
                    }

                    try
                    {
                        await context.Handler._webSocket
                              .SendAsync(context.Message, context.MessageType, context.EndOfMessage, CancellationToken.None)
                              .PreserveCulture();
                    }
                    catch (Exception ex)
                    {
                        // Swallow exceptions on send
                        _logger.LogError("Error while sending: " + ex);
                    }
                },
                sendContext);
        }

        public virtual Task CloseAsync()
        {
            if (IsClosedOrClosedSent(_webSocket))
            {
                return TaskAsyncHelper.Empty;
            }

            var closeContext = new CloseContext(this);

            return _sendQueue.Enqueue(async state =>
                {
                    var context = (CloseContext)state;

                    if (IsClosedOrClosedSent(context.Handler._webSocket))
                    {
                        return;
                    }

                    try
                    {
                        await context.Handler._webSocket
                            .CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                            .PreserveCulture();
                    }
                    catch (Exception ex)
                    {
                        // Swallow exceptions on close
                        _logger.LogError("Error while closing the websocket: " + ex);
                    }
                },
                closeContext);
        }

        public Task ProcessWebSocketRequestAsync(WebSocket webSocket)
        {
            return ProcessWebSocketRequestAsync(webSocket, CancellationToken.None);
        }

        public Task ProcessWebSocketRequestAsync(WebSocket webSocket, CancellationToken disconnectToken)
        {
            if (webSocket == null)
            {
                throw new ArgumentNullException("webSocket");
            }

            var receiveContext = new ReceiveContext(webSocket, disconnectToken, MaxIncomingMessageSize, _receiveLoopBufferSize);

            return ProcessWebSocketRequestAsync(webSocket, disconnectToken, state =>
                {
                    var context = (ReceiveContext)state;

                    return WebSocketMessageReader.ReadMessageAsync(context.WebSocket, _memoryPool, context.BufferSize, context.MaxIncomingMessageSize, context.DisconnectToken);
                },
                receiveContext);
        }

        internal async Task ProcessWebSocketRequestAsync(WebSocket webSocket, CancellationToken disconnectToken, Func<object, Task<WebSocketMessage>> messageRetriever, object state)
        {
            var closedReceived = false;

            try
            {
                // first, set primitives and initialize the object
                _webSocket = webSocket;

                OnOpen();

                // dispatch incoming messages
                while (!disconnectToken.IsCancellationRequested && !closedReceived)
                {
                    var incomingMessage = await messageRetriever(state).PreserveCulture();
                    switch (incomingMessage.MessageType)
                    {
                        case WebSocketMessageType.Binary:
                            OnMessageByte((byte[])incomingMessage.Data);
                            break;

                        case WebSocketMessageType.Text:
                            OnMessageText((string)incomingMessage.Data);
                            break;

                        default:
                            closedReceived = true;

                            // If we received an incoming CLOSE message, we'll queue a CLOSE frame to be sent.
                            // We'll give the queued frame some amount of time to go out on the wire, and if a
                            // timeout occurs we'll give up and abort the connection.
                            await Task.WhenAny(CloseAsync(), Task.Delay(_closeTimeout)).PreserveCulture();
                            break;
                    }
                }

            }
            catch (OperationCanceledException ex)
            {
                // ex.CancellationToken never has the token that was actually cancelled
                if (!disconnectToken.IsCancellationRequested)
                {
                    Error = ex;
                    OnError();
                }
            }
            catch (ObjectDisposedException)
            {
                // If the websocket was disposed while we were reading then noop
            }
            catch (Exception ex)
            {
                if (IsFatalException(ex))
                {
                    Error = ex;
                    OnError();
                }
            }

            OnClose();
        }
        
        // returns true if this is a fatal exception (e.g. OnError should be called)
        private static bool IsFatalException(Exception ex)
        {
#if DNX451
            // If this exception is due to the underlying TCP connection going away, treat as a normal close
            // rather than a fatal exception.
            COMException ce = ex as COMException;
            if (ce != null)
            {
                switch ((uint)ce.ErrorCode)
                {
                    // These are the three error codes we've seen in testing which can be caused by the TCP connection going away unexpectedly.
                    case 0x800703e3:
                    case 0x800704cd:
                    case 0x80070026:
                        return false;
                }
            }
#endif
            // unknown exception; treat as fatal
            return true;
        }

        private static bool IsClosedOrClosedSent(WebSocket webSocket)
        {
            return webSocket.State == WebSocketState.Closed || webSocket.State == WebSocketState.CloseSent || webSocket.State == WebSocketState.Aborted;
        }
        
        private class CloseContext
        {
            public WebSocketHandler Handler;

            public CloseContext(WebSocketHandler webSocketHandler)
            {
                Handler = webSocketHandler;
            }
        }

        private class SendContext
        {
            public WebSocketHandler Handler;
            public ArraySegment<byte> Message;
            public WebSocketMessageType MessageType;
            public bool EndOfMessage;

            public SendContext(WebSocketHandler webSocketHandler, ArraySegment<byte> message, WebSocketMessageType messageType, bool endOfMessage)
            {
                Handler = webSocketHandler;
                Message = message;
                MessageType = messageType;
                EndOfMessage = endOfMessage;
            }
        }

        private class ReceiveContext
        {
            public WebSocket WebSocket;
            public CancellationToken DisconnectToken;
            public int? MaxIncomingMessageSize;
            public int BufferSize;

            public ReceiveContext(WebSocket webSocket, CancellationToken disconnectToken, int? maxIncomingMessageSize, int bufferSize)
            {
                WebSocket = webSocket;
                DisconnectToken = disconnectToken;
                MaxIncomingMessageSize = maxIncomingMessageSize;
                BufferSize = bufferSize;
            }
        }
    }
}
