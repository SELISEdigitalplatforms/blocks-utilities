using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payment.DomainService.Services;

public sealed record ProtectedCheckoutCallbackState(string Token, CheckoutCallbackState State);
