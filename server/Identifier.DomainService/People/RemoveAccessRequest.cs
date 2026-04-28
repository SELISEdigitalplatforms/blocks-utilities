using Blocks.Genesis;

namespace DomainService.People
{
    public class RemoveAccessRequest
    {
        public string Email { get; set; }
        public List<string> ProjectKeys { get; set; } = new List<string>();
        public required string GroupId { get; set; }
    }

    public class RemoveAccessResponse : BaseResponse
    {
        public string? ItemId { get; set; }
    }
}
