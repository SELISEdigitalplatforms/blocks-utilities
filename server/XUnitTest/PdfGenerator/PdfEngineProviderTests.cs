using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class PdfEngineProviderTests
    {
        private readonly PuppeteerSharpEngine _puppeteer;
        private readonly PdfSharpCoreEngine _pdfSharp;
        private readonly AsposePdfEngine _aspose;
        private readonly WkHtmlToPdfEngine _wkHtml;
        private readonly PdfEngineProvider _provider;

        public PdfEngineProviderTests()
        {
            var mockConfiguration = Mock.Of<IConfiguration>();
            _puppeteer = new PuppeteerSharpEngine(Mock.Of<ILogger<PuppeteerSharpEngine>>(), mockConfiguration);
            _pdfSharp = new PdfSharpCoreEngine(Mock.Of<ILogger<PdfSharpCoreEngine>>());
            _aspose = new AsposePdfEngine(Mock.Of<ILogger<AsposePdfEngine>>());
            _wkHtml = new WkHtmlToPdfEngine(Mock.Of<ILogger<WkHtmlToPdfEngine>>(), mockConfiguration);

            _provider = new PdfEngineProvider(
                _puppeteer,
                _pdfSharp,
                _aspose,
                _wkHtml);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void GetEngine_ValidEngineNumber_ReturnsCorrectInstance(int engineNumber)
        {
            var result = _provider.GetEngine(engineNumber);

            Assert.NotNull(result);

            switch (engineNumber)
            {
                case 1:
                    Assert.Same(_puppeteer, result);
                    break;
                case 2:
                    Assert.Same(_pdfSharp, result);
                    break;
                case 3:
                    Assert.Same(_aspose, result);
                    break;
                case 4:
                    Assert.Same(_wkHtml, result);
                    break;
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(-1)]
        [InlineData(99)]
        public void GetEngine_InvalidEngineNumber_ThrowsArgumentException(int engineNumber)
        {
            var ex = Assert.Throws<ArgumentException>(() => _provider.GetEngine(engineNumber));

            Assert.Contains("Invalid engine number", ex.Message);
        }
    }
}
