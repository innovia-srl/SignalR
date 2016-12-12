// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
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
                var endPointTask = endPoint.OnConnectedAsync(connectionWrapper.Connection);

                await connectionWrapper.HttpConnection.Input.ReadingStarted;

                // kill the connection
                connectionWrapper.Connection.Channel.Dispose();

                await endPointTask;

                Assert.Equal(2, trackDispose.DisposeCount);
            }
        }

        [Fact]
        public async Task OnDisconnectedCalledWithExceptionIfHubMethodNotFound()
        {
            var hub = Mock.Of<Hub>();

            var endPointType = GetEndPointType(hub.GetType());
            var serviceProvider = CreateServiceProvider(s =>
            {
                s.AddSingleton(endPointType);
                s.AddTransient(hub.GetType(), sp => hub);
            });

            dynamic endPoint = serviceProvider.GetService(endPointType);

            using (var connectionWrapper = new ConnectionWrapper())
            {
                var endPointTask = endPoint.OnConnectedAsync(connectionWrapper.Connection);

                await connectionWrapper.HttpConnection.Input.ReadingStarted;

                var buffer = connectionWrapper.HttpConnection.Input.Alloc();
                buffer.Write(Encoding.UTF8.GetBytes("0xdeadbeef"));
                await buffer.FlushAsync();

                connectionWrapper.Connection.Channel.Dispose();

                await endPointTask;

                Mock.Get(hub).Verify(h => h.OnDisconnectedAsync(It.IsNotNull<Exception>()), Times.Once());
            }
        }

        private static Type GetEndPointType(Type hubType)
        {
            var endPointType = typeof(HubEndPoint<>);
            return endPointType.MakeGenericType(hubType);
        }

        [Fact]
        public async Task CanReturnValuesFromMethodsOnHub()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var connectionWrapper = new ConnectionWrapper())
            {
                var endPointTask = endPoint.OnConnectedAsync(connectionWrapper.Connection);

                await connectionWrapper.HttpConnection.Input.ReadingStarted;

                var invocationAdapter = serviceProvider.GetService<InvocationAdapterRegistry>();
                var writer = invocationAdapter.GetInvocationAdapter("json");
                var serializer = new JsonSerializer();

                // return Task<int>
                await SendRequest(connectionWrapper.HttpConnection, writer, "TaskValueMethod");
                var methodResult = await connectionWrapper.HttpConnection.Output.ReadAsync();
                var res = serializer.Deserialize<InvocationResultDescriptor>(new JsonTextReader(new StreamReader(new MemoryStream(methodResult.Buffer.ToArray()))));
                connectionWrapper.HttpConnection.Output.AdvanceReader(methodResult.Buffer.End, methodResult.Buffer.End);
                Assert.Equal(10.ToString(), res.Result.ToString());

                // return int
                await SendRequest(connectionWrapper.HttpConnection, writer, "ValueMethod");
                methodResult = await connectionWrapper.HttpConnection.Output.ReadAsync();
                res = new JsonSerializer().Deserialize<InvocationResultDescriptor>(new JsonTextReader(new StreamReader(new MemoryStream(methodResult.Buffer.ToArray()))));
                connectionWrapper.HttpConnection.Output.AdvanceReader(methodResult.Buffer.End, methodResult.Buffer.End);
                Assert.Equal(11.ToString(), res.Result.ToString());

                // kill the connection
                connectionWrapper.Connection.Channel.Dispose();

                await endPointTask;
            }
        }

        [Fact]
        public async Task BroadcastHubMethod_SendsToAllClients()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var firstConnection = new ConnectionWrapper())
            using (var secondConnection = new ConnectionWrapper())
            {
                var firstEndPointTask = endPoint.OnConnectedAsync(firstConnection.Connection);
                var secondEndPointTask = endPoint.OnConnectedAsync(secondConnection.Connection);

                await firstConnection.HttpConnection.Input.ReadingStarted;

                var invocationAdapter = serviceProvider.GetService<InvocationAdapterRegistry>();
                var writer = invocationAdapter.GetInvocationAdapter("json");
                var serializer = new JsonSerializer();

                await SendRequest(firstConnection.HttpConnection, writer, "BroadcastMethod", "test");

                var methodResult = await firstConnection.HttpConnection.Output.ReadAsync();
                var res = serializer.Deserialize<InvocationDescriptor>(new JsonTextReader(new StreamReader(new MemoryStream(methodResult.Buffer.ToArray()))));
                firstConnection.HttpConnection.Output.AdvanceReader(methodResult.Buffer.End, methodResult.Buffer.End);
                Assert.Equal("Broadcast", res.Method);
                Assert.Equal(1, res.Arguments.Length);
                Assert.Equal("test", res.Arguments[0]);

                methodResult = await secondConnection.HttpConnection.Output.ReadAsync();
                res = serializer.Deserialize<InvocationDescriptor>(new JsonTextReader(new StreamReader(new MemoryStream(methodResult.Buffer.ToArray()))));
                secondConnection.HttpConnection.Output.AdvanceReader(methodResult.Buffer.End, methodResult.Buffer.End);
                Assert.Equal("Broadcast", res.Method);
                Assert.Equal(1, res.Arguments.Length);
                Assert.Equal("test", res.Arguments[0]);

                // kill the connections
                firstConnection.Connection.Channel.Dispose();
                secondConnection.Connection.Channel.Dispose();

                await firstEndPointTask;
                await secondEndPointTask;
            }
        }

        private class MethodHub : Hub
        {
            public Task BroadcastMethod(string message)
            {
                return Clients.All.InvokeAsync("Broadcast", message);
            }

            public Task<int> TaskValueMethod()
            {
                return Task.FromResult(10);
            }

            public int ValueMethod()
            {
                return 11;
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

        public async Task SendRequest(HttpConnection connection, IInvocationAdapter writer, string method, params object[] args)
        {
            if (connection == null)
            {
                throw new Exception();
            }

            var stream = new MemoryStream();
            await writer.WriteInvocationDescriptorAsync(new InvocationDescriptor
            {
                Arguments = args,
                Method = method
            }, stream);

            var buffer = connection.Input.Alloc();
            buffer.Write(stream.ToArray());
            await buffer.FlushAsync();
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
            private HttpConnection _httpConnection;

            public Connection Connection;
            public HttpConnection HttpConnection => (HttpConnection)Connection.Channel;

            public ConnectionWrapper(string format = "json")
            {
                _factory = new PipelineFactory();
                _httpConnection = new HttpConnection(_factory);

                var connectionManager = new ConnectionManager();

                Connection = connectionManager.AddNewConnection(_httpConnection).Connection;
                Connection.Metadata["formatType"] = format;
                Connection.User = new ClaimsPrincipal(new ClaimsIdentity());
            }

            public void Dispose()
            {
                Connection.Channel.Dispose();
                _httpConnection.Dispose();
                _factory.Dispose();
            }
        }
    }
}
