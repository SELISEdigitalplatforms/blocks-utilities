namespace Mail.DomainService.Mails.Services.Attachments
{
    public sealed class MailAttachment : IAsyncDisposable
    {
        private readonly string? _temporaryFilePath;

        public MailAttachment(string fileId, string fileName, string contentType, Stream content, long sizeInBytes, string? temporaryFilePath = null)
        {
            FileId = fileId;
            FileName = fileName;
            ContentType = contentType;
            Content = content;
            SizeInBytes = sizeInBytes;
            _temporaryFilePath = temporaryFilePath;
        }

        public string FileId { get; }
        public string FileName { get; }
        public string ContentType { get; }
        public Stream Content { get; }
        public long SizeInBytes { get; }

        public async ValueTask DisposeAsync()
        {
            await Content.DisposeAsync();

            if (string.IsNullOrWhiteSpace(_temporaryFilePath) || !File.Exists(_temporaryFilePath))
            {
                return;
            }

            File.Delete(_temporaryFilePath);
        }
    }
}
