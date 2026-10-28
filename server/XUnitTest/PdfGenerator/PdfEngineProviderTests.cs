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
        private readonly PdfEngineProvider _provider;

        public PdfEngineProviderTests()
        {
            var mockConfiguration = Mock.Of<IConfiguration>();
            _puppeteer = new PuppeteerSharpEngine(Mock.Of<ILogger<PuppeteerSharpEngine>>(), mockConfiguration);
            _pdfSharp = new PdfSharpCoreEngine(Mock.Of<ILogger<PdfSharpCoreEngine>>());
            _aspose = new AsposePdfEngine(Mock.Of<ILogger<AsposePdfEngine>>(), mockConfiguration);

            _provider = new PdfEngineProvider(
                _puppeteer,
                _pdfSharp,
                _aspose);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
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

        [Fact]
        public void WkHtmlToPdf_IsTurnedOff_BecauseItRunsScriptsInCallerHtml()
        {
            var ex = Assert.Throws<ArgumentException>(() => _provider.GetEngine(4));

            Assert.Contains("turned off", ex.Message);
        }
    }
}
