using System;
using System.Threading.Tasks.Channels;

namespace Microsoft.AspNetCore.Sockets
{
    // REVIEW: These should probably move to Channels. Why not use IChannel? Because I think it's better to be clear that this is providing
    // access to two separate channels, the read end for one and the write end for the other.
    public interface IChannelConnection<T> : IDisposable
    {
        IReadableChannel<T> Input { get; }
        IWritableChannel<T> Output { get; }
    }
}
