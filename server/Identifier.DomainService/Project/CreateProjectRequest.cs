using Blocks.Genesis;

namespace DomainService.Projects
{
    public class CreateProjectRequest
    {
        public string Name { get; set; }
        public bool IsAcceptBlocksTerms { get; set; }
        public bool IsUseBlocksExclusively { get; set; }
        public bool IsProduction { get; set; }
        public string? TenantGroupId { get; set; }
        public List<Resource>? Resources { get; set; }
        public List<ApplicationContext> applicationContexts { get; set; }
    }

    public class ApplicationContext
    {
        public string Environment { get; set; } // e.g., DEV, STG, PROD
        public string Domain { get; set; } // e.g.,dev.example.com
        public string CookieDomain { get; set; }
    }

    public class CreateProjectResponse : BaseResponse
    {
        public string? TenantGroupId { get; set; }
    }

    public class Resource
    {
        public string ResourceId { get; set; }
        public string Name { get; set; }
        public string Link { get; set; }
    }
}
