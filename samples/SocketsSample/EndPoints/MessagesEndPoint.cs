using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;

namespace SocketsSample.EndPoints
{
    public class MessagesEndPoint : MessagingEndPoint
    {
        public ConnectionList Connections { get; } = new ConnectionList();

        public override async Task OnConnectedAsync(MessagingConnection connection)
        {
            Connections.Add(connection);

            await Broadcast($"{connection.ConnectionId} connected ({connection.Metadata["transport"]})");

            try
            {
                while (!connection.Transport.Input.Completion.IsCompleted)
                {
                    using (var message = await connection.Transport.Input.ReadAsync())
                    {
                        // We can avoid the copy here but we'll deal with that later
                        await Broadcast(message.Payload.Buffer, message.MessageFormat, message.EndOfMessage);
                    }
                }
            }
            finally
            {
                Connections.Remove(connection);

                await Broadcast($"{connection.ConnectionId} disconnected ({connection.Metadata["transport"]})");
            }
        }

        private Task Broadcast(string text)
        {
            return Broadcast(ReadableBuffer.Create(Encoding.UTF8.GetBytes(text)), Format.Text, endOfMessage: true);
        }

        private Task Broadcast(ReadableBuffer payload, Format format, bool endOfMessage)
        {
            var tasks = new List<Task>(Connections.Count);

            foreach (var c in Connections.Cast<MessagingConnection>())
            {
                tasks.Add(c.Transport.Output.WriteAsync(new Message(
                    payload.Preserve(),
                    format,
                    endOfMessage)));
            }

            return Task.WhenAll(tasks);
        }
    }
}
