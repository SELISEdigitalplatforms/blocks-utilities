namespace Payment.DomainService.Providers.HostedCheckout;

public sealed class CheckoutResultClientResolver :
    ICheckoutResultClientResolver
{
    private readonly IReadOnlyCollection<
        ICheckoutResultClient> _clients;

    public CheckoutResultClientResolver(
        IEnumerable<ICheckoutResultClient> clients)
    {
        _clients = clients.ToArray();
    }

    public ICheckoutResultClient? Resolve(
        string providerName) =>
        _clients.FirstOrDefault(client =>
            client.Supports(providerName));
}
