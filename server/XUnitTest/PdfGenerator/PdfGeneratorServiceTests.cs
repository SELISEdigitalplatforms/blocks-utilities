using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class PdfGeneratorServiceTests
    {
        private readonly Mock<ILogger<PdfGeneratorService>> _logger;
        private readonly Mock<IMessageClient> _messageClient;
        private readonly PdfGeneratorService _service;

        public PdfGeneratorServiceTests()
        {
            _logger = new Mock<ILogger<PdfGeneratorService>>();
            _messageClient = new Mock<IMessageClient>();

            _service = new PdfGeneratorService(
                _logger.Object,
                _messageClient.Object);
        }

        #region MergePdfsAsync

        [Fact]
        public async Task MergePdfsAsync_Success_ReturnsSuccessResponse()
        {
            var request = new MergePdfsRequest
            {
                OutputPdfFileId = "out-1"
            };

            _messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<MergePdfsEvent>>()))
                .Returns(Task.CompletedTask);

            var result = await _service.MergePdfsAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("out-1", result.OutputPdfFileId);

            _messageClient.Verify(
                m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<MergePdfsEvent>>()),
                Times.Once);
        }

        [Fact]
        public async Task MergePdfsAsync_WhenException_ReturnsFailureResponse()
        {
            var request = new MergePdfsRequest
            {
                OutputPdfFileId = "out-1"
            };

            _messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<MergePdfsEvent>>()))
                .ThrowsAsync(new Exception("boom"));

            var result = await _service.MergePdfsAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Contains("boom", result.Message);
        }

        #endregion

        #region CreatePdfsFromHtmlAsync

        [Fact]
        public async Task CreatePdfsFromHtmlAsync_Success_ReturnsSuccess()
        {
            var request = new CreatePdfsFromHtmlRequest
            {
                MessageCoRelationId = "corr-1"
            };

            _messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreatePdfsFromHtmlEvent>>()))
                .Returns(Task.CompletedTask);

            var result = await _service.CreatePdfsFromHtmlAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("corr-1", result.MessageCoRelationId);
        }

        [Fact]
        public async Task CreatePdfsFromHtmlAsync_WhenException_ReturnsFailure()
        {
            var request = new CreatePdfsFromHtmlRequest
            {
                MessageCoRelationId = "corr-1"
            };

            _messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreatePdfsFromHtmlEvent>>()))
                .ThrowsAsync(new Exception("error"));

            var result = await _service.CreatePdfsFromHtmlAsync(request);

            Assert.False(result.IsSuccess);
        }

        #endregion

        #region FixPdfsAsync

        [Fact]
        public async Task FixPdfsAsync_WhenPdfInfosNull_ReturnsFailure_WithoutSendingEvent()
        {
            var request = new FixPdfsRequest
            {
                MessageCorrelationId = "corr-1",
                PdfInfos = null
            };

            var result = await _service.FixPdfsAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal("PdfInfos cannot be null or empty", result.Message);

            _messageClient.Verify(
                m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<FixPdfsEvent>>()),
                Times.Never);
        }

        [Fact]
        public async Task FixPdfsAsync_WhenPdfInfosEmpty_ReturnsFailure()
        {
            var request = new FixPdfsRequest
            {
                MessageCorrelationId = "corr-1",
                PdfInfos = new List<FixPdfCommand>() // Corrected type to match FixPdfsRequest.PdfInfos
            };

            var result = await _service.FixPdfsAsync(request);

            Assert.False(result.IsSuccess);
            _messageClient.Verify(
                m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<FixPdfsEvent>>()),
                Times.Never);
        }

        [Fact]
        public async Task FixPdfsAsync_Success_ReturnsSuccess()
        {
            var request = new FixPdfsRequest
            {
                MessageCorrelationId = "corr-1",
                PdfInfos = new List<FixPdfCommand>
               {
                   new FixPdfCommand
                   {
                       OriginalPdfId = "original-1",
                       OutputPdfId = "output-1"
                   }
               }
            };

            _messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<FixPdfsEvent>>()))
                .Returns(Task.CompletedTask);

            var result = await _service.FixPdfsAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("corr-1", result.MessageCorrelationId);
        }

        #endregion

        #region Stamp APIs (pattern-based tests)

        [Fact]
        public async Task StampImageToPdfAsync_Success()
        {
            var request = new StampImageToPdfRequest
            {
                OutputPdfFileId = "pdf-1"
            };

            _messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<StampImageToPdfEvent>>()))
                .Returns(Task.CompletedTask);

            var result = await _service.StampImageToPdfAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("pdf-1", result.OutputPdfFileId);
        }

        [Fact]
        public async Task StampTextToPdfAsync_Success()
        {
            var request = new StampTextToPdfRequest
            {
                OutputPdfFileId = "pdf-2"
            };

            _messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<StampTextToPdfEvent>>()))
                .Returns(Task.CompletedTask);

            var result = await _service.StampTextToPdfAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("pdf-2", result.OutputPdfFileId);
        }

        [Fact]
        public async Task StampIntoPdfAsync_Success()
        {
            var request = new StampIntoPdfRequest
            {
                OutputPdfFileId = "pdf-3"
            };

            _messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<StampIntoPdfEvent>>()))
                .Returns(Task.CompletedTask);

            var result = await _service.StampIntoPdfAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("pdf-3", result.OutputPdfFileId);
        }

        #endregion
    }
}
