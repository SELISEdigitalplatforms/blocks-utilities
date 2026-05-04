using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.TemplateEngine.service;

namespace Worker.Consumers.PdfGenerator
{
    [ExcludeFromCodeCoverage]
    public class CreatePdfsFromHtmlUsingTEConsumer 
        : PdfFromHtmlUsingTEConsumerBase<CreatePdfsFromHtmlUsingTEEvent, CreateFromHtmlUsingTECommand>,
          IConsumer<CreatePdfsFromHtmlUsingTEEvent>
    {
        public CreatePdfsFromHtmlUsingTEConsumer(
            ILogger<CreatePdfsFromHtmlUsingTEConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorRepository repository,
            IPdfGeneratorNotificationService notificationService,
            ITemplateEngineService templateEngineService)
            : base(logger, storageHelper, engineProvider, repository, notificationService, templateEngineService)
        {
        }

        public async Task Consume(CreatePdfsFromHtmlUsingTEEvent @event)
        {
            await ConsumeInternal(
                @event.MessageCoRelationId,
                @event.ProjectKey,
                @event.Engine,
                @event.CreateFromHtmlCommands);
        }

        protected override string GetConsumerName() => "CreatePdfsFromHtmlUsingTEConsumer";
        protected override string GetFileType() => "TemplateEnginePDF";
        protected override string GetStorageContainer() => "Blocks-PDF-TE-Generated-Files";

        protected override async Task SendNotificationAsync(
            string messageCoRelationId,
            string? projectKey,
            int successCount,
            int failureCount,
            bool isError = false)
        {
            await _notificationService.NotifyCreatePdfsFromHtmlUsingTEEvent(
                !isError && failureCount == 0,
                messageCoRelationId,
                projectKey);
        }

        // Property accessors for CreateFromHtmlUsingTECommand
        protected override string GetOutputFileId(CreateFromHtmlUsingTECommand command) => command.OutputPdfFileId;
        protected override string GetOutputFileName(CreateFromHtmlUsingTECommand command) => command.OutputPdfFileName;
        protected override string GetTemplateFileId(CreateFromHtmlUsingTECommand command) => command.TemplateFileId;
        protected override List<PdfMetaData>? GetMetaDataList(CreateFromHtmlUsingTECommand command) => command.MetaDataList;
        protected override string GetFileNameExtension(CreateFromHtmlUsingTECommand command) => ".html"; // Hardcoded for non-bulk
        protected override double GetHeaderHeight(CreateFromHtmlUsingTECommand command) => command.HeaderHeight;
        protected override double GetFooterHeight(CreateFromHtmlUsingTECommand command) => command.FooterHeight;
        protected override bool GetIsPageNumberEnabled(CreateFromHtmlUsingTECommand command) => command.IsPageNumberEnabled;
        protected override bool GetIsTotalPageCountEnabled(CreateFromHtmlUsingTECommand command) => command.IsTotalPageCountEnabled;
        protected override bool GetUseFormatting(CreateFromHtmlUsingTECommand command) => command.UseFormatting;
        protected override bool GetOpenInBrowser(CreateFromHtmlUsingTECommand command) => command.OpenInBrowser;
        protected override string? GetProfile(CreateFromHtmlUsingTECommand command) => command.Profile;
        protected override string? GetPageNumberText(CreateFromHtmlUsingTECommand command) => command.PageNumberText;
        protected override bool GetHasHeader(CreateFromHtmlUsingTECommand command) => command.HasHeader;
        protected override string? GetHeaderHtmlFileId(CreateFromHtmlUsingTECommand command) => command.HeaderHtmlFileId;
        protected override bool GetHasFooter(CreateFromHtmlUsingTECommand command) => command.HasFooter;
        protected override string? GetFooterHtmlFileId(CreateFromHtmlUsingTECommand command) => command.FooterHtmlFileId;
        protected override bool GetHasFirstPageHeader(CreateFromHtmlUsingTECommand command) => command.HasFirstPageHeader;
        protected override string? GetFirstPageHeaderFileId(CreateFromHtmlUsingTECommand command) => command.FirstPageHeaderFileId;
        protected override bool GetHasFirstPageFooter(CreateFromHtmlUsingTECommand command) => command.HasFirstPageFooter;
        protected override string? GetFirstPageFooterFileId(CreateFromHtmlUsingTECommand command) => command.FirstPageFooterFileId;
    }
}
