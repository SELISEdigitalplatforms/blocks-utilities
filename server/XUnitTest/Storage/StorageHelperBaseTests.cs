using System.Net;
using System.Net.Sockets;
using System.Text;
using DomainService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.Storage;

namespace XUnitTest.Storage
{
    public class StorageHelperBaseTests
    {
        [Fact]
        public async Task GetFileStreamFromUrl_ShouldReturnStream_WhenResponseIsSuccessful()
        {
            var helper = new TestStorageHelper(new Mock<ILogger>().Object);
            var (url, serverTask) = StartServer(HttpStatusCode.OK, "hello-world");

            var stream = await helper.Download(url);
            await serverTask;

            stream.Should().NotBeNull();
            using var reader = new StreamReader(stream!);
            (await reader.ReadToEndAsync()).Should().Be("hello-world");
        }

        [Fact]
        public async Task GetFileStreamFromUrl_ShouldReturnNull_WhenResponseIsNotSuccessful()
        {
            var helper = new TestStorageHelper(new Mock<ILogger>().Object);
            var (url, serverTask) = StartServer(HttpStatusCode.InternalServerError, "failure");

            var stream = await helper.Download(url);
            await serverTask;

            stream.Should().BeNull();
        }

        private static (string url, Task serverTask) StartServer(HttpStatusCode statusCode, string responseBody)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var networkStream = client.GetStream();
                using var reader = new StreamReader(networkStream, Encoding.ASCII, leaveOpen: true);

                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                }

                var responseBytes = Encoding.UTF8.GetBytes(responseBody);
                var responseHeader = $"HTTP/1.1 {(int)statusCode} {statusCode}\r\nContent-Length: {responseBytes.Length}\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(responseHeader);

                await networkStream.WriteAsync(headerBytes);
                await networkStream.WriteAsync(responseBytes);
                await networkStream.FlushAsync();

                listener.Stop();
            });

            return ($"http://127.0.0.1:{port}/test", serverTask);
        }

        private sealed class TestStorageHelper : StorageHelperBase
        {
            public TestStorageHelper(ILogger logger)
                : base(logger)
            {
            }

            public Task<Stream?> Download(string fileUrl) => GetFileStreamFromUrl(fileUrl);
        }
    }
}
