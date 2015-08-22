using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{

    public class WebSocketMessageReader
    {
        private readonly static ArraySegment<byte> _emptyArraySegment = new ArraySegment<byte>(new byte[0]);

        public static async Task<WebSocketMessage> ReadMessageAsync(WebSocket webSocket, MemoryPool _memoryPool, int bufferSize, int? maxMessageSize, CancellationToken disconnectToken)
        {
            WebSocketMessage message;

            // Read the first time with an empty array
            //      Holds hear untill first message comes through. Because its empty, it will go onto the next 
            //      TryGetMessage when the data comes through.
            var receiveResult = await webSocket.ReceiveAsync(_emptyArraySegment, disconnectToken).PreserveCultureNotContext();
            if (TryGetMessage(receiveResult, null, out message))
            {
                return message;
            }
            
            var arraySegment = _memoryPool.AllocSegment(bufferSize);

            // Now read with the real buffer
            //      We now know there is content, so we we allocate memory. Additionally this is setup in hopes
            //      that the first message we read will only take one read.
            receiveResult = await webSocket.ReceiveAsync(arraySegment, disconnectToken).PreserveCultureNotContext();
            if (TryGetMessage(receiveResult, arraySegment.Array, out message))
            {
                return message;
            }

            // Lastly read the rest of the message if needed
            //      For multi-fragment messages, we need to coalesce and use a buffer that can grow in size 
            //      as more of the message comes in.
            var bytebuffer = new ByteBuffer(maxMessageSize);
            bytebuffer.Append(BufferSliceToByteArray(arraySegment.Array, receiveResult.Count));
            var originalMessageType = receiveResult.MessageType;

            while (true)
            {
                // loop until an error occurs or we see EOF
                receiveResult = await webSocket.ReceiveAsync(arraySegment, disconnectToken).PreserveCultureNotContext();

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    return WebSocketMessage.CloseMessage;
                }

                if (receiveResult.MessageType != originalMessageType)
                {
                    throw new InvalidOperationException("Incorrect message type");
                }

                bytebuffer.Append(BufferSliceToByteArray(arraySegment.Array, receiveResult.Count));

                if (receiveResult.EndOfMessage)
                {
                    switch (receiveResult.MessageType)
                    {
                        case WebSocketMessageType.Binary:
                            return new WebSocketMessage(bytebuffer.GetByteArray(), WebSocketMessageType.Binary);

                        case WebSocketMessageType.Text:
                            return new WebSocketMessage(bytebuffer.GetString(), WebSocketMessageType.Text);

                        default:
                            throw new InvalidOperationException("Unknown message type");
                    }
                }
            }
        }

        private static bool TryGetMessage(WebSocketReceiveResult receiveResult, byte[] buffer, out WebSocketMessage message)
        {
            message = null;

            if (receiveResult.MessageType == WebSocketMessageType.Close)
            {
                message = WebSocketMessage.CloseMessage;
            }
            else if (receiveResult.EndOfMessage)
            {
                // we anticipate that single-fragment messages will be common, so we optimize for them
                switch (receiveResult.MessageType)
                {
                    case WebSocketMessageType.Binary:
                        if (buffer == null)
                        {
                            message = WebSocketMessage.EmptyBinaryMessage;
                        }
                        else
                        {
                            message = new WebSocketMessage(BufferSliceToByteArray(buffer, receiveResult.Count), WebSocketMessageType.Binary);
                        }
                        break;
                    case WebSocketMessageType.Text:
                        if (buffer == null)
                        {
                            message = WebSocketMessage.EmptyTextMessage;
                        }
                        else
                        {
                            message = new WebSocketMessage(BufferSliceToString(buffer, receiveResult.Count), WebSocketMessageType.Text);
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Unknown message type");
                }
            }

            return message != null;
        }

        private static byte[] BufferSliceToByteArray(byte[] buffer, int count)
        {
            var newArray = new byte[count];
            Buffer.BlockCopy(buffer, 0, newArray, 0, count);
            return newArray;
        }

        private static string BufferSliceToString(byte[] buffer, int count)
        {
            return Encoding.UTF8.GetString(buffer, 0, count);
        }
    }
}
