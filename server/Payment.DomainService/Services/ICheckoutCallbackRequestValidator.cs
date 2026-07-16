namespace Payment.DomainService.Services;

public interface ICheckoutCallbackRequestValidator
{
    bool IsValid(CheckoutCallbackRequest request);
}
