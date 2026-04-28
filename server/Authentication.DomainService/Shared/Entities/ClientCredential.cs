using Blocks.Genesis;

namespace DomainService.Entities
{
    public class ClientCredential : BaseEntity
    {
        public string Name { get; set; }
        public string ClientSecret { get; set; }
        public List<string> Roles { get; set; }
        public bool IsActive { get; set; }
        public List<string> Audiences { get; set; }
    }
}
