using Blocks.Genesis;
using Blocks.MailDriver;
using DomainService.Dtos;
using DomainService.Migration.Entities;
using DomainService.Migration.Services;
using DomainService.Shared;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;
using SendMail = Blocks.MailDriver.SendMail;

namespace DomainService.Migration
{
    public class MigrationService : IMigrationService
    {
        private readonly ICacheClient _cacheClient;
        private readonly IMailDriverService _mailDriverService;
        private readonly IMessageClient _messageClient;
        private readonly IValidator<MigrationRequest> _migrationRequestValidator;
        private readonly IMigrationRepository _migrationRepository;
        private readonly IConfiguration _configuration;
        private readonly ITenants _tenants;
        private readonly ICryptoService _cryptoService;
        private readonly IHttpService _httpService;
        private readonly ILogger<MigrationService> _logger;

        public MigrationService(
            ICacheClient cacheClient,
            IMailDriverService mailDriverService,
            IMessageClient messageClient,
            IValidator<MigrationRequest> migrationRequestValidator,
            IMigrationRepository migrationRepository,
            IConfiguration configuration,
            ITenants tenants,
            ICryptoService cryptoService,
            IHttpService httpService,
            ILogger<MigrationService> logger)
        {
            _cacheClient = cacheClient;
            _mailDriverService = mailDriverService;
            _messageClient = messageClient;
            _migrationRequestValidator = migrationRequestValidator;
            _configuration = configuration;
            _migrationRepository = migrationRepository;
            _tenants = tenants;
            _cryptoService = cryptoService;
            _httpService = httpService;
            _logger = logger;
        }
        public async Task<MigrationOtpGenerationResponse> Migrate(MigrationRequest request)
        {
            var validationResult = await _migrationRequestValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return new MigrationOtpGenerationResponse { IsSuccess = false, Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            var bc = BlocksContext.GetContext();
            if (bc == null || string.IsNullOrEmpty(bc.UserName))
            {
                return new MigrationOtpGenerationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "message", "invalid_user_context" } } };
            }
            var code = GenerateSecureRandomNumber();
            var verificationId = Guid.NewGuid().ToString();

            var serializedData = JsonSerializer.Serialize(new { Code = code, Request = request });

            await _cacheClient.AddStringValueAsync(verificationId, serializedData, 600);
            var result = await SendMfaCodeAsync(bc.UserName, code, "en-US");
            return new MigrationOtpGenerationResponse { VerificationId = verificationId, IsSuccess = result };
        }
        public static string GenerateSecureRandomNumber()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[2];
            rng.GetBytes(bytes);
            int number = BitConverter.ToUInt16(bytes, 0) % 88889 + 11111;
            return number.ToString();
        }

        private async Task<bool> SendMfaCodeAsync(string email, string code, string language)
        {
            // var configuration = await _configurationService.GetAsync();

            var sendMailCommand = new SendMail
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = new Dictionary<string, string>
                                {
                                   { "TwoFactorCode", code }
                                },

                Purpose = "MfaViaEmail",
                Language = language ?? "en-US",
                To = [email]
            };

            var response = await _mailDriverService.SendAsync(sendMailCommand);

            return response.IsSuccess;
        }

        public async Task<MigrationOtpVerificationResponse> VerifyAsync(MigrationVerifyOtpRequest request)
        {
            var isKeyExist = await _cacheClient.KeyExistsAsync(request.VerificationId);

            if (!isKeyExist)
            {
                return new MigrationOtpVerificationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_id" } }, IsSuccess = false, IsValid = false };
            }

            var keyValue = await _cacheClient.GetStringValueAsync(request.VerificationId);
            if (string.IsNullOrEmpty(keyValue))
            {
                return new MigrationOtpVerificationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_id" } }, IsSuccess = false, IsValid = false };
            }

            var data = JsonSerializer.Deserialize<MigrationOtpData>(keyValue);
            if (data == null)
            {
                return new MigrationOtpVerificationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_id" } }, IsSuccess = false, IsValid = false };
            }

            if (data.Code == request.VerificationCode)
            {
                await _cacheClient.RemoveKeyAsync(request.VerificationId);
                await HandleServiceMigrations(data.Request);
                await NotifyMigrationStarted(data.Request.ProjectKey, data.Request.TargetedProjectKey);
                return new MigrationOtpVerificationResponse { IsSuccess = true, IsValid = true };
            }

            return new MigrationOtpVerificationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_code" } }, IsSuccess = false, IsValid = false };
        }

        private async Task HandleServiceMigrations(MigrationRequest request)
        {
            // Create migration tracker
            var migrationTracker = new MigrationTracker
            {
                ItemId = Guid.NewGuid().ToString(),
                ProjectKey = request.ProjectKey,
                TargetedProjectKey = request.TargetedProjectKey,
                TenantGroupId = request.TenantGroupId,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext()?.UserId ?? "system",
                LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "system"
            };

            // Set up service properties based on requested services
            foreach (var service in request.Services)
            {
                var serviceStatus = new ServiceMigrationStatus
                {
                    ShouldOverWriteExistingData = service.ShouldOverWriteExistingData,
                    QueueName = GetQueueNameForService(service.ServiceName)
                };

                SetServiceProperty(migrationTracker, service.ServiceName, serviceStatus);
            }

            var trackerId = await _migrationRepository.CreateMigrationTrackerAsync(migrationTracker);

            // Process each service
            foreach (var service in request.Services)
            {
                var queueName = GetQueueNameForService(service.ServiceName);
                if (!string.IsNullOrEmpty(queueName))
                {
                    await SendMigrationEvent(request, service.ServiceName, queueName, trackerId);
                }
                // If queueName is null/empty, service migration is not yet implemented
            }
        }

        private static void SetServiceProperty(MigrationTracker tracker, MigrationServiceNames serviceName, ServiceMigrationStatus status)
        {
            switch (serviceName)
            {
                case MigrationServiceNames.Authentication:
                    tracker.Authentication = status;
                    break;
                case MigrationServiceNames.IAM:
                    tracker.IAM = status;
                    break;
                case MigrationServiceNames.MFA:
                    tracker.MFA = status;
                    break;
                case MigrationServiceNames.CAPTCHA:
                    tracker.CAPTCHA = status;
                    break;
                case MigrationServiceNames.Email:
                    tracker.Email = status;
                    break;
                case MigrationServiceNames.DataGateway:
                    tracker.DataGateway = status;
                    break;
                case MigrationServiceNames.Notifications:
                    tracker.Notifications = status;
                    break;
                case MigrationServiceNames.Storage:
                    tracker.Storage = status;
                    break;
                case MigrationServiceNames.Language:
                    tracker.LanguageService = status;
                    break;
            }
        }

        private static string GetQueueNameForService(MigrationServiceNames serviceName)
        {
            return serviceName switch
            {
                MigrationServiceNames.Language => IdentifierConstants.LanguageDataMigrationQueue,
                MigrationServiceNames.IAM => IdentifierConstants.IamQueue,
                MigrationServiceNames.Email => IdentifierConstants.GenericMigrationQueue,
                MigrationServiceNames.Authentication => "", // TODO: Define queue name for Authentication service
                MigrationServiceNames.MFA => "", // TODO: Define queue name for MFA service
                MigrationServiceNames.CAPTCHA => "", // TODO: Define queue name for CAPTCHA service
                MigrationServiceNames.DataGateway => IdentifierConstants.GenericMigrationQueue, 
                MigrationServiceNames.Notifications => "", // TODO: Define queue name for Notifications service
                MigrationServiceNames.Storage => "", // TODO: Define queue name for Storage service
                _ => "" // Unknown service
            };
        }

        // public async Task SendLanguageManagerMigrationEvent(MigrationRequest request)
        // {
        //     await SendMigrationEvent(request, MigrationServiceNames.Language, IdentifierConstants.LanguageDataMigrationQueue);
        // }

        private async Task SendMigrationEvent(MigrationRequest request, MigrationServiceNames serviceName, string queueName, string? trackerId = null)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<Dtos.EnvironmentDataMigrationEvent>
                {
                    ConsumerName = queueName,
                    Payload = new Dtos.EnvironmentDataMigrationEvent
                    {
                        ProjectKey = request.ProjectKey,
                        TargetedProjectKey = request.TargetedProjectKey,
                        ShouldOverWriteExistingData = request.Services.Any(s => s.ServiceName == serviceName && s.ShouldOverWriteExistingData),
                        TrackerId = trackerId // Add trackerId to the payload
                    }
                }
            );
        }

        public async Task<bool> NotifyDataMigrationProgress(bool response, string projectKey, string targetedProjectKey)
        {
            var requestData = new
            {
                ConnectionId = projectKey,
                Roles = new List<string> { },
                UserIds = new List<string> { BlocksContext.GetContext()?.UserId ?? "" },
                DenormalizedPayload = JsonSerializer.Serialize(new
                {
                    IsSuccess = response,
                    title = "Migration Completed",
                    projectKey = projectKey,
                    targetedProjectKey = targetedProjectKey
                }),
                SaveDenormalizedPayloadAsAnObject = false,
                ConfiguratoinName = "DataMigrationProgress",
                ContentAvailable = true,
                ResponseKey = "Environment Data Migration",
                ResponseValue = response ? "Migration completed" : "Migration failed"
            };

            var blocksKey = _configuration["RootTenantId"];
            var rootTenantId = _configuration["RootTenantId"];
            var salt = _tenants.GetTenantByID(rootTenantId)?.TenantSalt;
            var actulalSecret = _cryptoService.Hash(rootTenantId, salt);

            var url = _configuration["NotificationServiceUrl"];
            var headers = new Dictionary<string, string>
            {
                { "x-blocks-key", blocksKey },
                { "Secret", actulalSecret}
            };

            var (data, rawResponse) = await _httpService.Post<NotificationResponse>(requestData, url, "application/json", headers);
            return data == null ? false : data.isSuccess;
        }

        public async Task<bool> NotifyEnvironmentDataMigration(bool response, string projectKey, string targetedProjectKey)
        {
            var requestData = new
            {
                ConnectionId = projectKey,
                Roles = new List<string> { },
                UserIds = new List<string> { BlocksContext.GetContext()?.UserId ?? "" },
                DenormalizedPayload = JsonSerializer.Serialize(new
                {
                    IsSuccess = response,
                    title = "Migration Completed",
                    description = $"Migration {(response ? "completed successfully" : "failed")}",
                    projectKey = projectKey,
                    targetedProjectKey = targetedProjectKey
                }),
                SaveDenormalizedPayloadAsAnObject = false,
                ConfiguratoinName = "EnvironmentDataMigration",
                ContentAvailable = true,
                ResponseKey = "Environment Data Migration",
                ResponseValue = response ? "Migration completed" : "Migration failed"
            };

            var blocksKey = _configuration["RootTenantId"];
            var rootTenantId = _configuration["RootTenantId"];
            var salt = _tenants.GetTenantByID(rootTenantId)?.TenantSalt;
            var actulalSecret = _cryptoService.Hash(rootTenantId, salt);

            var url = _configuration["NotificationServiceUrl"];
            var headers = new Dictionary<string, string>
            {
                { "x-blocks-key", blocksKey },
                { "Secret", actulalSecret}
            };

            var (data, rawResponse) = await _httpService.Post<NotificationResponse>(requestData, url, "application/json", headers);
            return data == null ? false : data.isSuccess;
        }

        public async Task<bool> NotifyServiceDataMigrationProgress(bool response, string projectKey, string targetedProjectKey)
        {
            return await NotifyDataMigrationProgress(response, projectKey, targetedProjectKey);
        }

        public async Task<bool> NotifyDataMigrationEvent(bool response, string projectKey, string targetedProjectKey)
        {
            return await NotifyEnvironmentDataMigration(response, projectKey, targetedProjectKey);
        }

        public async Task<bool> NotifyMigrationStarted(string projectKey, string targetedProjectKey)
        {
            var requestData = new
            {
                ConnectionId = projectKey,
                Roles = new List<string> { },
                UserIds = new List<string> { BlocksContext.GetContext()?.UserId ?? "" },
                DenormalizedPayload = JsonSerializer.Serialize(new
                {
                    IsSuccess = true,
                    title = "Migration Started",
                    description = "Environment data migration has been initiated successfully",
                    projectKey = projectKey,
                    targetedProjectKey = targetedProjectKey
                }),
                SaveDenormalizedPayloadAsAnObject = false,
                ConfiguratoinName = "EnvironmentDataMigration",
                ContentAvailable = true,
                ResponseKey = "Environment Data Migration",
                ResponseValue = "Migration started"
            };

            var blocksKey = _configuration["RootTenantId"];
            var rootTenantId = _configuration["RootTenantId"];
            var salt = _tenants.GetTenantByID(rootTenantId)?.TenantSalt;
            var actulalSecret = _cryptoService.Hash(rootTenantId, salt);

            var url = _configuration["NotificationServiceUrl"];
            var headers = new Dictionary<string, string>
            {
                { "x-blocks-key", blocksKey },
                { "Secret", actulalSecret}
            };

            var (data, rawResponse) = await _httpService.Post<NotificationResponse>(requestData, url, "application/json", headers);
            return data == null ? false : data.isSuccess;
        }

        public bool AreAllServicesCompleted(MigrationTracker tracker)
        {
            var services = new List<ServiceMigrationStatus?>
            {
                tracker.Authentication,
                tracker.IAM,
                tracker.MFA,
                tracker.CAPTCHA,
                tracker.Email,
                tracker.DataGateway,
                tracker.Notifications,
                tracker.Storage,
                tracker.LanguageService
            };

            var activeServices = services.Where(s => s != null).ToList();
            return activeServices.Any() && activeServices.All(s => s!.IsCompleted);
        }

        public async Task MigrateEnvironmentDataAsync(string projectKey, string targetedProjectKey, bool shouldOverwriteExistingData, string trackerId)
        {
            var tracker = await _migrationRepository.GetMigrationTrackerAsync(trackerId);
            if (tracker == null)
            {
                _logger.LogError("Migration tracker not found for trackerId: {TrackerId}", trackerId);
                return;
            }

            // Get incomplete services that use GenericMigrationQueue
            var incompleteGenericServices = GetIncompleteServicesUsingGenericQueue(tracker);

            if (!incompleteGenericServices.Any())
            {
                _logger.LogInformation("No incomplete services using GenericMigrationQueue found for trackerId: {TrackerId}", trackerId);
                return;
            }

            _logger.LogInformation("Found {Count} incomplete services using GenericMigrationQueue for trackerId: {TrackerId}", 
                incompleteGenericServices.Count, trackerId);

            // Migrate collections for each incomplete service
            foreach (var serviceName in incompleteGenericServices)
            {
                try
                {
                    var requiredCollections = GetRequiredCollectionsForService(serviceName);
                    _logger.LogInformation("Migrating {CollectionCount} collections for service {ServiceName}", 
                        requiredCollections.Count, serviceName);

                    await MigrateServiceCollections(projectKey, targetedProjectKey, shouldOverwriteExistingData, 
                        serviceName, requiredCollections, trackerId);

                    _logger.LogInformation("Successfully migrated collections for service {ServiceName}", serviceName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error migrating collections for service {ServiceName} in trackerId: {TrackerId}", 
                        serviceName, trackerId);
                    
                    // Send completion event for failed migration
                    await SendMigrationCompletionEvent(trackerId, serviceName, false, ex.Message);
                }
            }
        }

        private List<MigrationServiceNames> GetIncompleteServicesUsingGenericQueue(MigrationTracker tracker)
        {
            var incompleteServices = new List<MigrationServiceNames>();

            // Map each service property to its corresponding enum and check if it uses GenericMigrationQueue
            var serviceMapping = new Dictionary<MigrationServiceNames, ServiceMigrationStatus?>
            {
                { MigrationServiceNames.Authentication, tracker.Authentication },
                { MigrationServiceNames.IAM, tracker.IAM },
                { MigrationServiceNames.MFA, tracker.MFA },
                { MigrationServiceNames.CAPTCHA, tracker.CAPTCHA },
                { MigrationServiceNames.Email, tracker.Email },
                { MigrationServiceNames.DataGateway, tracker.DataGateway },
                { MigrationServiceNames.Notifications, tracker.Notifications },
                { MigrationServiceNames.Storage, tracker.Storage },
                { MigrationServiceNames.Language, tracker.LanguageService }
            };

            foreach (var kvp in serviceMapping)
            {
                var serviceName = kvp.Key;
                var serviceStatus = kvp.Value;

                // Check if service is tracked and not completed
                if (serviceStatus != null && !serviceStatus.IsCompleted)
                {
                    // Check if this service uses GenericMigrationQueue
                    var queueName = GetQueueNameForService(serviceName);
                    if (queueName == IdentifierConstants.GenericMigrationQueue)
                    {
                        incompleteServices.Add(serviceName);
                    }
                }
            }

            return incompleteServices;
        }

        private static List<string> GetRequiredCollectionsForService(MigrationServiceNames serviceName)
        {
            return serviceName switch
            {
                MigrationServiceNames.DataGateway => new List<string>
                {
                    "DataServiceConfigurations",
                    "SchemaDefinitions",
					"DataAccessPolicys",
                    "DataValidations"
				},
                MigrationServiceNames.Email => new List<string>
                {
                    "EmailTemplates"
                },
                MigrationServiceNames.Authentication => new List<string>
                {
                    // "AuthProviders",
                    // "AuthConfigurations",
                    // "AuthTokens",
                    // "OAuthSettings"
                },
                MigrationServiceNames.MFA => new List<string>
                {
                    // "MfaSettings",
                    // "MfaDevices",
                    // "MfaBackupCodes"
                },
                MigrationServiceNames.CAPTCHA => new List<string>
                {
                    // "CaptchaSettings",
                    // "CaptchaProviders"
                },
                MigrationServiceNames.Notifications => new List<string>
                {
                    // "NotificationTemplates",
                    // "NotificationSettings",
                    // "NotificationChannels",
                    // "NotificationSubscriptions"
                },
                MigrationServiceNames.Storage => new List<string>
                {
                    // "StorageConfigurations",
                    // "StorageProviders",
                    // "FileMetadata",
                    // "StoragePolicies"
                },
                _ => new List<string>() // Return empty list for services not using GenericMigrationQueue
            };
        }

        private async Task MigrateServiceCollections(string projectKey, string targetedProjectKey, 
            bool shouldOverwriteExistingData, MigrationServiceNames serviceName, 
            List<string> requiredCollections, string trackerId)
        {
            try
            {
                _logger.LogInformation("Starting migration of {CollectionCount} collections for service {ServiceName} from {ProjectKey} to {TargetedProjectKey}", 
                    requiredCollections.Count, serviceName, projectKey, targetedProjectKey);

                foreach (var collectionName in requiredCollections)
                {
                    await MigrateCollection(projectKey, targetedProjectKey, collectionName, shouldOverwriteExistingData);
                    _logger.LogDebug("Successfully migrated collection {CollectionName} for service {ServiceName}", 
                        collectionName, serviceName);
                }

                // Send completion event for successful migration
                await SendMigrationCompletionEvent(trackerId, serviceName, true, null);
                _logger.LogInformation("Completed migration of all collections for service {ServiceName}", serviceName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during collection migration for service {ServiceName}", serviceName);
                // Send completion event for failed migration
                await SendMigrationCompletionEvent(trackerId, serviceName, false, ex.Message);
                throw;
            }
        }

        private async Task MigrateCollection(string sourceProjectKey, string targetProjectKey, 
            string collectionName, bool shouldOverwriteExistingData)
        {
            try
            {
                _logger.LogDebug("Migrating collection {CollectionName} from {SourceProject} to {TargetProject}", 
                    collectionName, sourceProjectKey, targetProjectKey);

                var (totalDocuments, migratedDocuments) = await _migrationRepository.MigrateCollectionAsync(sourceProjectKey, targetProjectKey, 
                    collectionName, shouldOverwriteExistingData);

                _logger.LogInformation("Collection {CollectionName} migration completed: migrated {MigratedCount} out of {TotalCount} documents", 
                    collectionName, migratedDocuments, totalDocuments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate collection {CollectionName} from {SourceProject} to {TargetProject}", 
                    collectionName, sourceProjectKey, targetProjectKey);
                throw;
            }
        }

        private async Task SendMigrationCompletionEvent(string trackerId, MigrationServiceNames serviceName, bool isSuccess, string? errorMessage)
        {
            var completionEvent = new Dtos.MigrationCompletionEvent
            {
                TrackerId = trackerId,
                ServiceName = serviceName.ToString(),
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
                CompletedAt = DateTime.UtcNow
            };

            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<Dtos.MigrationCompletionEvent>
                {
                    ConsumerName = IdentifierConstants.MigrationCompletionTopic,
                    Payload = completionEvent
                }
            );

            _logger.LogInformation("Migration completion event sent for service {ServiceName}, TrackerId: {TrackerId}, Success: {IsSuccess}",
                serviceName, trackerId, isSuccess);
        }
        
        public async Task<bool> DataCleanupAsync(DataCleanupRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ProjectKey))
            {
                _logger.LogWarning("DataCleanupAsync called with invalid request");
                return false;
            }

            try
            {
                var collectionsToClean = new List<string>
                {
                    "UilmFiles",
                    "BlocksLanguageModules",
                    "BlocksLanguageKeys",
                    "BlocksLanguages"
                };

                var cleanupTasks = collectionsToClean.Select(async collection =>
                {
                    await _migrationRepository.CleanupCollectionAsync(request.ProjectKey, collection);
                    _logger.LogInformation("Cleaned up collection {Collection} in project {ProjectKey}", collection, request.ProjectKey);
                });

                await Task.WhenAll(cleanupTasks);

                var migrationTasks = collectionsToClean.Select(async collection =>
                {
                    await _migrationRepository.MigrateDocumentsAsync(request.ProjectKey, collection);
                    _logger.LogInformation("Migrated documents for collection {Collection} in project {ProjectKey}", collection, request.ProjectKey);
                });

                await Task.WhenAll(migrationTasks);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data cleanup for project {ProjectKey}", request.ProjectKey);
                return false;
            }
        }
    }
}