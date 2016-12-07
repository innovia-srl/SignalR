using System;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Sockets.Internal
{
    public class StreamingConnectionState : ConnectionState
    {
        public new StreamingConnection Connection => (StreamingConnection)base.Connection;
        public IPipelineConnection Application { get; }

        public StreamingConnectionState(StreamingConnection connection, IPipelineConnection application) : base(connection)
        {
            Application = application;
        }

        public override void Dispose()
        {
            Connection.Dispose();
            Application.Dispose();
        }
    }
}
