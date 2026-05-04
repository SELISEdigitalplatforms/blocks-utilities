using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.TemplateEngine;
using Utility.DomainService.TemplateEngine.service;

namespace XUnitTest.TemplateEngine
{
    public class TemplateEngineServiceTests
    {
        private readonly Mock<ILogger<TemplateEngineService>> _mockLogger;
        private readonly Mock<ITemplateEngineRepository> _mockRepository;
        private readonly Mock<IMessageClient> _mockMessageClient;
        private readonly TemplateEngineService _service;

        public TemplateEngineServiceTests()
        {
            _mockLogger = new Mock<ILogger<TemplateEngineService>>();
            _mockRepository = new Mock<ITemplateEngineRepository>();
            _mockMessageClient = new Mock<IMessageClient>();
            _service = new TemplateEngineService(
                _mockLogger.Object,
                _mockMessageClient.Object);
        }

        #region RenderWithJsonAsync Tests

        [Fact]
        public async Task RenderWithJsonAsync_ValidJson_ShouldReturnSuccess()
        {
            // Arrange
            var request = new RenderWithJsonRequest
            {
                RenderedFileId = "file-123",
                JSONString = "{\"name\":\"test\"}"
            };

            // Act
            var result = await _service.RenderWithJsonAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.RenderedFileId.Should().Be("file-123");
            result.Message.Should().Contain("queued successfully");
        }

        [Fact]
        public async Task RenderWithJsonAsync_InvalidJson_ShouldReturnError()
        {
            // Arrange
            var request = new RenderWithJsonRequest
            {
                RenderedFileId = "file-123",
                JSONString = "not valid json {"
            };

            // Act
            var result = await _service.RenderWithJsonAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Message.Should().Contain("Invalid JSON");
        }

        [Fact]
        public async Task RenderWithJsonAsync_EmptyJson_ShouldReturnError()
        {
            // Arrange
            var request = new RenderWithJsonRequest
            {
                RenderedFileId = "file-123",
                JSONString = ""
            };

            // Act
            var result = await _service.RenderWithJsonAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task RenderWithJsonAsync_NullJson_ShouldReturnError()
        {
            // Arrange
            var request = new RenderWithJsonRequest
            {
                RenderedFileId = "file-123",
                JSONString = null!
            };

            // Act
            var result = await _service.RenderWithJsonAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
        }

        #endregion

        #region RenderWithJsonBulkAsync Tests

        [Fact]
        public async Task RenderWithJsonBulkAsync_AllValidJson_ShouldReturnSuccess()
        {
            // Arrange
            var request = new RenderWithJsonBulkRequest
            {
                ReferenceId = "ref-123",
                Payloads = new List<RenderWithJsonPayload>
                {
                    new() { RenderedFileId = "file-1", JSONString = "{\"a\":1}" },
                    new() { RenderedFileId = "file-2", JSONString = "{\"b\":2}" }
                }
            };

            // Act
            var result = await _service.RenderWithJsonBulkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.ReferenceId.Should().Be("ref-123");
        }

        [Fact]
        public async Task RenderWithJsonBulkAsync_OneInvalidJson_ShouldReturnError()
        {
            // Arrange
            var request = new RenderWithJsonBulkRequest
            {
                ReferenceId = "ref-123",
                Payloads = new List<RenderWithJsonPayload>
                {
                    new() { RenderedFileId = "file-1", JSONString = "{\"valid\":true}" },
                    new() { RenderedFileId = "file-2", JSONString = "invalid json" }
                }
            };

            // Act
            var result = await _service.RenderWithJsonBulkAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Message.Should().Contain("file-2");
        }

        [Fact]
        public async Task RenderWithJsonBulkAsync_EmptyPayloads_ShouldReturnSuccess()
        {
            // Arrange
            var request = new RenderWithJsonBulkRequest
            {
                ReferenceId = "ref-123",
                Payloads = new List<RenderWithJsonPayload>()
            };

            // Act
            var result = await _service.RenderWithJsonBulkAsync(request);

            // Assert - empty payloads should be handled
            result.Should().NotBeNull();
        }

        #endregion

        #region GenerateRenderedFileAsync Tests

        [Fact]
        public async Task GenerateRenderedFileAsync_ValidRequest_ShouldReturnSuccess()
        {
            // Arrange
            var request = new GenerateRenderedFileRequest
            {
                FileId = "file-456"
            };

            // Act
            var result = await _service.GenerateRenderedFileAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.FileId.Should().Be("file-456");
            result.Message.Should().Contain("queued successfully");
        }

        [Fact]
        public async Task GenerateRenderedFileAsync_WithContext_ShouldReturnSuccess()
        {
            // Arrange
            var request = new GenerateRenderedFileRequest
            {
                FileId = "file-with-context"
            };

            // Act
            var result = await _service.GenerateRenderedFileAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
        }

        #endregion

        #region GenerateRenderedFilesBulkAsync Tests

        [Fact]
        public async Task GenerateRenderedFilesBulkAsync_ValidRequest_ShouldReturnSuccess()
        {
            // Arrange
            var request = new GenerateRenderedFilesBulkRequest
            {
                GenerateRenderedFileRequests = new List<GenerateRenderedFileRequest>
                {
                    new() { FileId = "file-1" },
                    new() { FileId = "file-2" }
                }
            };

            // Act
            var result = await _service.GenerateRenderedFilesBulkAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Message.Should().Contain("queued successfully");
        }

        [Fact]
        public async Task GenerateRenderedFilesBulkAsync_EmptyList_ShouldReturnSuccess()
        {
            // Arrange
            var request = new GenerateRenderedFilesBulkRequest
            {
                GenerateRenderedFileRequests = new List<GenerateRenderedFileRequest>()
            };

            // Act
            var result = await _service.GenerateRenderedFilesBulkAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
        }

        #endregion

        #region Request Model Tests

        [Fact]
        public void RenderWithJsonRequest_ShouldStoreAllProperties()
        {
            // Arrange & Act
            var request = new RenderWithJsonRequest
            {
                RenderedFileId = "file-id",
                JSONString = "{\"key\":\"value\"}"
            };

            // Assert
            request.RenderedFileId.Should().Be("file-id");
            request.JSONString.Should().Contain("key");
        }

        [Fact]
        public void RenderWithJsonBulkRequest_ShouldStoreAllProperties()
        {
            // Arrange & Act
            var request = new RenderWithJsonBulkRequest
            {
                ReferenceId = "ref-id",
                Payloads = new List<RenderWithJsonPayload>
                {
                    new() { RenderedFileId = "f1", JSONString = "{}" }
                }
            };

            // Assert
            request.ReferenceId.Should().Be("ref-id");
            request.Payloads.Should().HaveCount(1);
        }

        [Fact]
        public void GenerateRenderedFileRequest_ShouldStoreFileId()
        {
            // Arrange & Act
            var request = new GenerateRenderedFileRequest
            {
                FileId = "test-file-id"
            };

            // Assert
            request.FileId.Should().Be("test-file-id");
        }

        [Fact]
        public void GenerateRenderedFilesBulkRequest_ShouldStoreRequests()
        {
            // Arrange & Act
            var request = new GenerateRenderedFilesBulkRequest
            {
                GenerateRenderedFileRequests = new List<GenerateRenderedFileRequest>
                {
                    new() { FileId = "f1" },
                    new() { FileId = "f2" }
                }
            };

            // Assert
            request.GenerateRenderedFileRequests.Should().HaveCount(2);
        }

        #endregion
    }
}
