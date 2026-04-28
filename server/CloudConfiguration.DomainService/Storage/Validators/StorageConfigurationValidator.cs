using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.RequestModel;
using FluentValidation;
using System.Text.RegularExpressions;

namespace CloudConfiguration.DomainService.Storage.Validators
{
    public class StorageConfigurationValidator : AbstractValidator<SaveStorageConfigurationRequest>
    {
        private readonly IConfigurationRepository _configurationRepository;

        public StorageConfigurationValidator(IConfigurationRepository configurationRepository)
        {
            _configurationRepository = configurationRepository;
            //Validate Name
            RuleFor(config => config.Name)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .WithMessage("Name must not be empty.");

            When(config => config.UpdateRequest, () =>
            {
                RuleFor(config => config.ItemId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .WithMessage("ItemId should not be empty");

                RuleFor(config => config.Name)
                .MustAsync(async (name, cancellation) => await IsUniqueNameAsync(name, default))
                .WithMessage("Name must be unique")
                .WhenAsync(async (config, cancellation) => await IsNameUpdated(config));
            });

            When(config => !config.UpdateRequest, () =>
            {
                RuleFor(config => config.ConnectionString)
                .MustAsync(IsUniqueNameAsync)
               .WithMessage("Name should be unique");
            });

            // Validate StorageStrategy
            RuleFor(config => config.StorageStrategy)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .WithMessage("StorageStrategy must not be empty.")
                .Must(strategy => strategy == "Azure" || strategy == "AWS" || strategy == "SftpStorage" || strategy == "S3Compatible")
                .WithMessage("StorageStrategy must be one of the following values: 'Azure', 'AWS', 'SftpStorage', 'S3Compatible'.");

            When(config => config.StorageStrategy == "SftpStorage", () =>
            {
                RuleFor(config => config.Host)
                    .NotEmpty().WithMessage("Host must not be empty.")
                    .Matches(@"^([a-zA-Z0-9\-\.]+)$").WithMessage("Host must be a valid hostname or IP.");

                //Empty Port will result in 22 (sftp) by default inside service.

                RuleFor(config => config.UserName)
                    .NotEmpty().WithMessage("UserName must not be empty.")
                    .MinimumLength(3).WithMessage("UserName must be at least 3 characters.");

                RuleFor(config => config.Password)
                    .NotEmpty()
                    .NotNull()
                    .WithMessage("Password must not be empty.");

                RuleFor(config => config.RemoteBasePath)
                    .NotEmpty().WithMessage("RemoteBasePath must not be empty.")
                    .Must(path => path.StartsWith('/')).WithMessage("RemoteBasePath must start with a '/'")
                    .Must(path => !path.Contains("..")).WithMessage("RemoteBasePath must not contain relative segments ('..')");
            });

            When(config => config.StorageStrategy == "Azure", () =>
            {
                // Validate ConnectionString
                RuleFor(config => config.ConnectionString)
                    .NotEmpty()
                    .WithMessage("ConnectionString must not be empty.")
                    .Must(IsAzureStorageConnectionStringValid)
                    .WithMessage("ConnectionString format is invalid");
            });

            When(config => config.StorageStrategy == "AWS", () =>
            {
                RuleFor(config => config.SecretKey)
                .NotEmpty()
                .NotNull()
                .WithMessage("SecretKey must not be empty.");

                RuleFor(config => config.AccessKey)
                .NotEmpty()
                .NotNull()
                .WithMessage("AccessKey must not be empty.");

                RuleFor(config => config.CloudStorageRegionEndPoint)
                .NotEmpty()
                .NotNull()
                .WithMessage("CloudStorageRegionEndPoint must not be empty.");
            });
            When(config => config.StorageStrategy == "S3Compatible", () =>
            {
                RuleFor(config => config.SecretKey)
                .NotEmpty()
                .NotNull()
                .WithMessage("SecretKey must not be empty.");

                RuleFor(config => config.AccessKey)
                .NotEmpty()
                .NotNull()
                .WithMessage("AccessKey must not be empty.");

                RuleFor(config => config.Host)
                .NotEmpty()
                .NotNull()
                .WithMessage("Host must not be empty.");
            });
        }

        private static bool IsAzureStorageConnectionStringValid(string connectionString)
        {
            var pattern = @"^DefaultEndpointsProtocol=(http|https);AccountName=[\w-]+;AccountKey=[\w+=/]+;EndpointSuffix=[\w.]+$";
            return Regex.IsMatch(connectionString, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        }

        private async Task<bool> IsUniqueNameAsync(string name, CancellationToken cancellationToken)
        {
            var configuration = await _configurationRepository.GetStorageConfigurationByNameAsync(name);
            return configuration == null;
        }

        private async Task<bool> IsNameUpdated(SaveStorageConfigurationRequest request)
        {
            var config = await _configurationRepository.GetStorageConfigurationByIdAsync(request.ItemId);
            return config.Name != request.Name;
        }
    }
}
