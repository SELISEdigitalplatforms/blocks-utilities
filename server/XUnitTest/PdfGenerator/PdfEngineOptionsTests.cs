using FluentAssertions;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class PdfEngineOptionsTests
    {
        [Fact]
        public void PdfGenerationOptions_ShouldStoreAllProperties()
        {
            var profile = new PdfUtilityProfile { Id = "p1" };
            var options = new PdfGenerationOptions
            {
                HeaderHtml = "<h1>Header</h1>",
                FooterHtml = "<footer>Footer</footer>",
                FirstPageHeaderHtml = "first-h",
                FirstPageFooterHtml = "first-f",
                HeaderHeight = 10,
                FooterHeight = 12,
                IsPageNumberEnabled = true,
                IsTotalPageCountEnabled = true,
                UseFormatting = true,
                OpenInBrowser = true,
                ProfileId = "profile-1",
                PageNumberText = "{page}/{total}",
                Profile = profile
            };

            options.HeaderHtml.Should().Contain("Header");
            options.FooterHeight.Should().Be(12);
            options.Profile!.Id.Should().Be("p1");
        }

        [Fact]
        public void ImageStampOptions_ShouldStoreAllProperties()
        {
            var options = new ImageStampOptions
            {
                XPosition = 1,
                YPosition = 2,
                Width = 100,
                Height = 200,
                Rotation = 45,
                Opacity = 0.5,
                PageNumbers = new List<int> { 1, 2 },
                IsBackground = true
            };

            options.PageNumbers.Should().ContainInOrder(1, 2);
            options.IsBackground.Should().BeTrue();
        }

        [Fact]
        public void TextStampOptions_And_Coordinate_ShouldStoreAllProperties()
        {
            var text = new TextStampOptions
            {
                Text = "hello",
                XPosition = 1,
                YPosition = 2,
                FontName = "Arial",
                FontSize = 12,
                FontColor = "#000000",
                Rotation = 0,
                Opacity = 0.9,
                PageNumbers = new List<int> { 3 },
                IsBackground = false,
                IsBold = true,
                IsItalic = true
            };

            var coordinate = new Coordinate
            {
                PageNumber = 1,
                X = 10,
                Y = 20,
                Width = 30,
                Height = 40
            };

            text.IsBold.Should().BeTrue();
            text.IsItalic.Should().BeTrue();
            coordinate.Height.Should().Be(40);
        }
    }
}
