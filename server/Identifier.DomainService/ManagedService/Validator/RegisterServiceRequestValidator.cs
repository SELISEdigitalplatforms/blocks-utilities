using FluentValidation;
using System.Text.Json;

namespace DomainService.ManagedService.Validator
{
    public class RegisterServiceRequestValidator : AbstractValidator<RegisterServiceRequest>
    {
        private readonly List<string> allowedServiceType = new List<string>() { "backend", "frontend" };
        public RegisterServiceRequestValidator()
        {
            RuleFor(x => x.ServiceName)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("ServiceName is required.")
                .MaximumLength(100).WithMessage("ServiceName cannot exceed 100 characters.");
            RuleFor(x => x.ServiceType)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("ServiceType is required.")
                .Must(BeAllowedServiceType).WithMessage($"ServiceType is not allowed. Allowed values are {JsonSerializer.Serialize(allowedServiceType)}");
        }

        private bool BeAllowedServiceType(string serviceType)
        {
            return allowedServiceType.Contains(serviceType.ToLower());
        }
    }
}
