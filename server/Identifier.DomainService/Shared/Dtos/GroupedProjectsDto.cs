using DomainService.Entities;

namespace DomainService.Dtos
{
    public class GroupedProjectsDto
    {
        public string TenantGroupId { get; set; } = string.Empty;
        public List<Project> Projects { get; set; } = [];
        public bool IsShared { get; set; }
        public List<Project> NonSharedProject { get; set; } = [];
    }
}
