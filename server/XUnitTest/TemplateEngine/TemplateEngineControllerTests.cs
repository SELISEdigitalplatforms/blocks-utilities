using Api.Controllers;
using Moq;
using Utility.DomainService.TemplateEngine;
using Utility.DomainService.TemplateEngine.service;
using XUnitTest.TestHelpers;

namespace XUnitTest.TemplateEngine
{
    public class TemplateEngineControllerTests
    {
        private readonly Mock<ITemplateEngineService> _templateEngineService = new();
        private readonly TemplateEngineController _controller;

        public TemplateEngineControllerTests()
        {
            _controller = new TemplateEngineController(
                ControllerTestHelper.CreateChangeControllerContext(),
                _templateEngineService.Object);
        }

        [Fact]
        public async Task RenderWithJSON_Returns_Service_Response()
        {
            var request = new RenderWithJsonRequest();
            var response = new RenderWithJsonResponse();
            _templateEngineService.Setup(s => s.RenderWithJsonAsync(request)).ReturnsAsync(response);

            var result = await _controller.RenderWithJSON(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task RenderWithJSONBulk_Returns_Service_Response()
        {
            var request = new RenderWithJsonBulkRequest();
            var response = new RenderWithJsonBulkResponse();
            _templateEngineService.Setup(s => s.RenderWithJsonBulkAsync(request)).ReturnsAsync(response);

            var result = await _controller.RenderWithJSONBulk(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task GenerateRenderedFile_Returns_Service_Response()
        {
            var request = new GenerateRenderedFileRequest();
            var response = new GenerateRenderedFileResponse();
            _templateEngineService.Setup(s => s.GenerateRenderedFileAsync(request)).ReturnsAsync(response);

            var result = await _controller.GenerateRenderedFile(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task GenerateRenderedFileBulk_Returns_Service_Response()
        {
            var request = new GenerateRenderedFilesBulkRequest();
            var response = new GenerateRenderedFilesBulkResponse();
            _templateEngineService.Setup(s => s.GenerateRenderedFilesBulkAsync(request)).ReturnsAsync(response);

            var result = await _controller.GenerateRenderedFileBulk(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task CreateFileWithFilteredMongoQuery_Returns_Service_Response()
        {
            var request = new CreateFileWithFilteredMongoQueryRequest();
            var response = new CreateFileWithFilteredMongoQueryResponse();
            _templateEngineService.Setup(s => s.CreateFileWithFilteredMongoQueryAsync(request)).ReturnsAsync(response);

            var result = await _controller.CreateFileWithFilteredMongoQuery(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task CreateFileWithFilteredMongoQueryBulk_Returns_Service_Response()
        {
            var request = new CreateFileWithFilteredMongoQueryBulkRequest();
            var response = new CreateFileWithFilteredMongoQueryBulkResponse();
            _templateEngineService.Setup(s => s.CreateFileWithFilteredMongoQueryBulkAsync(request)).ReturnsAsync(response);

            var result = await _controller.CreateFileWithFilteredMongoQueryBulk(request);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task CreateMultipleFileWithFilteredMongoQuery_Returns_Service_Response()
        {
            var request = new CreateMultipleFileWithFilteredMongoQueryRequest();
            var response = new CreateMultipleFileWithFilteredMongoQueryResponse();
            _templateEngineService.Setup(s => s.CreateMultipleFileWithFilteredMongoQueryAsync(request)).ReturnsAsync(response);

            var result = await _controller.CreateMultipleFileWithFilteredMongoQuery(request);

            Assert.Same(response, result);
        }
    }
}
