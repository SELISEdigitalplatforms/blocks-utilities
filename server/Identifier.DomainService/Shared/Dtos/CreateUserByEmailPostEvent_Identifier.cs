namespace DomainService.Dtos
{
    public class CreateUserByEmailPostEvent_Identifier
    {
        public string Key { get; set; }
        public string UserId { get; set; }
        public string EventType { get; set; }
        public string ProjectKey { get; set; }
    }
}
