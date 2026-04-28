using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using Captcha.DomainService.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using System.Net;

namespace XUnitTest.Captcha
{
    public class CaptchaVerificationTests
    {
        #region BlocksCaptchaVerificationService Tests

        [Fact]
        public async Task BlocksCaptcha_VerifyAsync_WithValidCode_ReturnsVerifiedResult()
        {
            // Arrange
            var cacheClient = new Mock<ICacheClient>();
            var verificationCode = "valid-code-123";
            var expectedHostName = "test.com";

            cacheClient.Setup(x => x.GetStringValueAsync(verificationCode))
                .ReturnsAsync(expectedHostName);
            cacheClient.Setup(x => x.RemoveKeyAsync(verificationCode))
                .ReturnsAsync(true);

            var service = new BlocksCaptchaVerificationService(cacheClient.Object);

            // Act
            var result = await service.VerifyAsync(verificationCode);

            // Assert
            result.Should().NotBeNull();
            result.Verified.Should().BeTrue();
            result.HostName.Should().Be(expectedHostName);
            result.Errors.Should().BeNull();

            cacheClient.Verify(x => x.GetStringValueAsync(verificationCode), Times.Once);
            cacheClient.Verify(x => x.RemoveKeyAsync(verificationCode), Times.Once);
        }

        [Fact]
        public async Task BlocksCaptcha_VerifyAsync_WithInvalidCode_ReturnsFailedResult()
        {
            // Arrange
            var cacheClient = new Mock<ICacheClient>();
            var verificationCode = "invalid-code";

            cacheClient.Setup(x => x.GetStringValueAsync(verificationCode))
                .ReturnsAsync((string)null);

            var service = new BlocksCaptchaVerificationService(cacheClient.Object);

            // Act
            var result = await service.VerifyAsync(verificationCode);

            // Assert
            result.Should().NotBeNull();
            result.Verified.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
            result.Errors["VerificationCode"].Should().Be("Verification code incorrect or expire.");

            cacheClient.Verify(x => x.GetStringValueAsync(verificationCode), Times.Once);
            cacheClient.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task BlocksCaptcha_VerifyAsync_WithEmptyString_ReturnsFailedResult()
        {
            // Arrange
            var cacheClient = new Mock<ICacheClient>();
            var verificationCode = "code-with-empty";

            cacheClient.Setup(x => x.GetStringValueAsync(verificationCode))
                .ReturnsAsync("   ");

            var service = new BlocksCaptchaVerificationService(cacheClient.Object);

            // Act
            var result = await service.VerifyAsync(verificationCode);

            // Assert
            result.Verified.Should().BeFalse();
            cacheClient.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region CaptchaVerificationServiceProvider Tests

        [Theory]
        [InlineData("bcaptcha")]
        [InlineData("BCAPTCHA")]
        [InlineData("BcApTcHa")]
        public void Provider_GetService_WithBCaptcha_ReturnsBlocksService(string provider)
        {
            // Arrange
            var serviceProvider = new Mock<IServiceProvider>();
            var blocksService = new BlocksCaptchaVerificationService(Mock.Of<ICacheClient>());

            serviceProvider.Setup(x => x.GetService(typeof(BlocksCaptchaVerificationService)))
                .Returns(blocksService);

            var captchaProvider = new CaptchaVerificationServiceProvider(serviceProvider.Object);

            // Act
            var result = captchaProvider.GetCaptchaVerificationService(provider);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeOfType<BlocksCaptchaVerificationService>();
        }

        [Theory]
        [InlineData("recaptcha")]
        [InlineData("RECAPTCHA")]
        [InlineData("ReCaPtChA")]
        public void Provider_GetService_WithReCaptcha_ReturnsReCaptchaService(string provider)
        {
            // Arrange
            var serviceProvider = new Mock<IServiceProvider>();
            var reCaptchaService = new ReCaptchaVerificationService(
                Mock.Of<IHttpClientService>(),
                Mock.Of<IConfiguration>(),
                Mock.Of<ILogger<ReCaptchaVerificationService>>(),
                Mock.Of<IRecaptchaConfigFactory>());

            serviceProvider.Setup(x => x.GetService(typeof(ReCaptchaVerificationService)))
                .Returns(reCaptchaService);

            var captchaProvider = new CaptchaVerificationServiceProvider(serviceProvider.Object);

            // Act
            var result = captchaProvider.GetCaptchaVerificationService(provider);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeOfType<ReCaptchaVerificationService>();
        }

        [Theory]
        [InlineData("hcaptcha")]
        [InlineData("HCAPTCHA")]
        [InlineData("HCaPtChA")]
        public void Provider_GetService_WithHCaptcha_ReturnsHCaptchaService(string provider)
        {
            // Arrange
            var serviceProvider = new Mock<IServiceProvider>();
            var hCaptchaService = new HCaptchaVerificationService(
                Mock.Of<ICaptchaConfigurationService>(),
                Mock.Of<IConfiguration>(),
                Mock.Of<ILogger<HCaptchaVerificationService>>(),
                Mock.Of<IHttpClientService>());

            serviceProvider.Setup(x => x.GetService(typeof(HCaptchaVerificationService)))
                .Returns(hCaptchaService);

            var captchaProvider = new CaptchaVerificationServiceProvider(serviceProvider.Object);

            // Act
            var result = captchaProvider.GetCaptchaVerificationService(provider);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeOfType<HCaptchaVerificationService>();
        }

        [Fact]
        public void Provider_GetService_WithNullProvider_ReturnsBCaptchaService()
        {
            // Arrange
            var serviceProvider = new Mock<IServiceProvider>();
            var blocksService = new BlocksCaptchaVerificationService(Mock.Of<ICacheClient>());

            serviceProvider.Setup(x => x.GetService(typeof(BlocksCaptchaVerificationService)))
                .Returns(blocksService);

            var captchaProvider = new CaptchaVerificationServiceProvider(serviceProvider.Object);

            // Act
            var result = captchaProvider.GetCaptchaVerificationService(null);

            // Assert
            result.Should().BeOfType<BlocksCaptchaVerificationService>();
        }

        [Fact]
        public void Provider_GetService_WithEmptyProvider_ReturnsBCaptchaService()
        {
            // Arrange
            var serviceProvider = new Mock<IServiceProvider>();
            var blocksService = new BlocksCaptchaVerificationService(Mock.Of<ICacheClient>());

            serviceProvider.Setup(x => x.GetService(typeof(BlocksCaptchaVerificationService)))
                .Returns(blocksService);

            var captchaProvider = new CaptchaVerificationServiceProvider(serviceProvider.Object);

            // Act
            var result = captchaProvider.GetCaptchaVerificationService("   ");

            // Assert
            result.Should().BeOfType<BlocksCaptchaVerificationService>();
        }

        [Fact]
        public void Provider_GetService_WithUnknownProvider_ReturnsNull()
        {
            // Arrange
            var serviceProvider = new Mock<IServiceProvider>();
            var captchaProvider = new CaptchaVerificationServiceProvider(serviceProvider.Object);

            // Act
            var result = captchaProvider.GetCaptchaVerificationService("unknown-provider");

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region DbReCaptchaConfig Tests

        [Fact]
        public void DbConfig_ResolveRecaptchaUri_ReturnsFormattedUri()
        {
            // Arrange
            var config = new CaptchaConfiguration
            {
                CaptchaSecret = "test-secret-key"
            };
            var token = "test-token-123";

            var dbConfig = new DbReCaptchaConfig(config, token);

            // Act
            var result = dbConfig.ResolveRecaptchaUri();

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().Contain("https://www.google.com/recaptcha/api/siteverify");
            result.Should().Contain("secret=test-secret-key");
            result.Should().Contain($"response={token}");
        }

        [Fact]
        public void DbConfig_Constructor_StoresTokenAndConfig()
        {
            // Arrange
            var config = new CaptchaConfiguration
            {
                CaptchaSecret = "secret-key-abc"
            };
            var token = "token-xyz";

            // Act
            var dbConfig = new DbReCaptchaConfig(config, token);
            var uri = dbConfig.ResolveRecaptchaUri();

            // Assert
            uri.Should().Contain("secret-key-abc");
            uri.Should().Contain("token-xyz");
        }

        #endregion

        #region LocalReCaptchaConfig Tests

        [Fact]
        public void LocalConfig_ResolveRecaptchaUri_ReturnsFormattedUri()
        {
            // Arrange
            var verificationUri = "https://recaptcha.example.com/verify?token={0}";
            var token = "test-token-456";

            var localConfig = new LocalReCaptchaConfig(verificationUri, token);

            // Act
            var result = localConfig.ResolveRecaptchaUri();

            // Assert
            result.Should().Be($"https://recaptcha.example.com/verify?token={token}");
        }

        [Fact]
        public void LocalConfig_Constructor_StoresUriAndToken()
        {
            // Arrange
            var uri = "http://test.com/verify?code={0}";
            var token = "abc123";

            // Act
            var localConfig = new LocalReCaptchaConfig(uri, token);
            var result = localConfig.ResolveRecaptchaUri();

            // Assert
            result.Should().Contain(token);
            result.Should().Contain("test.com/verify");
        }

        #endregion

        #region RecaptchaConfigFactory Tests

        [Fact]
        public async Task Factory_GetRecaptchaConfig_WithDbConfig_ReturnsDbConfig()
        {
            // Arrange
            var logger = new Mock<ILogger<RecaptchaConfigFactory>>();
            var configService = new Mock<ICaptchaConfigurationService>();
            var dbConfig = new CaptchaConfiguration
            {
                CaptchaSecret = "db-secret"
            };

            configService.Setup(x => x.GetCaptchaConfigurationAsync())
                .ReturnsAsync(dbConfig);

            var factory = new RecaptchaConfigFactory(logger.Object, configService.Object);

            // Act
            var result = await factory.GetRecaptchaConfig("local-uri-{0}", "token");

            // Assert
            result.Should().BeOfType<DbReCaptchaConfig>();
            var uri = result.ResolveRecaptchaUri();
            uri.Should().Contain("db-secret");
        }

        [Fact]
        public async Task Factory_GetRecaptchaConfig_WithNullDbConfig_ReturnsLocalConfig()
        {
            // Arrange
            var logger = new Mock<ILogger<RecaptchaConfigFactory>>();
            var configService = new Mock<ICaptchaConfigurationService>();

            configService.Setup(x => x.GetCaptchaConfigurationAsync())
                .ReturnsAsync((CaptchaConfiguration)null);

            var factory = new RecaptchaConfigFactory(logger.Object, configService.Object);
            var localUri = "http://local.com/verify?token={0}";
            var token = "test-token";

            // Act
            var result = await factory.GetRecaptchaConfig(localUri, token);

            // Assert
            result.Should().BeOfType<LocalReCaptchaConfig>();
            result.ResolveRecaptchaUri().Should().Contain(token);
        }

        [Fact]
        public async Task Factory_GetRecaptchaConfig_WithException_ReturnsLocalConfig()
        {
            // Arrange
            var logger = new Mock<ILogger<RecaptchaConfigFactory>>();
            var configService = new Mock<ICaptchaConfigurationService>();

            configService.Setup(x => x.GetCaptchaConfigurationAsync())
                .ThrowsAsync(new Exception("Database error"));

            var factory = new RecaptchaConfigFactory(logger.Object, configService.Object);
            var localUri = "http://fallback.com/verify?token={0}";
            var token = "fallback-token";

            // Act
            var result = await factory.GetRecaptchaConfig(localUri, token);

            // Assert
            result.Should().BeOfType<LocalReCaptchaConfig>();
        }

        [Fact]
        public async Task Factory_GetConfigFromDb_ReturnsConfiguration()
        {
            // Arrange
            var logger = new Mock<ILogger<RecaptchaConfigFactory>>();
            var configService = new Mock<ICaptchaConfigurationService>();
            var expectedConfig = new CaptchaConfiguration { CaptchaSecret = "test" };

            configService.Setup(x => x.GetCaptchaConfigurationAsync())
                .ReturnsAsync(expectedConfig);

            var factory = new RecaptchaConfigFactory(logger.Object, configService.Object);

            // Act
            var result = await factory.GetConfigFromDb();

            // Assert
            result.Should().Be(expectedConfig);
        }

        #endregion

        #region HCaptchaVerificationService Tests

        [Fact]
        public async Task HCaptcha_VerifyAsync_WithSuccessfulResponse_ReturnsVerifiedResult()
        {
            // Arrange
            var configService = new Mock<ICaptchaConfigurationService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<HCaptchaVerificationService>>();
            var httpClientService = new Mock<IHttpClientService>();

            var dbConfig = new CaptchaConfiguration { CaptchaSecret = "hcaptcha-secret" };
            var recaptchaResponse = new RecaptchaResponse { Success = true, HostName = "test.com" };

            configService.Setup(x => x.GetCaptchaConfigurationAsync()).ReturnsAsync(dbConfig);
            configuration.Setup(x => x["HCpatchaVerificationUrl"]).Returns("https://hcaptcha.com/verify");

            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(recaptchaResponse))
            };

            httpClientService.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string>()))
                .ReturnsAsync(httpResponse);

            var service = new HCaptchaVerificationService(
                configService.Object,
                configuration.Object,
                logger.Object,
                httpClientService.Object);

            // Act
            var result = await service.VerifyAsync("test-token");

            // Assert
            result.Should().NotBeNull();
            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("test.com");
        }

        [Fact]
        public async Task HCaptcha_VerifyAsync_WithFailedResponse_ReturnsFailedResult()
        {
            // Arrange
            var configService = new Mock<ICaptchaConfigurationService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<HCaptchaVerificationService>>();
            var httpClientService = new Mock<IHttpClientService>();

            var dbConfig = new CaptchaConfiguration { CaptchaSecret = "secret" };
            var recaptchaResponse = new RecaptchaResponse { Success = false, HostName = null };

            configService.Setup(x => x.GetCaptchaConfigurationAsync()).ReturnsAsync(dbConfig);
            configuration.Setup(x => x["HCpatchaVerificationUrl"]).Returns("https://hcaptcha.com/verify");

            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(recaptchaResponse))
            };

            httpClientService.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string>()))
                .ReturnsAsync(httpResponse);

            var service = new HCaptchaVerificationService(
                configService.Object,
                configuration.Object,
                logger.Object,
                httpClientService.Object);

            // Act
            var result = await service.VerifyAsync("test-token");

            // Assert
            result.Verified.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
        }

        [Fact]
        public async Task HCaptcha_VerifyCaptchaAsync_WithNullDbConfig_ReturnsFailedResponse()
        {
            // Arrange
            var configService = new Mock<ICaptchaConfigurationService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<HCaptchaVerificationService>>();
            var httpClientService = new Mock<IHttpClientService>();

            configService.Setup(x => x.GetCaptchaConfigurationAsync()).ReturnsAsync((CaptchaConfiguration)null);
            configuration.Setup(x => x["HCpatchaVerificationUrl"]).Returns("https://hcaptcha.com/verify");

            var service = new HCaptchaVerificationService(
                configService.Object,
                configuration.Object,
                logger.Object,
                httpClientService.Object);

            // Act
            var result = await service.VerifyCaptchaAsync("token");

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.HostName.Should().BeNull();
        }

        [Fact]
        public async Task HCaptcha_VerifyCaptchaAsync_WithNullSecret_ReturnsFailedResponse()
        {
            // Arrange
            var configService = new Mock<ICaptchaConfigurationService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<HCaptchaVerificationService>>();
            var httpClientService = new Mock<IHttpClientService>();

            var dbConfig = new CaptchaConfiguration { CaptchaSecret = null };

            configService.Setup(x => x.GetCaptchaConfigurationAsync()).ReturnsAsync(dbConfig);
            configuration.Setup(x => x["HCpatchaVerificationUrl"]).Returns("https://hcaptcha.com/verify");

            var service = new HCaptchaVerificationService(
                configService.Object,
                configuration.Object,
                logger.Object,
                httpClientService.Object);

            // Act
            var result = await service.VerifyCaptchaAsync("token");

            // Assert
            result.Success.Should().BeFalse();
        }

        [Fact]
        public async Task HCaptcha_VerifyCaptchaAsync_WithHttpFailure_ReturnsFailedResponse()
        {
            // Arrange
            var configService = new Mock<ICaptchaConfigurationService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<HCaptchaVerificationService>>();
            var httpClientService = new Mock<IHttpClientService>();

            var dbConfig = new CaptchaConfiguration { CaptchaSecret = "secret" };

            configService.Setup(x => x.GetCaptchaConfigurationAsync()).ReturnsAsync(dbConfig);
            configuration.Setup(x => x["HCpatchaVerificationUrl"]).Returns("https://hcaptcha.com/verify");

            var httpResponse = new HttpResponseMessage(HttpStatusCode.BadRequest);

            httpClientService.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string>()))
                .ReturnsAsync(httpResponse);

            var service = new HCaptchaVerificationService(
                configService.Object,
                configuration.Object,
                logger.Object,
                httpClientService.Object);

            // Act
            var result = await service.VerifyCaptchaAsync("token");

            // Assert
            result.Success.Should().BeFalse();
        }

        [Fact]
        public async Task HCaptcha_VerifyCaptchaAsync_WithException_ReturnsFailedResponse()
        {
            // Arrange
            var configService = new Mock<ICaptchaConfigurationService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<HCaptchaVerificationService>>();
            var httpClientService = new Mock<IHttpClientService>();

            configService.Setup(x => x.GetCaptchaConfigurationAsync())
                .ThrowsAsync(new Exception("Network error"));
            configuration.Setup(x => x["HCpatchaVerificationUrl"]).Returns("https://hcaptcha.com/verify");

            var service = new HCaptchaVerificationService(
                configService.Object,
                configuration.Object,
                logger.Object,
                httpClientService.Object);

            // Act
            var result = await service.VerifyCaptchaAsync("token");

            // Assert
            result.Success.Should().BeFalse();
        }

        #endregion

        #region ReCaptchaVerificationService Tests

        [Fact]
        public async Task ReCaptcha_VerifyAsync_WithSuccessfulResponse_ReturnsVerifiedResult()
        {
            // Arrange
            var httpClientService = new Mock<IHttpClientService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<ReCaptchaVerificationService>>();
            var configFactory = new Mock<IRecaptchaConfigFactory>();

            configuration.Setup(x => x["ReCaptchaSecretKey"]).Returns("recaptcha-secret");

            var mockConfig = new Mock<IRecaptchaConfig>();
            mockConfig.Setup(x => x.ResolveRecaptchaUri()).Returns("https://google.com/verify?token=test");

            configFactory.Setup(x => x.GetRecaptchaConfig(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(mockConfig.Object);

            var recaptchaResponse = new RecaptchaResponse { Success = true, HostName = "example.com" };
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(recaptchaResponse))
            };

            httpClientService.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string>()))
                .ReturnsAsync(httpResponse);

            var service = new ReCaptchaVerificationService(
                httpClientService.Object,
                configuration.Object,
                logger.Object,
                configFactory.Object);

            // Act
            var result = await service.VerifyAsync("test-token");

            // Assert
            result.Should().NotBeNull();
            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("example.com");
        }

        [Fact]
        public async Task ReCaptcha_VerifyAsync_WithFailedResponse_ReturnsFailedResult()
        {
            // Arrange
            var httpClientService = new Mock<IHttpClientService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<ReCaptchaVerificationService>>();
            var configFactory = new Mock<IRecaptchaConfigFactory>();

            configuration.Setup(x => x["ReCaptchaSecretKey"]).Returns("secret");

            var mockConfig = new Mock<IRecaptchaConfig>();
            mockConfig.Setup(x => x.ResolveRecaptchaUri()).Returns("https://google.com/verify");

            configFactory.Setup(x => x.GetRecaptchaConfig(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(mockConfig.Object);

            var recaptchaResponse = new RecaptchaResponse { Success = false };
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(recaptchaResponse))
            };

            httpClientService.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string>()))
                .ReturnsAsync(httpResponse);

            var service = new ReCaptchaVerificationService(
                httpClientService.Object,
                configuration.Object,
                logger.Object,
                configFactory.Object);

            // Act
            var result = await service.VerifyAsync("test-token");

            // Assert
            result.Verified.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
        }

        [Fact]
        public async Task ReCaptcha_VerifyCaptchaAsync_WithHttpFailure_ReturnsNull()
        {
            // Arrange
            var httpClientService = new Mock<IHttpClientService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<ReCaptchaVerificationService>>();
            var configFactory = new Mock<IRecaptchaConfigFactory>();

            configuration.Setup(x => x["ReCaptchaSecretKey"]).Returns("secret");

            var mockConfig = new Mock<IRecaptchaConfig>();
            mockConfig.Setup(x => x.ResolveRecaptchaUri()).Returns("https://google.com/verify");

            configFactory.Setup(x => x.GetRecaptchaConfig(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(mockConfig.Object);

            var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);

            httpClientService.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string>()))
                .ReturnsAsync(httpResponse);

            var service = new ReCaptchaVerificationService(
                httpClientService.Object,
                configuration.Object,
                logger.Object,
                configFactory.Object);

            // Act
            var result = await service.VerifyCaptchaAsync("token");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task ReCaptcha_VerifyCaptchaAsync_WithException_ReturnsNull()
        {
            // Arrange
            var httpClientService = new Mock<IHttpClientService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<ReCaptchaVerificationService>>();
            var configFactory = new Mock<IRecaptchaConfigFactory>();

            configuration.Setup(x => x["ReCaptchaSecretKey"]).Returns("secret");

            configFactory.Setup(x => x.GetRecaptchaConfig(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Network error"));

            var service = new ReCaptchaVerificationService(
                httpClientService.Object,
                configuration.Object,
                logger.Object,
                configFactory.Object);

            // Act
            var result = await service.VerifyCaptchaAsync("token");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task ReCaptcha_ResolveVerificationUri_WithValidConfig_ReturnsUri()
        {
            // Arrange
            var httpClientService = new Mock<IHttpClientService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<ReCaptchaVerificationService>>();
            var configFactory = new Mock<IRecaptchaConfigFactory>();

            configuration.Setup(x => x["ReCaptchaSecretKey"]).Returns("secret");

            var mockConfig = new Mock<IRecaptchaConfig>();
            mockConfig.Setup(x => x.ResolveRecaptchaUri()).Returns("https://resolved-uri.com");

            configFactory.Setup(x => x.GetRecaptchaConfig(It.IsAny<string>(), "test-token"))
                .ReturnsAsync(mockConfig.Object);

            var service = new ReCaptchaVerificationService(
                httpClientService.Object,
                configuration.Object,
                logger.Object,
                configFactory.Object);

            // Act
            var result = await service.ResolveVerificationUri("test-token");

            // Assert
            result.Should().Be("https://resolved-uri.com");
        }

        [Fact]
        public async Task ReCaptcha_ResolveVerificationUri_WithException_ReturnsFallbackUri()
        {
            // Arrange
            var httpClientService = new Mock<IHttpClientService>();
            var configuration = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<ReCaptchaVerificationService>>();
            var configFactory = new Mock<IRecaptchaConfigFactory>();

            var secretKey = "fallback-secret";
            configuration.Setup(x => x["ReCaptchaSecretKey"]).Returns(secretKey);

            configFactory.Setup(x => x.GetRecaptchaConfig(It.IsAny<string>(), "error-token"))
                .ThrowsAsync(new Exception("Config error"));

            var service = new ReCaptchaVerificationService(
                httpClientService.Object,
                configuration.Object,
                logger.Object,
                configFactory.Object);

            // Act
            var result = await service.ResolveVerificationUri("error-token");

            // Assert
            result.Should().Contain(secretKey);
            result.Should().Contain("error-token");
        }

        #endregion
    }
}
