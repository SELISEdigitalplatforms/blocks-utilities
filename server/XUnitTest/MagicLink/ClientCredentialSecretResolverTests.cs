using Blocks.Genesis;
using Blocks.Secrets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Service;

namespace XUnitTest.MagicLink
{
    /// <summary>
    /// <see cref="ClientCredentialSecretResolver"/>: the reference rule, the context the worker's
    /// read runs under and its restore, and that a failed read never falls back to the plaintext.
    /// </summary>
    public sealed class ClientCredentialSecretResolverTests : IDisposable
    {
        private const string ProjectKey = "project-a";
        private const string Reference = "cs-ref-1";
        private const string StoreValue = "blxk_from-the-store";
        private const string LegacyValue = "blxk_legacy";

        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);
        private readonly Mock<ILogger<ClientCredentialSecretResolver>> _logger = new();

        public void Dispose() => BlocksContext.SetContext(null);

        private ClientCredentialSecretResolver Create()
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => _secrets.Object);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            return new ClientCredentialSecretResolver(provider.GetRequiredService<IServiceScopeFactory>(), _logger.Object);
        }

        private static ClientCredential Legacy() => new() { ItemId = "cc-1", ClientSecret = LegacyValue };

        private static ClientCredential Referenced(string plaintext = "") =>
            new() { ItemId = "cc-1", ClientSecret = plaintext, ClientSecretReference = Reference };

        private static BlocksContext Caller() =>
            BlocksContext.Create("other-tenant", [], "subscriber-7", false, "/consumer", "org-1", DateTime.UtcNow.AddMinutes(5),
                string.Empty, [], "subscriber-7", string.Empty, "Subscriber", string.Empty, "other-tenant", string.Empty);

        [Fact]
        public async Task LegacyCredential_ReturnsThePlaintext_WithoutASecretCall()
        {
            (await Create().GetSecretAsync(Legacy(), ProjectKey)).Should().Be(LegacyValue);

            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReferencedCredential_ReadsTheStore_AsAServiceInTheLinksProject_AndRestoresTheContext()
        {
            var caller = Caller();
            BlocksContext.SetContext(caller);
            BlocksContext? seen = null;
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .Callback(() => seen = BlocksContext.GetContext())
                .ReturnsAsync(StoreValue);

            var value = await Create().GetSecretAsync(Referenced(plaintext: LegacyValue), ProjectKey);

            value.Should().Be(StoreValue, "the reference wins over a plaintext left in the document");
            seen!.TenantId.Should().Be(ProjectKey);
            seen.OriginalTenantId.Should().Be(ProjectKey);
            seen.IsAuthenticated.Should().BeTrue();
            seen.UserId.Should().Be(ClientCredentialSecretResolver.SystemUserId);
            BlocksContext.GetContext()!.UserId.Should().Be("subscriber-7");
            BlocksContext.GetContext()!.TenantId.Should().Be("other-tenant");
        }

        [Fact]
        public async Task ReferencedCredential_WithNoContextBefore_LeavesNoneAfter()
        {
            BlocksContext.SetContext(null);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(StoreValue);

            await Create().GetSecretAsync(Referenced(), ProjectKey);

            BlocksContext.GetContext()?.TenantId.Should().BeNullOrEmpty();
        }

        [Theory]
        [InlineData(typeof(SecretVaultException))]
        [InlineData(typeof(SecretNotFoundException))]
        [InlineData(typeof(SecretStateException))]
        public async Task StoreFailure_ReturnsNull_NeverThePlaintext(Type failure)
        {
            Exception thrown = failure == typeof(SecretVaultException) ? new SecretVaultException("down " + Reference, "get", Reference)
                : failure == typeof(SecretNotFoundException) ? new SecretNotFoundException(Reference)
                : new SecretStateException("locked", "read");
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(thrown);
            var caller = Caller();
            BlocksContext.SetContext(caller);

            var value = await Create().GetSecretAsync(Referenced(plaintext: LegacyValue), ProjectKey);

            value.Should().BeNull();
            BlocksContext.GetContext()!.UserId.Should().Be("subscriber-7", "the context is restored after a failed read too");
        }

        [Fact]
        public async Task EmptyStoreValue_ReturnsNull()
        {
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(string.Empty);

            (await Create().GetSecretAsync(Referenced(), ProjectKey)).Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public async Task ReferencedCredential_WithoutAProject_ReturnsNull_WithoutASecretCall(string projectKey)
        {
            (await Create().GetSecretAsync(Referenced(), projectKey)).Should().BeNull();

            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Cancellation_IsNotSwallowed()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException(cts.Token));

            var act = () => Create().GetSecretAsync(Referenced(), ProjectKey, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
