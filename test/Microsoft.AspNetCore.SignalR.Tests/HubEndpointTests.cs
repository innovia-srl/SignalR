// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class HubEndpointTests
    {
        [Fact]
        public async Task HubsAreDisposed()
        {
            var trackDispose = new TrackDispose();
            var serviceProvider = CreateServiceProvider(s => s.AddSingleton(trackDispose));

            var endPoint = serviceProvider.GetService<HubEndPoint<TestHub>>();

            using (var connectionWrapper = new ConnectionWrapper())
            {
                var endPointTask = endPoint.OnConnectedAsync(connectionWrapper.ConnectionState.Connection);

                await Task.Delay(10000);

                // REVIEW: Grossssss
                await ((PipelineReaderWriter)connectionWrapper.ConnectionState.Connection.Transport.Input).ReadingStarted;

                // kill the connection
                connectionWrapper.ConnectionState.Dispose();

                await endPointTask;

                Assert.Equal(2, trackDispose.DisposeCount);
            }
        }

        private class TestHub : Hub
        {
            private TrackDispose _trackDispose;

            public TestHub(TrackDispose trackDispose)
            {
                _trackDispose = trackDispose;
            }

            protected override void Dispose(bool dispose)
            {
                if (dispose)
                {
                    _trackDispose.DisposeCount++;
                }
            }
        }

        private class TrackDispose
        {
            public int DisposeCount = 0;
        }

        private IServiceProvider CreateServiceProvider(Action<ServiceCollection> addServices = null)
        {
            var services = new ServiceCollection();
            services.AddOptions()
                .AddLogging()
                .AddSignalR();

            addServices?.Invoke(services);

            return services.BuildServiceProvider();
        }

        private class ConnectionWrapper : IDisposable
        {
            private PipelineFactory _factory;

            public StreamingConnectionState ConnectionState;

            public ConnectionWrapper(string format = "json")
            {
                _factory = new PipelineFactory();

                var connectionManager = new ConnectionManager(_factory);

                ConnectionState = (StreamingConnectionState)connectionManager.CreateConnection(ConnectionMode.Streaming);
                ConnectionState.Connection.Metadata["formatType"] = format;
                ConnectionState.Connection.User = new ClaimsPrincipal(new ClaimsIdentity());
            }

            public void Dispose()
            {
                ConnectionState.Connection.Dispose();
                _factory.Dispose();
            }
        }
    }
}
