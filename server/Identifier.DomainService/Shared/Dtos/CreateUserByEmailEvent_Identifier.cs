namespace DomainService.Dtos
{
    public class CreateUserByEmailEvent_Identifier
    {
        public string Email { get; set; }
        public string EventQueue { get; set; }
        public string EventType { get; set; }
        public string ProjectKey { get; set; }
    }
}
