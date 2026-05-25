using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.TemplateEngine.service;

namespace XUnitTest.TemplateEngine
{
    public class TemplateRenderingServiceTests
    {
        private readonly TemplateRenderingService _service;

        public TemplateRenderingServiceTests()
        {
            var loggerMock = new Mock<ILogger<TemplateRenderingService>>();
            _service = new TemplateRenderingService(loggerMock.Object);
        }

        [Fact]
        public void RenderTemplateWithJson_RendersSimpleTemplate()
        {
            var template = "Hello {{ name }}!";
            var json = "{ \"name\": \"Mahmud\" }";

            var result = _service.RenderTemplateWithJson(template, json);

            Assert.Equal("Hello Mahmud!", result.Trim());
        }

        [Fact]
        public void RenderTemplateWithJson_AddsSecurityToken()
        {
            var template = "{{ securityToken }}";
            var json = "{}";

            var result = _service.RenderTemplateWithJson(template, json);

            Assert.Equal("", result); // securityToken defaults to empty string
        }

        [Fact]
        public void RenderTemplateWithJson_HandlesEmptyJson()
        {
            var template = "Test";
            var json = "{}";

            var result = _service.RenderTemplateWithJson(template, json);

            Assert.Equal("Test", result);
        }

        [Fact]
        public void RenderTemplateWithEntityData_RendersEntities()
        {
            var template = "User: {{ user }}";

            var entities = new Dictionary<string, object>
            {
                { "user", "Hasan" }
            };

            var metadata = new Dictionary<string, object>();

            var result = _service.RenderTemplateWithEntityData(template, entities, metadata);

            Assert.Equal("User: Hasan", result.Trim());
        }

        [Fact]
        public void RenderTemplateWithEntityData_RendersMetadata()
        {
            var template = "Version: {{ version }}";

            var entities = new Dictionary<string, object>();
            var metadata = new Dictionary<string, object>
            {
                { "version", "1.0" }
            };

            var result = _service.RenderTemplateWithEntityData(template, entities, metadata);

            Assert.Equal("Version: 1.0", result.Trim());
        }

        [Fact]
        public void RenderTemplateWithEntityData_MergesData()
        {
            var template = "{{ name }} - {{ type }}";

            var entities = new Dictionary<string, object>
            {
                { "name", "Invoice" }
            };

            var metadata = new Dictionary<string, object>
            {
                { "type", "PDF" }
            };

            var result = _service.RenderTemplateWithEntityData(template, entities, metadata);

            Assert.Equal("Invoice - PDF", result.Trim());
        }

        [Fact]
        public void RenderTemplateWithEntityData_MetadataOverridesEntity()
        {
            var template = "{{ value }}";

            var entities = new Dictionary<string, object>
            {
                { "value", "EntityValue" }
            };

            var metadata = new Dictionary<string, object>
            {
                { "value", "MetadataValue" }
            };

            var result = _service.RenderTemplateWithEntityData(template, entities, metadata);

            Assert.Equal("MetadataValue", result.Trim());
        }

        [Fact]
        public void RenderTemplateWithEntityData_AddsSecurityToken()
        {
            var template = "{{ securityToken }}";

            var result = _service.RenderTemplateWithEntityData(
                template,
                new Dictionary<string, object>(),
                new Dictionary<string, object>());

            Assert.Equal("", result);
        }

    }

}
