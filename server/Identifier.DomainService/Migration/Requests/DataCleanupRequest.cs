using Blocks.Genesis;

namespace DomainService.Migration
{
    public class DataCleanupRequest:IProjectKey
    {
        public string ProjectKey { get; set; }
    }
    public class PublishScheduleCommand
    {
        public string Payload { get; set; }
    }
}