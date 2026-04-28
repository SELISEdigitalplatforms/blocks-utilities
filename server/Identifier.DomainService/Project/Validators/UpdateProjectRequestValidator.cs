using DomainService.Shared;
using FluentValidation;

namespace DomainService.Projects
{
    public class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
    {
        private readonly IProjectRepository _repository;

        public UpdateProjectRequestValidator(IProjectRepository repository)
        {
            _repository = repository;

            // Validation for ProjectId
            RuleFor(x => x.ProjectKey)
               .Cascade(CascadeMode.Stop)
               .NotEmpty()
               .NotNull();

            RuleFor(x => x.ApplicationDomain)
                .Cascade(CascadeMode.Stop)
                .Must(IdentifierHelper.BeAValidUrl).WithMessage("ApplicationDomain is not in a valid format.")
                .MustAsync(IsUniqueDomain).WithMessage("ApplicationDomain must be unique")
                .WhenAsync((x, ct) => IsDomainUpdated(x.ApplicationDomain, x.ProjectKey, ct));

            RuleFor(x => x.CustomDomain)
                .Cascade(CascadeMode.Stop)
                .Must(IdentifierHelper.BeAValidUrl).WithMessage("CustomDomain is not in a valid format.")
                .When(x => !string.IsNullOrWhiteSpace(x.CustomDomain));
        }

        private async Task<bool> IsDomainUpdated(string applicationDomain, string projectId, CancellationToken _)
        {
            var project = await _repository.GetByTenantIdAsync(projectId);
            return project?.ApplicationDomain != applicationDomain;
        }

        private async Task<bool> IsUniqueDomain(string applicationDomain, CancellationToken _)
        {
            var application = await _repository.GetByDomainAsync(applicationDomain);

            if (application != null)
            {
                return false; // If any domain is not unique, return false
            }

            return true;
        }
    }
}
