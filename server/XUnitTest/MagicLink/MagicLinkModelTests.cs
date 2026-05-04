using FluentAssertions;
using Utility.DomainService.MagicLink;
using Utility.DomainService.MagicLink.Models;

namespace XUnitTest.MagicLink
{
    public class MagicLinkModelTests
    {
        #region MagicLink Entity Tests

        [Fact]
        public void MagicLink_ShouldHaveDefaultValues()
        {
            // Arrange & Act
            var link = new Utility.DomainService.MagicLink.Models.MagicLink();

            // Assert
            link.ItemId.Should().Be(string.Empty);
            link.Type.Should().Be(MagicLinkType.Action);
            link.Uri.Should().Be(string.Empty);
            link.ProjectKey.Should().Be(string.Empty);
            link.ShortUri.Should().Be(string.Empty);
            link.UsageLimit.Should().Be(0);
            link.UsageCount.Should().Be(0);
            link.ExpiryLifeSpan.Should().Be(0);
            link.IsExpired.Should().BeFalse();
            link.UserCanLogin.Should().BeFalse();
            link.Persistent.Should().BeFalse();
        }

        [Fact]
        public void MagicLink_ShouldStoreAllProperties()
        {
            // Arrange
            var createdAt = DateTime.UtcNow;
            var expiryDate = createdAt.AddHours(1);

            // Act
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "abc123",
                Type = MagicLinkType.Redirect,
                Name = "Test Link",
                Uri = "https://example.com",
                UriOnForbidden = "https://example.com/forbidden",
                RequestMethod = "POST",
                RequestPayload = "{\"key\": \"value\"}",
                RequestHeaders = "{\"Authorization\": \"Bearer token\"}",
                RequestEncodedQueryString = "param=value",
                RedirectUrl = "https://example.com/redirect",
                UsageLimit = 10,
                UsageCount = 5,
                ExpiryLifeSpan = 3600000,
                ExpiryDate = expiryDate,
                IsExpired = true,
                ExpiredReason = "UsageLimitExceeded",
                ProjectKey = "test-project",
                ShortUri = "https://short.test/abc123",
                RequestByUserId = "user-123",
                UserCanLogin = true,
                ClientCredential = "client-cred",
                LinkBasedActionConfigId = "config-123",
                Language = "en-US",
                Origin = "https://origin.com",
                Persistent = true,
                CreatedAt = createdAt,
                CreatedBy = "admin",
                UpdatedAt = createdAt.AddMinutes(30)
            };

            // Assert
            link.ItemId.Should().Be("abc123");
            link.Type.Should().Be(MagicLinkType.Redirect);
            link.Name.Should().Be("Test Link");
            link.Uri.Should().Be("https://example.com");
            link.UriOnForbidden.Should().Be("https://example.com/forbidden");
            link.RequestMethod.Should().Be("POST");
            link.RequestPayload.Should().Be("{\"key\": \"value\"}");
            link.RequestHeaders.Should().Be("{\"Authorization\": \"Bearer token\"}");
            link.RequestEncodedQueryString.Should().Be("param=value");
            link.RedirectUrl.Should().Be("https://example.com/redirect");
            link.UsageLimit.Should().Be(10);
            link.UsageCount.Should().Be(5);
            link.ExpiryLifeSpan.Should().Be(3600000);
            link.ExpiryDate.Should().Be(expiryDate);
            link.IsExpired.Should().BeTrue();
            link.ExpiredReason.Should().Be("UsageLimitExceeded");
            link.ProjectKey.Should().Be("test-project");
            link.ShortUri.Should().Be("https://short.test/abc123");
            link.RequestByUserId.Should().Be("user-123");
            link.UserCanLogin.Should().BeTrue();
            link.ClientCredential.Should().Be("client-cred");
            link.LinkBasedActionConfigId.Should().Be("config-123");
            link.Language.Should().Be("en-US");
            link.Origin.Should().Be("https://origin.com");
            link.Persistent.Should().BeTrue();
            link.CreatedAt.Should().Be(createdAt);
            link.CreatedBy.Should().Be("admin");
            link.UpdatedAt.Should().BeCloseTo(createdAt.AddMinutes(30), TimeSpan.FromSeconds(1));
        }

        #endregion

        #region MagicLinkType Tests

        [Theory]
        [InlineData(MagicLinkType.Action, 0)]
        [InlineData(MagicLinkType.Redirect, 1)]
        public void MagicLinkType_ShouldHaveCorrectIntValues(MagicLinkType type, int expected)
        {
            // Assert
            ((int)type).Should().Be(expected);
        }

        #endregion

        #region MagicLinkExpiredReason Tests

        [Theory]
        [InlineData(MagicLinkExpiredReason.None, 0)]
        [InlineData(MagicLinkExpiredReason.UsageLimitExceeded, 1)]
        [InlineData(MagicLinkExpiredReason.ManuallyDisabled, 2)]
        [InlineData(MagicLinkExpiredReason.TimeExpired, 3)]
        [InlineData(MagicLinkExpiredReason.LifespanExpired, 4)]
        public void MagicLinkExpiredReason_ShouldHaveCorrectIntValues(MagicLinkExpiredReason reason, int expected)
        {
            // Assert
            ((int)reason).Should().Be(expected);
        }

        #endregion

        #region CreateMagicLinkRequest Tests

        [Fact]
        public void CreateMagicLinkRequest_ShouldHaveDefaultValues()
        {
            // Arrange & Act
            var request = new CreateMagicLinkRequest();

            // Assert
            request.Type.Should().Be(MagicLinkType.Action);
            request.Uri.Should().Be(string.Empty);
            request.UsageLimit.Should().Be(0);
            request.ExpiryLifeSpan.Should().Be(0);
            request.UserCanLogin.Should().BeFalse();
            request.Persistent.Should().BeFalse();
        }

        [Fact]
        public void CreateMagicLinkRequest_ShouldStoreAllProperties()
        {
            // Arrange & Act
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Name = "Test Link",
                Uri = "https://example.com",
                UriOnForbidden = "https://forbidden.com",
                RequestMethod = "POST",
                RequestPayload = "{\"data\": 1}",
                RequestHeaders = "{\"X-Custom\": \"header\"}",
                RequestEncodedQueryString = "foo=bar",
                RedirectUrl = "https://redirect.com",
                UsageLimit = 100,
                ExpiryLifeSpan = 86400000,
                RequestByUserId = "user-abc",
                UserCanLogin = true,
                ClientCredential = "cred-123",
                LinkBasedActionConfigId = "config-abc",
                Persistent = true,
                ProjectKey = "project-123"
            };

            // Assert
            request.Type.Should().Be(MagicLinkType.Redirect);
            request.Name.Should().Be("Test Link");
            request.Uri.Should().Be("https://example.com");
            request.UriOnForbidden.Should().Be("https://forbidden.com");
            request.RequestMethod.Should().Be("POST");
            request.RequestPayload.Should().Be("{\"data\": 1}");
            request.RequestHeaders.Should().Be("{\"X-Custom\": \"header\"}");
            request.RequestEncodedQueryString.Should().Be("foo=bar");
            request.RedirectUrl.Should().Be("https://redirect.com");
            request.UsageLimit.Should().Be(100);
            request.ExpiryLifeSpan.Should().Be(86400000);
            request.RequestByUserId.Should().Be("user-abc");
            request.UserCanLogin.Should().BeTrue();
            request.ClientCredential.Should().Be("cred-123");
            request.LinkBasedActionConfigId.Should().Be("config-abc");
            request.Persistent.Should().BeTrue();
            request.ProjectKey.Should().Be("project-123");
        }

        #endregion

        #region CreateMagicLinkResponse Tests

        [Fact]
        public void CreateMagicLinkResponse_ShouldHaveDefaultValues()
        {
            // Arrange & Act
            var response = new CreateMagicLinkResponse();

            // Assert
            response.LinkId.Should().Be(string.Empty);
            response.ShortUri.Should().Be(string.Empty);
            response.Type.Should().Be(string.Empty);
            response.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public void CreateMagicLinkResponse_ShouldStoreAllProperties()
        {
            // Arrange & Act
            var response = new CreateMagicLinkResponse
            {
                IsSuccess = true,
                LinkId = "link-123",
                ShortUri = "https://short.url/link-123",
                Type = "Redirect",
                ErrorMessage = null
            };

            // Assert
            response.IsSuccess.Should().BeTrue();
            response.LinkId.Should().Be("link-123");
            response.ShortUri.Should().Be("https://short.url/link-123");
            response.Type.Should().Be("Redirect");
        }

        [Fact]
        public void CreateMagicLinkResponse_ShouldStoreErrorMessage_OnFailure()
        {
            // Arrange & Act
            var response = new CreateMagicLinkResponse
            {
                IsSuccess = false,
                ErrorMessage = "Failed to create link"
            };

            // Assert
            response.IsSuccess.Should().BeFalse();
            response.ErrorMessage.Should().Be("Failed to create link");
        }

        #endregion

        #region CreateMagicLinksRequest Tests

        [Fact]
        public void CreateMagicLinksRequest_ShouldHaveDefaultEmptyList()
        {
            // Arrange & Act
            var request = new CreateMagicLinksRequest();

            // Assert
            request.Requests.Should().NotBeNull();
            request.Requests.Should().BeEmpty();
            request.ProjectKey.Should().BeNull();
        }

        [Fact]
        public void CreateMagicLinksRequest_ShouldStoreMultipleRequests()
        {
            // Arrange & Act
            var request = new CreateMagicLinksRequest
            {
                Requests = new List<CreateMagicLinkRequest>
                {
                    new() { Uri = "https://link1.com" },
                    new() { Uri = "https://link2.com" },
                    new() { Uri = "https://link3.com" }
                },
                ProjectKey = "multi-project"
            };

            // Assert
            request.Requests.Should().HaveCount(3);
            request.ProjectKey.Should().Be("multi-project");
        }

        #endregion

        #region MagicLinkResult Tests

        [Fact]
        public void MagicLinkResult_ShouldHaveDefaultValues()
        {
            // Arrange & Act
            var result = new MagicLinkResult();

            // Assert
            result.Id.Should().Be(string.Empty);
            result.ShortUri.Should().Be(string.Empty);
            result.Type.Should().Be(string.Empty);
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public void MagicLinkResult_ShouldStoreSuccessResult()
        {
            // Arrange & Act
            var result = new MagicLinkResult
            {
                Id = "result-123",
                ShortUri = "https://short.url/result-123",
                Type = "Action",
                IsSuccess = true
            };

            // Assert
            result.Id.Should().Be("result-123");
            result.ShortUri.Should().Be("https://short.url/result-123");
            result.Type.Should().Be("Action");
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public void MagicLinkResult_ShouldStoreFailedResult()
        {
            // Arrange & Act
            var result = new MagicLinkResult
            {
                Id = "failed-123",
                IsSuccess = false,
                ErrorMessage = "Invalid URL format"
            };

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("Invalid URL format");
        }

        #endregion

        #region InvokeMagicLinkRequest Tests

        [Fact]
        public void InvokeMagicLinkRequest_ShouldHaveDefaultValues()
        {
            // Arrange & Act
            var request = new InvokeMagicLinkRequest();

            // Assert
            request.LinkId.Should().Be(string.Empty);
            request.NotifyOnProcessEnding.Should().BeFalse();
            request.RaiseEventOnProcessEnding.Should().BeFalse();
        }

        [Fact]
        public void InvokeMagicLinkRequest_ShouldStoreVisitorInformation()
        {
            // Arrange & Act
            var request = new InvokeMagicLinkRequest
            {
                LinkId = "link-abc",
                ProjectKey = "project-xyz",
                VisitorIpAddress = "192.168.1.100",
                VisitorUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
                VisitorOrigin = "https://referrer.example.com",
                VisitorLanguage = "en-US,en;q=0.9"
            };

            // Assert
            request.LinkId.Should().Be("link-abc");
            request.ProjectKey.Should().Be("project-xyz");
            request.VisitorIpAddress.Should().Be("192.168.1.100");
            request.VisitorUserAgent.Should().Be("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            request.VisitorOrigin.Should().Be("https://referrer.example.com");
            request.VisitorLanguage.Should().Be("en-US,en;q=0.9");
        }

        #endregion

        #region InvokeMagicLinkResponse Tests

        [Fact]
        public void InvokeMagicLinkResponse_ShouldHaveNullableDefaults()
        {
            // Arrange & Act
            var response = new InvokeMagicLinkResponse();

            // Assert
            response.RedirectUrl.Should().BeNull();
            response.ErrorCode.Should().BeNull();
            response.ErrorMessage.Should().BeNull();
            response.Type.Should().BeNull();
        }

        [Fact]
        public void InvokeMagicLinkResponse_ShouldStoreSuccessRedirect()
        {
            // Arrange & Act
            var response = new InvokeMagicLinkResponse
            {
                IsSuccess = true,
                RedirectUrl = "https://destination.example.com",
                Type = "Redirect"
            };

            // Assert
            response.IsSuccess.Should().BeTrue();
            response.RedirectUrl.Should().Be("https://destination.example.com");
            response.Type.Should().Be("Redirect");
        }

        [Fact]
        public void InvokeMagicLinkResponse_ShouldStoreError()
        {
            // Arrange & Act
            var response = new InvokeMagicLinkResponse
            {
                IsSuccess = false,
                ErrorCode = "EXPIRED",
                ErrorMessage = "The magic link has expired"
            };

            // Assert
            response.IsSuccess.Should().BeFalse();
            response.ErrorCode.Should().Be("EXPIRED");
            response.ErrorMessage.Should().Be("The magic link has expired");
        }

        #endregion

        #region RemoveMagicLinksRequest Tests

        [Fact]
        public void RemoveMagicLinksRequest_ShouldHaveDefaultEmptyList()
        {
            // Arrange & Act
            var request = new RemoveMagicLinksRequest();

            // Assert
            request.LinkIds.Should().NotBeNull();
            request.LinkIds.Should().BeEmpty();
            request.ProjectKey.Should().BeNull();
        }

        [Fact]
        public void RemoveMagicLinksRequest_ShouldStoreMultipleLinkIds()
        {
            // Arrange & Act
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "link-1", "link-2", "link-3" },
                ProjectKey = "remove-project"
            };

            // Assert
            request.LinkIds.Should().HaveCount(3);
            request.LinkIds.Should().Contain("link-1");
            request.LinkIds.Should().Contain("link-2");
            request.LinkIds.Should().Contain("link-3");
            request.ProjectKey.Should().Be("remove-project");
        }

        #endregion

        #region RemoveMagicLinksResponse Tests

        [Fact]
        public void RemoveMagicLinksResponse_ShouldHaveDefaultZeroRemoved()
        {
            // Arrange & Act
            var response = new RemoveMagicLinksResponse();

            // Assert
            response.RemovedCount.Should().Be(0);
            response.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public void RemoveMagicLinksResponse_ShouldStoreRemovedCount()
        {
            // Arrange & Act
            var response = new RemoveMagicLinksResponse
            {
                IsSuccess = true,
                RemovedCount = 5
            };

            // Assert
            response.IsSuccess.Should().BeTrue();
            response.RemovedCount.Should().Be(5);
        }

        #endregion

        #region MagicLinkDto.CalculateStatus Tests

        [Fact]
        public void CalculateStatus_ShouldReturnExpired_WhenIsExpiredTrue()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = true,
                ExpiredReason = "ManuallyDisabled"
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("ManuallyDisabled");
        }

        [Fact]
        public void CalculateStatus_ShouldReturnExpired_WhenIsExpiredTrueAndReasonIsNull()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = true,
                ExpiredReason = null
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("Expired");
        }

        [Fact]
        public void CalculateStatus_ShouldReturnUsageLimitExceeded_WhenUsageExceedsLimit()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = false,
                UsageLimit = 5,
                UsageCount = 5
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("UsageLimitExceeded");
        }

        [Fact]
        public void CalculateStatus_ShouldReturnUsageLimitExceeded_WhenUsageGreaterThanLimit()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = false,
                UsageLimit = 5,
                UsageCount = 10
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("UsageLimitExceeded");
        }

        [Fact]
        public void CalculateStatus_ShouldNotReturnUsageLimitExceeded_WhenLimitIsZero()
        {
            // Arrange - UsageLimit of 0 means unlimited
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = false,
                UsageLimit = 0,
                UsageCount = 100
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("Active");
        }

        [Fact]
        public void CalculateStatus_ShouldReturnTimeExpired_WhenExpiryDatePassed()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = false,
                UsageLimit = 0,
                ExpiryDate = DateTime.UtcNow.AddHours(-1) // Expired 1 hour ago
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("TimeExpired");
        }

        [Fact]
        public void CalculateStatus_ShouldReturnActive_WhenExpiryDateNotPassed()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = false,
                UsageLimit = 0,
                ExpiryDate = DateTime.UtcNow.AddHours(1) // Expires in 1 hour
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("Active");
        }

        [Fact]
        public void CalculateStatus_ShouldReturnLifespanExpired_WhenLifespanExpiredButNoExpiryDate()
        {
            // Arrange - Link created 2 hours ago with 1 hour lifespan, but no ExpiryDate set
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = false,
                UsageLimit = 0,
                ExpiryDate = null,
                ExpiryLifeSpan = 3600000, // 1 hour in ms
                CreatedAt = DateTime.UtcNow.AddHours(-2) // Created 2 hours ago
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("LifespanExpired");
        }

        [Fact]
        public void CalculateStatus_ShouldReturnActive_WhenLifespanNotExpired()
        {
            // Arrange - Link created 30 mins ago with 1 hour lifespan
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = false,
                UsageLimit = 0,
                ExpiryDate = null,
                ExpiryLifeSpan = 3600000, // 1 hour in ms
                CreatedAt = DateTime.UtcNow.AddMinutes(-30) // Created 30 mins ago
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("Active");
        }

        [Fact]
        public void CalculateStatus_ShouldReturnActive_WhenNoExpiryLimits()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = false,
                UsageLimit = 0,
                ExpiryDate = null,
                ExpiryLifeSpan = 0,
                CreatedAt = DateTime.UtcNow
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("Active");
        }

        [Fact]
        public void CalculateStatus_ShouldPrioritizeIsExpired_OverOtherConditions()
        {
            // Arrange - IsExpired takes precedence even if usage limit not exceeded
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = true,
                ExpiredReason = "ManuallyDisabled",
                UsageLimit = 100,
                UsageCount = 1
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("ManuallyDisabled");
        }

        [Fact]
        public void CalculateStatus_ShouldPrioritizeUsageLimit_OverTimeExpiry()
        {
            // Arrange - Usage limit exceeded should be checked before time expiry
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                IsExpired = false,
                UsageLimit = 5,
                UsageCount = 5,
                ExpiryDate = DateTime.UtcNow.AddHours(-1) // Also time expired
            };

            // Act
            var status = MagicLinkDto.CalculateStatus(link);

            // Assert
            status.Should().Be("UsageLimitExceeded");
        }

        #endregion

        #region MagicLinkDto.FromEntity Tests

        [Fact]
        public void FromEntity_ShouldMapAllProperties()
        {
            // Arrange
            var entity = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-id",
                Type = MagicLinkType.Redirect,
                Name = "Test Link",
                Uri = "https://example.com",
                UriOnForbidden = "https://forbidden.com",
                RequestMethod = "GET",
                RequestPayload = "{}",
                RequestHeaders = "{}",
                RequestEncodedQueryString = "test=1",
                RedirectUrl = "https://redirect.com",
                UsageLimit = 10,
                UsageCount = 2,
                ExpiryLifeSpan = 3600000,
                ExpiryDate = DateTime.UtcNow.AddHours(1),
                IsExpired = false,
                ExpiredReason = null,
                ProjectKey = "project-123",
                ShortUri = "https://short.test/test-id",
                RequestByUserId = "user-123",
                UserCanLogin = true,
                ClientCredential = "cred",
                LinkBasedActionConfigId = "config-123",
                Language = "en",
                Origin = "https://origin.com",
                Persistent = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "admin",
                UpdatedAt = DateTime.UtcNow
            };

            // Act
            var dto = MagicLinkDto.FromEntity(entity);

            // Assert
            dto.ItemId.Should().Be("test-id");
            dto.Type.Should().Be("Redirect");
            dto.Name.Should().Be("Test Link");
            dto.Uri.Should().Be("https://example.com");
            dto.UriOnForbidden.Should().Be("https://forbidden.com");
            dto.RequestMethod.Should().Be("GET");
            dto.RequestPayload.Should().Be("{}");
            dto.RequestHeaders.Should().Be("{}");
            dto.RequestEncodedQueryString.Should().Be("test=1");
            dto.RedirectUrl.Should().Be("https://redirect.com");
            dto.UsageLimit.Should().Be(10);
            dto.UsageCount.Should().Be(2);
            dto.ExpiryLifeSpan.Should().Be(3600000);
            dto.IsExpired.Should().BeFalse();
            dto.ProjectKey.Should().Be("project-123");
            dto.ShortUri.Should().Be("https://short.test/test-id");
            dto.RequestByUserId.Should().Be("user-123");
            dto.UserCanLogin.Should().BeTrue();
            dto.ClientCredential.Should().Be("cred");
            dto.LinkBasedActionConfigId.Should().Be("config-123");
            dto.Language.Should().Be("en");
            dto.Origin.Should().Be("https://origin.com");
            dto.Persistent.Should().BeTrue();
            dto.CreatedBy.Should().Be("admin");
            dto.Status.Should().Be("Active");
        }

        [Fact]
        public void FromEntity_ShouldComputeCorrectStatus()
        {
            // Arrange - Expired link
            var entity = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "expired-link",
                IsExpired = true,
                ExpiredReason = "ManuallyDisabled"
            };

            // Act
            var dto = MagicLinkDto.FromEntity(entity);

            // Assert
            dto.Status.Should().Be("ManuallyDisabled");
        }

        #endregion

        #region Additional Contract Coverage

        [Fact]
        public void GetMagicLinksRequest_AndDateRange_ShouldStoreDefaultsAndValues()
        {
            var request = new GetMagicLinksRequest();
            request.PageSize.Should().Be(10);
            request.PageNumber.Should().Be(0);
            request.ProjectKey.Should().BeNull();

            var range = new DateRange
            {
                StartDate = DateTime.UtcNow.Date,
                EndDate = DateTime.UtcNow.Date.AddDays(1)
            };

            request.ProjectKey = "p1";
            request.Type = MagicLinkType.Redirect;
            request.SearchText = "abc";
            request.Status = "Active";
            request.RequestMethod = "GET";
            request.ExpiryDateRange = range;

            request.ProjectKey.Should().Be("p1");
            request.Type.Should().Be(MagicLinkType.Redirect);
            request.SearchText.Should().Be("abc");
            request.Status.Should().Be("Active");
            request.RequestMethod.Should().Be("GET");
            request.ExpiryDateRange.Should().BeSameAs(range);
        }

        [Fact]
        public void MagicLinkVisitorUsage_AndClientCredential_ShouldStoreValues()
        {
            var usage = new MagicLinkVisitorUsage
            {
                LinkId = "l1",
                ProjectKey = "p1",
                VisitorIpAddress = "127.0.0.1",
                VisitorUserAgent = "ua",
                VisitorOrigin = "https://origin",
                VisitorLanguage = "en",
                LinkType = "Action",
                ActionSuccess = true,
                ActionStatusCode = 200,
                ActionErrorMessage = null
            };

            var credential = new ClientCredential
            {
                Name = "cred",
                ClientSecret = "secret",
                Roles = new List<string> { "admin" },
                IsActive = true,
                Audiences = new List<string> { "api" }
            };

            usage.ItemId.Should().NotBeNullOrWhiteSpace();
            usage.LinkId.Should().Be("l1");
            usage.ActionStatusCode.Should().Be(200);
            credential.Name.Should().Be("cred");
            credential.Roles.Should().ContainSingle().Which.Should().Be("admin");
            credential.Audiences.Should().ContainSingle().Which.Should().Be("api");
            credential.IsActive.Should().BeTrue();
        }

        #endregion
    }
}
