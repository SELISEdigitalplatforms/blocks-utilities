namespace Iam.DomainService.Dtos
{
    public class GetRolesFilter
    {
        public string Search { get; set; }
        public List<string> Slugs { get; set; } = [];
    }
}
