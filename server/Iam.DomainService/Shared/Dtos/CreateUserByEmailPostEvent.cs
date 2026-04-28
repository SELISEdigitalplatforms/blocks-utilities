namespace Iam.DomainService.Shared.Dtos
{
    public class CreateUserByEmailPostEvent
    {
        public string Key { get; set; }
        public string UserId { get; set; }
        public string EventType { get; set; }
        public string ProjectKey { get; set; }
    }
}
