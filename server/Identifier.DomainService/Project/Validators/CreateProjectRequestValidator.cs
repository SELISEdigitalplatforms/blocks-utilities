using DomainService.Shared;
using FluentValidation;

namespace DomainService.Projects
{
    public class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
    {
       private readonly IProjectRepository _projectRepository;

        public CreateProjectRequestValidator(IProjectRepository projectRepository)
        {
           _projectRepository = projectRepository;

            // Validation for ProjectName
            RuleFor(x => x.Name)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .Length(3, 100).WithMessage("Project Name must be between 3 and 100 characters.");

           // Validation for IsAcceptBlocksTerms
           RuleFor(x => x.IsAcceptBlocksTerms)
               .Equal(true)
               .WithMessage("You must accept the Blocks Terms to proceed.");

            //Validation for IsUseBlocksExclusively
            RuleFor(x => x.IsUseBlocksExclusively)
               .Equal(true)
               .WithMessage("You must accept the Blocks Terms to proceed.");

            RuleFor(x => x.applicationContexts)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .Must(ContainAtLestOneApplicationContext).WithMessage("Should contain atleast one application")
                .Must(SupportedEnviromet).WithMessage("One or more projects have unsupported environment.")
                .Must(ValidDomain).WithMessage("Application domain name must be a valid URL format.")
                .Must(NotContainAnyDuplicateEnv).When(x => string.IsNullOrWhiteSpace(x.TenantGroupId)).WithMessage("Projects should not contain duplicate environments.");
            //.MustAsync(IsUniqueDomain).WithMessage("Application domain name must be unique")

            RuleFor(x => x)
               .Cascade(CascadeMode.Stop)
               .Must(x=> ContainAtLestOneApplicationContext(x.applicationContexts)).WithMessage("Should contain atleast one application")
               .Must(x=> SupportedEnviromet(x.applicationContexts)).WithMessage("One or more projects have unsupported environment")
               .Must(x=> NotContainAnyDuplicateEnv(x.applicationContexts)).WithMessage("Projects should not contain duplicate environments.")
               .MustAsync(NotContainExistingEnviroment).WithMessage("One or more projects already have an existing environment in the system.")
               .When(x => !string.IsNullOrWhiteSpace(x.TenantGroupId));
               


            // Validation to restrict users from creating more than 5 projects
            //RuleFor(x => x)
            //    .Cascade(CascadeMode.Stop)
            //    .MustAsync(HasNotExceededProjectLimit)
            //    .WithMessage("You are not allowed to create more than 5 projects.");
        }

        private static bool IsValidCookieDomain(List<ApplicationContext> applicationContexts)
        {
            foreach (var applicationContext in applicationContexts)
            {
                if (!(IdentifierHelper.ExtractMainDomain(applicationContext.Domain) == applicationContext.CookieDomain))
                    return false;
            }

            return true;
        }

        private static bool ValidDomain(List<ApplicationContext> applicationContexts)
        {
            foreach (var applicationContext in applicationContexts)
            {
                if (!IdentifierHelper.BeAValidUrl(applicationContext.Domain))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ContainAtLestOneApplicationContext(List<ApplicationContext> applicationContexts)
        {
            return applicationContexts != null && applicationContexts.Count > 0;
        }

        private static bool SupportedEnviromet(List<ApplicationContext> applicationContexts)
        {
            foreach(var applicationContext in applicationContexts)
            {
                if(! IdentifierHelper.IsSupportedEnvironment(applicationContext.Environment))
                    return false;
            }

            return true;
        }

        private static bool NotContainAnyDuplicateEnv(List<ApplicationContext> applicationContexts)
        {
            var envSet = new HashSet<string>();
            foreach (var applicationContext in applicationContexts)
            {
                if (!envSet.Add(applicationContext.Environment))
                {
                   return false;
                }
            }

            return true;
        }

        private async Task<bool> NotContainExistingEnviroment(CreateProjectRequest request, CancellationToken cancellationToken)
        {
             if(await _projectRepository.IsExistingEnviroment([.. request.applicationContexts.Select(a=>a.Environment)], request.TenantGroupId))
                    return false;
            
            return true;
        }



        private async Task<bool> HasNotExceededProjectLimit(object _, CancellationToken cancellationToken)
        {
            var projectCount = await _projectRepository.GetProjectCountAsync();
            return projectCount < 5;
        }
    }
}
