using Api.Controllers;
using Moq;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.service;
using XUnitTest.TestHelpers;

namespace XUnitTest.PdfGenerator
{
    public class PdfGeneratorControllerTests
    {
        private readonly Mock<IPdfGeneratorService> _pdfGeneratorService = new();
        private readonly PdfGeneratorController _controller;

        public PdfGeneratorControllerTests()
        {
            _controller = new PdfGeneratorController(
                ControllerTestHelper.CreateChangeControllerContext(),
                _pdfGeneratorService.Object);
        }

        [Fact]
        public async Task MergePdfs_Returns_Service_Response()
        {
            var request = new MergePdfsRequest();
            var response = new MergePdfsResponse();
            _pdfGeneratorService.Setup(s => s.MergePdfsAsync(request)).ReturnsAsync(response);

            var result = await _controller.MergePdfs(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task CreatePdfsFromHtml_Returns_Service_Response()
        {
            var request = new CreatePdfsFromHtmlRequest();
            var response = new CreatePdfsFromHtmlResponse();
            _pdfGeneratorService.Setup(s => s.CreatePdfsFromHtmlAsync(request)).ReturnsAsync(response);

            var result = await _controller.CreatePdfsFromHtml(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task CreatePdfsFromHtmlUsingTemplateEngine_Returns_Service_Response()
        {
            var request = new CreatePdfsFromHtmlUsingTERequest();
            var response = new CreatePdfsFromHtmlUsingTEResponse();
            _pdfGeneratorService.Setup(s => s.CreatePdfsFromHtmlUsingTEAsync(request)).ReturnsAsync(response);

            var result = await _controller.CreatePdfsFromHtmlUsingTemplateEngine(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task CreatePdfsFromHtmlUsingTemplateEngineBulk_Returns_Service_Response()
        {
            var request = new CreatePdfsFromHtmlUsingTEBulkRequest();
            var response = new CreatePdfsFromHtmlUsingTEBulkResponse();
            _pdfGeneratorService.Setup(s => s.CreatePdfsFromHtmlUsingTEBulkAsync(request)).ReturnsAsync(response);

            var result = await _controller.CreatePdfsFromHtmlUsingTemplateEngineBulk(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task FixPdfs_Returns_Service_Response()
        {
            var request = new FixPdfsRequest();
            var response = new FixPdfsResponse();
            _pdfGeneratorService.Setup(s => s.FixPdfsAsync(request)).ReturnsAsync(response);

            var result = await _controller.FixPdfs(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task StampImageToPdf_Returns_Service_Response()
        {
            var request = new StampImageToPdfRequest();
            var response = new StampImageToPdfResponse();
            _pdfGeneratorService.Setup(s => s.StampImageToPdfAsync(request)).ReturnsAsync(response);

            var result = await _controller.StampImageToPdf(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task StampTextToPdf_Returns_Service_Response()
        {
            var request = new StampTextToPdfRequest();
            var response = new StampTextToPdfResponse();
            _pdfGeneratorService.Setup(s => s.StampTextToPdfAsync(request)).ReturnsAsync(response);

            var result = await _controller.StampTextToPdf(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task StampIntoPdf_Returns_Service_Response()
        {
            var request = new StampIntoPdfRequest();
            var response = new StampIntoPdfResponse();
            _pdfGeneratorService.Setup(s => s.StampIntoPdfAsync(request)).ReturnsAsync(response);

            var result = await _controller.StampIntoPdf(request);

            Assert.Same(response, result);
        }
    }
}
