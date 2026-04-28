using DomainService.Projects;
using FluentValidation;

namespace DomainService.Migration
{
    public class MigrationRequestValidator : AbstractValidator<MigrationRequest>
    {
        private readonly IProjectRepository _projectRepository;

        public MigrationRequestValidator(IProjectRepository projectRepository)
        {
            _projectRepository = projectRepository;

            // Validation for TenantGroupId
            RuleFor(x => x.TenantGroupId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .WithMessage("TenantGroupId is required.");

            // Validation for ProjectKey
            RuleFor(x => x.ProjectKey)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .WithMessage("ProjectKey is required.");

            // Validation for TargetedProjectKey
            RuleFor(x => x.TargetedProjectKey)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .WithMessage("TargetedProjectKey is required.");

            // Validation to ensure both ProjectKey and TargetedProjectKey exist in the projects for the TenantGroupId
            RuleFor(x => x)
                .Cascade(CascadeMode.Stop)
                .MustAsync(ValidateProjectKeysExistAsync)
                .WithMessage("You don't have access to one or both of the specified environments.");
        }

        private async Task<bool> ValidateProjectKeysExistAsync(MigrationRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var getProjectsRequest = new GetProjectsRequest
                {
                    TenantGroupId = request.TenantGroupId,
                    PageSize = 10,
                    Page = 0
                };

                var groupedProjects = await _projectRepository.GetAllByLastModifiedDateAsync(getProjectsRequest);
                
                if (groupedProjects == null || !groupedProjects.Any())
                {
                    return false;
                }

                // Get all projects for the specified TenantGroupId
                var projects = groupedProjects
                    .Where(g => g.TenantGroupId == request.TenantGroupId)
                    .SelectMany(g => g.Projects)
                    .ToList();

                if (!projects.Any())
                {
                    return false;
                }

                // Extract all TenantIds from the projects
                var tenantIds = projects.Select(p => p.TenantId).ToHashSet();

                // Check if both ProjectKey and TargetedProjectKey exist as TenantId in the projects
                return tenantIds.Contains(request.ProjectKey) && tenantIds.Contains(request.TargetedProjectKey);
            }
            catch
            {
                return false;
            }
        }
    }
}
