using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed record PaymentReservationResult(
    PaymentDetail? Payment,
    string? LeaseId,
    PaymentOperationResult? TerminalResult)
{
    public bool CanInitiate => Payment != null && LeaseId != null && TerminalResult == null;
}
