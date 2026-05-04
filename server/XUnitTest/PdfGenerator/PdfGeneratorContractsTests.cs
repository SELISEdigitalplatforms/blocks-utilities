using FluentAssertions;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.Entities;

namespace XUnitTest.PdfGenerator
{
    public class PdfGeneratorContractsTests
    {
        [Fact]
        public void ExtractTextFromPdfsRequest_And_Response_ShouldStoreValues()
        {
            var request = new ExtractTextFromPdfsRequest
            {
                ProjectKey = "p1",
                MessageCoRelationId = "corr",
                Engine = 2,
                EventReferenceData = new Dictionary<string, string> { ["source"] = "test" },
                ExtractTextCommands = new List<ExtractTextCommand>
                {
                    new() { PdfFileId = "pdf-1", RecordId = "rec-1" }
                }
            };

            var response = new ExtractTextFromPdfsResponse
            {
                IsSuccess = true,
                MessageCoRelationId = "corr",
                Message = "ok"
            };

            request.ExtractTextCommands.Should().ContainSingle();
            response.Message.Should().Be("ok");
        }

        [Fact]
        public void CreatePdfsFromHtmlRequest_ShouldCalculateHeaderFooterFlags()
        {
            var command = new CreateFromHtmlCommand
            {
                HtmlFileId = "html-1",
                HeaderHtmlFileId = "header",
                FooterHtmlFileId = "footer",
                FirstPageHeaderFileId = "first-header",
                FirstPageFooterFileId = "first-footer"
            };

            var request = new CreatePdfsFromHtmlRequest
            {
                MessageCoRelationId = "corr",
                CreateFromHtmlCommands = new List<CreateFromHtmlCommand> { command }
            };

            command.HasHeader.Should().BeTrue();
            command.HasFooter.Should().BeTrue();
            command.HasFirstPageHeader.Should().BeTrue();
            command.HasFirstPageFooter.Should().BeTrue();
            request.Engine.Should().Be(1);
        }

        [Fact]
        public void CreatePdfsFromHtmlUsingTERequest_ShouldStoreNestedMetadata()
        {
            var command = new CreateFromHtmlUsingTECommand
            {
                TemplateFileId = "tpl-1",
                FilteredSqlQueryDatas = new List<GetFilteredSqlQueryData>
                {
                    new() { EntityName = "EntityA", FilterQuery = "id=@id", FilterParameters = new Dictionary<string, object> { ["id"] = 5 } }
                },
                MetaDataList = new List<PdfMetaData>
                {
                    new() { Key = "Author", Value = "Tester" }
                }
            };

            var request = new CreatePdfsFromHtmlUsingTERequest
            {
                ProjectKey = "p1",
                MessageCoRelationId = "corr-te",
                CreateFromHtmlCommands = new List<CreateFromHtmlUsingTECommand> { command }
            };

            var response = new CreatePdfsFromHtmlUsingTEResponse
            {
                IsSuccess = true,
                MessageCoRelationId = "corr-te",
                Message = "done"
            };

            request.CreateFromHtmlCommands[0].MetaDataList!.Should().ContainSingle();
            response.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public void PdfEntities_ShouldStoreValues()
        {
            var dump = new PdfExtractDump
            {
                Id = "id1",
                Text = "content",
                MessageCorrelationId = "corr",
                PdfId = "pdf1",
                ItemId = "item1",
                TenantId = "tenant1",
                CreatedBy = "u1",
                CreateDate = DateTime.UtcNow,
                LastUpdatedBy = "u2",
                LastUpdateDate = DateTime.UtcNow,
                Tags = new[] { "tag1", "tag2" }
            };

            var profile = new PdfUtilityProfile
            {
                Id = "profile1",
                MarginLeft = "1cm",
                MarginRight = "1cm",
                HeaderSpacing = "0.5",
                FooterSpacing = "0.5",
                Width = "210mm",
                Height = "297mm",
                Zoom = "1",
                PageNumberPosition = 1,
                PageNumberText = "{page}/{total}",
                PageNumberOffset = new[] { 10, 10 },
                RemoveHeaderFromPage = new[] { 1 },
                RemoveFooterFromPage = new[] { 1 },
                PageNumberFont = "Arial",
                AsyncStream = true,
                ExecuteUsingWrapper = true,
                RemoveHeaderFooterFromCoverPage = true,
                Orientation = "Landscape",
                WkCustomArgs = "--arg"
            };

            dump.Tags.Should().HaveCount(2);
            profile.Orientation.Should().Be("Landscape");
            profile.PageNumberOffset.Should().ContainInOrder(10, 10);
        }
    }
}
