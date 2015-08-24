// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Server
{
    public static class MessageBusExtensions
    {
        public static Task Publish(this IMessageBus bus, string source, string key, string value)
        {
            if (bus == null)
            {
                throw new ArgumentNullException("bus");
            }

            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            if (String.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException("key");
            }

            return bus.Publish(new Message(source, key, value));
        }

        public static void Enumerate(this IList<ArraySegment<Message>> messages, Action<Message> onMessage)
        {
            if (messages == null)
            {
                throw new ArgumentNullException("messages");
            }

            if (onMessage == null)
            {
                throw new ArgumentNullException("onMessage");
            }

            Enumerate<object>(messages, message => true, (state, message) => onMessage(message), state: null);
        }

        public static void Enumerate<T>(this IList<ArraySegment<Message>> messages, Func<Message, bool> filter, Action<T, Message> onMessage, T state)
        {
            if (messages == null)
            {
                throw new ArgumentNullException("messages");
            }

            if (filter == null)
            {
                throw new ArgumentNullException("filter");
            }

            if (onMessage == null)
            {
                throw new ArgumentNullException("onMessage");
            }

            for (int i = 0; i < messages.Count; i++)
            {
                ArraySegment<Message> segment = messages[i];
                for (int j = segment.Offset; j < segment.Offset + segment.Count; j++)
                {
                    Message message = segment.Array[j];

                    if (filter(message))
                    {
                        onMessage(state, message);
                    }
                }
            }
        }
    }
}
