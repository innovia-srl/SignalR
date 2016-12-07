using System;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Sockets
{
    public struct Message : IDisposable
    {
        public bool EndOfMessage { get; }
        public Format MessageFormat { get; }
        public PreservedBuffer Payload { get; }

        public Message(PreservedBuffer payload, Format messageFormat, bool endOfMessage)
        {
            MessageFormat = messageFormat;
            EndOfMessage = endOfMessage;
            Payload = payload;
        }

        public void Dispose()
        {
            ((IDisposable)Payload).Dispose();
        }
    }
}
