using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets
{
    public abstract class StreamingEndPoint : EndPoint
    {
        public override ConnectionMode Mode => ConnectionMode.Streaming;

        internal protected override Task OnConnectedAsync(Connection connection)
        {
            if(connection.Mode != Mode)
            {
                throw new InvalidOperationException($"Connection mode does not match endpoint mode. Connection mode is '{connection.Mode}', endpoint mode is '{Mode}'");
            }
            return OnConnectedAsync((StreamingConnection)connection);
        }

        /// <summary>
        /// Called when a new connection is accepted to the endpoint
        /// </summary>
        /// <param name="connection">The new <see cref="StreamingConnection"/></param>
        /// <returns>A <see cref="Task"/> that represents the connection lifetime. When the task completes, the connection is complete.</returns>
        public abstract Task OnConnectedAsync(StreamingConnection connection);
    }
}
