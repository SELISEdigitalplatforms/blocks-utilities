using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Serves one key ring to every scope, so tests that are not about scoping stay about what they
/// were about. Tests that <em>are</em> about scoping use <see cref="ScopedKeyRingProvider"/>.
/// </summary>
public sealed class FixedKeyRingProvider : IProviderTokenEncryptionKeyRingProvider
{
    private readonly IProviderTokenEncryptionKeyRing _keyRing;

    public FixedKeyRingProvider(IProviderTokenEncryptionKeyRing keyRing)
    {
        _keyRing = keyRing;
    }

    public ValueTask<IProviderTokenEncryptionKeyRing> GetAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_keyRing);

    public ValueTask<PaymentKeyRingHealth> CheckAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            new PaymentKeyRingHealth(
                !string.IsNullOrEmpty(_keyRing.ActiveKeyId),
                PaymentKeyRingSecretName.Create(scope),
                false,
                _keyRing.ActiveKeyId,
                string.Empty));
}

/// <summary>
/// Serves a different key ring per scope, and nothing at all to a scope it does not know —
/// which is what one organization holding another's key ring would have to survive.
/// </summary>
public sealed class ScopedKeyRingProvider : IProviderTokenEncryptionKeyRingProvider
{
    private static readonly IProviderTokenEncryptionKeyRing Unavailable =
        new UnavailableProviderTokenEncryptionKeyRing();

    private readonly Dictionary<string, IProviderTokenEncryptionKeyRing> _rings;

    public ScopedKeyRingProvider(
        Dictionary<string, IProviderTokenEncryptionKeyRing> rings)
    {
        _rings = rings;
    }

    public ValueTask<IProviderTokenEncryptionKeyRing> GetAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            _rings.TryGetValue(scope.ToString(), out var ring)
                ? ring
                : Unavailable);

    public ValueTask<PaymentKeyRingHealth> CheckAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            new PaymentKeyRingHealth(
                _rings.ContainsKey(scope.ToString()),
                PaymentKeyRingSecretName.Create(scope),
                false,
                string.Empty,
                string.Empty));
}
