namespace Payment.DomainService.Providers.HostedCheckout;

public sealed class PaymentSessionClientResolver :
    IPaymentSessionClientResolver
{
    private readonly IReadOnlyCollection<
        IPaymentSessionClient> _clients;

    public PaymentSessionClientResolver(
        IEnumerable<IPaymentSessionClient> clients)
    {
        _clients = clients.ToArray();
    }

    public IPaymentSessionClient? Resolve(
        string providerName) =>
        _clients.FirstOrDefault(client =>
            client.Supports(providerName));
}
