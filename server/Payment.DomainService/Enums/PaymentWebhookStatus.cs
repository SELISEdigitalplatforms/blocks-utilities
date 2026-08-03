namespace Payment.DomainService.Enums;

public enum PaymentWebhookStatus { Pending, Processing, RetryScheduled, Processed, DeadLettered }
