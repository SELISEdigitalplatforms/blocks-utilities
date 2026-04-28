namespace CloudConfiguration.DomainService.Storage.Entities;
public class CreateDefaultFolderEvent
{
    public required string ItemId { get; set; }
    public required string ConfigurationName { get; set; }
    public required string StorageStrategy { get; set; }
    public required string ProjectKey { get; set; }

}