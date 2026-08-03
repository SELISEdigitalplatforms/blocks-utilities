using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public enum WebhookStoreResult { Stored, Duplicate }
