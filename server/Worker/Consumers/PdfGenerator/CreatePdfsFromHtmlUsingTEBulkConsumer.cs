using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.TemplateEngine.service;

namespace Worker.Consumers.PdfGenerator
{
    [ExcludeFromCodeCoverage]
    public class CreatePdfsFromHtmlUsingTEBulkConsumer 
        : PdfFromHtmlUsingTEConsumerBase<CreatePdfsFromHtmlUsingTEBulkEvent, CreateFromHtmlUsingTEForBulkCommand>,
          IConsumer<CreatePdfsFromHtmlUsingTEBulkEvent>
    {
        public CreatePdfsFromHtmlUsingTEBulkConsumer(
            ILogger<CreatePdfsFromHtmlUsingTEBulkConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorRepository repository,
            IPdfGeneratorNotificationService notificationService,
            ITemplateEngineService templateEngineService)
            : base(logger, storageHelper, engineProvider, repository, notificationService, templateEngineService)
        {
        }

        public async Task Consume(CreatePdfsFromHtmlUsingTEBulkEvent @event)
        {
            await ConsumeInternal(
                @event.MessageCoRelationId,
                @event.ProjectKey,
                @event.Engine,
                @event.CreateFromHtmlCommands);
        }

        protected override string GetConsumerName() => "CreatePdfsFromHtmlUsingTEBulkConsumer";
        protected override string GetFileType() => "BulkTemplateEnginePDF";
        protected override string GetStorageContainer() => "Blocks-PDF-TE-Bulk-Generated-Files";

        protected override async Task SendNotificationAsync(
            string messageCoRelationId,
            string? projectKey,
            int successCount,
            int failureCount,
            bool isError = false)
        {
            await _notificationService.NotifyCreatePdfsFromHtmlUsingTEBulkEvent(
                !isError && failureCount == 0,
                messageCoRelationId,
                projectKey,
                successCount,
                failureCount);
        }

        // Property accessors for CreateFromHtmlUsingTEForBulkCommand
        protected override string GetOutputFileId(CreateFromHtmlUsingTEForBulkCommand command) => command.OutputPdfFileId;
        protected override string GetOutputFileName(CreateFromHtmlUsingTEForBulkCommand command) => command.OutputPdfFileName;
        protected override string GetTemplateFileId(CreateFromHtmlUsingTEForBulkCommand command) => command.TemplateFileId;
        protected override List<PdfMetaData>? GetMetaDataList(CreateFromHtmlUsingTEForBulkCommand command) => command.MetaDataList;
        protected override string GetFileNameExtension(CreateFromHtmlUsingTEForBulkCommand command) => command.FileNameExtension; // From command for bulk
        protected override double GetHeaderHeight(CreateFromHtmlUsingTEForBulkCommand command) => command.HeaderHeight;
        protected override double GetFooterHeight(CreateFromHtmlUsingTEForBulkCommand command) => command.FooterHeight;
        protected override bool GetIsPageNumberEnabled(CreateFromHtmlUsingTEForBulkCommand command) => command.IsPageNumberEnabled;
        protected override bool GetIsTotalPageCountEnabled(CreateFromHtmlUsingTEForBulkCommand command) => command.IsTotalPageCountEnabled;
        protected override bool GetUseFormatting(CreateFromHtmlUsingTEForBulkCommand command) => command.UseFormatting;
        protected override bool GetOpenInBrowser(CreateFromHtmlUsingTEForBulkCommand command) => command.OpenInBrowser;
        protected override string? GetProfile(CreateFromHtmlUsingTEForBulkCommand command) => command.Profile;
        protected override string? GetPageNumberText(CreateFromHtmlUsingTEForBulkCommand command) => command.PageNumberText;
        protected override bool GetHasHeader(CreateFromHtmlUsingTEForBulkCommand command) => command.HasHeader;
        protected override string? GetHeaderHtmlFileId(CreateFromHtmlUsingTEForBulkCommand command) => command.HeaderHtmlFileId;
        protected override bool GetHasFooter(CreateFromHtmlUsingTEForBulkCommand command) => command.HasFooter;
        protected override string? GetFooterHtmlFileId(CreateFromHtmlUsingTEForBulkCommand command) => command.FooterHtmlFileId;
        protected override bool GetHasFirstPageHeader(CreateFromHtmlUsingTEForBulkCommand command) => command.HasFirstPageHeader;
        protected override string? GetFirstPageHeaderFileId(CreateFromHtmlUsingTEForBulkCommand command) => command.FirstPageHeaderFileId;
        protected override bool GetHasFirstPageFooter(CreateFromHtmlUsingTEForBulkCommand command) => command.HasFirstPageFooter;
        protected override string? GetFirstPageFooterFileId(CreateFromHtmlUsingTEForBulkCommand command) => command.FirstPageFooterFileId;
    }
}
