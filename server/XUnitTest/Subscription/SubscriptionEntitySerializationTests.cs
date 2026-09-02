using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace XUnitTest.Subscription;

/// <summary>
/// The stored shape survives a round trip, and a document written by a newer build still loads.
/// </summary>
/// <remarks>
/// Rolling deployments mean an old process reads documents a new one wrote. Without
/// <c>BsonIgnoreExtraElements</c> an added field makes the old process throw on every read of
/// that document, which turns a routine deploy into an outage that only appears once traffic
/// reaches the older instances.
/// </remarks>
public sealed class SubscriptionEntitySerializationTests
{
    [Fact]
    public void A_subscription_survives_a_round_trip()
    {
        var original = NewSubscription();

        var restored = BsonSerializer.Deserialize<SubscriptionDetail>(
            original.ToBsonDocument());

        restored.ItemId.Should().Be(original.ItemId);
        restored.Status.Should().Be(SubscriptionStatus.Trialing);
        restored.CurrencyCode.Should().Be("CHF");
        restored.Plan.Entitlements.Should().ContainSingle()
            .Which.Key.Should().Be("pep_screening");
        restored.Price.UnitAmountMinor.Should().Be(8900);
        restored.QuantityItems.Should().ContainSingle()
            .Which.UnitLabel.Should().Be("seat");
        restored.FeeSchedule.AnchorDayOfMonth.Should().Be(31);
        restored.UsageSchedule.TimeZoneId.Should().Be("Europe/Zurich");
        restored.Trial!.Grants.Should().ContainSingle()
            .Which.IncludedQuantity.Should().Be(25);
    }

    [Fact]
    public void A_subscription_written_by_a_newer_build_still_loads()
    {
        var document = NewSubscription().ToBsonDocument();
        document.Add("SomeFieldAddedLater", "value");
        document["Plan"].AsBsonDocument.Add("AlsoAddedLater", 42);

        var restored = () =>
            BsonSerializer.Deserialize<SubscriptionDetail>(document);

        restored.Should().NotThrow();
    }

    [Fact]
    public void A_meter_stored_before_reset_policy_defaults_to_periodic()
    {
        var document = NewSubscription().ToBsonDocument();
        var meter = document["Plan"].AsBsonDocument["Meters"].AsBsonArray[0].AsBsonDocument;
        meter.Remove("ResetPolicy");

        var restored = BsonSerializer.Deserialize<SubscriptionDetail>(document);

        restored.Plan.Meters[0].ResetPolicy.Should().Be(MeterResetPolicy.Periodic);
    }

    [Fact]
    public void A_usage_counter_id_is_composed_from_its_scope()
    {
        SubscriptionUsageCounter
            .CreateId("sub-1", "screening", "M20260801T000000Z")
            .Should()
            .Be("sub-1:screening:M20260801T000000Z");
    }

    [Fact]
    public void A_usage_record_survives_a_round_trip()
    {
        var original = new SubscriptionUsageRecord
        {
            TenantId = "tenant-1",
            OrganizationId = "org-1",
            SubscriptionId = "sub-1",
            MeterKey = "screening",
            PeriodKey = "M20260801T000000Z",
            EntryType = UsageEntryType.Reversal,
            Delta = -1,
            IdempotencyKey = "usage-1",
            OccurredAtUtc = new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc),
            Metadata = new Dictionary<string, string> { ["caseRef"] = "opaque-1" },
            CorrelationId = "corr-1"
        };

        var restored = BsonSerializer.Deserialize<SubscriptionUsageRecord>(
            original.ToBsonDocument());

        restored.Delta.Should().Be(-1);
        restored.EntryType.Should().Be(UsageEntryType.Reversal);
        restored.Metadata.Should().ContainKey("caseRef");
        restored.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public void Aggregates_start_at_version_one()
    {
        new SubscriptionDetail().Version.Should().Be(1);
        new Plan().Version.Should().Be(1);
        new Price().Version.Should().Be(1);
        new BillingAccount().Version.Should().Be(1);
    }

    // ------------------------------------------------------------------ fractional quantities

    /// <summary>
    /// A quantity is stored as <c>Decimal128</c>, not as a double.
    /// </summary>
    /// <remarks>
    /// The representation the whole design rests on. A double would store 0.1 as a value that is
    /// not 0.1, and a reversal would leave a residue in the customer's balance that gets billed.
    /// No serializer is registered for decimal anywhere in this repository, so this pins the
    /// driver's default rather than a choice made in configuration.
    /// </remarks>
    [Fact]
    public void A_fractional_quantity_is_stored_as_decimal128()
    {
        var counter = new SubscriptionUsageCounter
        {
            ItemId = "sub-1:storage:M2026-09",
            Balance = 512.5m,
            LimitSnapshot = 500m
        };

        var document = counter.ToBsonDocument();

        document["Balance"].BsonType.Should().Be(BsonType.Decimal128);
        document["Balance"].AsDecimal.Should().Be(512.5m);
        document["LimitSnapshot"].BsonType.Should().Be(BsonType.Decimal128);
    }

    /// <summary>
    /// A counter written before quantities were fractional still loads.
    /// </summary>
    /// <remarks>
    /// This is what makes the change need no data migration. Every counter and every ledger row in
    /// every tenant database holds <c>NumberLong</c> today; they are read back as decimals, and the
    /// next <c>$inc</c> promotes the field in place. If this ever stopped holding, the change would
    /// require rewriting the append-only usage ledger, which is the authority every past invoice
    /// was computed from.
    /// </remarks>
    [Fact]
    public void A_counter_stored_as_a_whole_number_still_loads()
    {
        var stored = new BsonDocument
        {
            ["_id"] = "sub-1:screening:M2026-09",
            ["TenantId"] = "tenant-1",
            ["Balance"] = new BsonInt64(400),
            ["AppliedRecordCount"] = new BsonInt64(7),
            ["LimitSnapshot"] = new BsonInt64(500)
        };

        var restored = BsonSerializer.Deserialize<SubscriptionUsageCounter>(stored);

        restored.Balance.Should().Be(400m);
        restored.LimitSnapshot.Should().Be(500m);
        restored.AppliedRecordCount.Should().Be(7);
    }

    /// <summary>A ledger entry written before quantities were fractional still loads.</summary>
    [Fact]
    public void A_ledger_entry_stored_as_a_whole_number_still_loads()
    {
        var stored = new BsonDocument
        {
            ["_id"] = "record-1",
            ["TenantId"] = "tenant-1",
            ["Delta"] = new BsonInt64(-3)
        };

        BsonSerializer.Deserialize<SubscriptionUsageRecord>(stored).Delta.Should().Be(-3m);
    }

    /// <summary>
    /// A plan authored before fractions existed has no scale field, reads as zero, and therefore
    /// counts whole units — which is exactly what it did before.
    /// </summary>
    [Fact]
    public void A_meter_stored_without_a_scale_reads_as_whole_units()
    {
        var stored = new BsonDocument
        {
            ["MeterKey"] = "screening",
            ["IncludedQuantity"] = new BsonInt64(500)
        };

        var restored = BsonSerializer.Deserialize<PlanMeter>(stored);

        restored.QuantityScale.Should().Be(0);
        restored.IncludedQuantity.Should().Be(500m);
        restored.CarryForwardCap.Should().BeNull();
    }

    /// <summary>
    /// A quantity survives a round trip to the last place it was authored with.
    /// </summary>
    /// <remarks>
    /// Six places is the finest a meter may declare, so this is the tightest case the storage has
    /// to hold without drift.
    /// </remarks>
    [Fact]
    public void A_six_place_quantity_round_trips_exactly()
    {
        var meter = new PlanMeter
        {
            MeterKey = "compute",
            QuantityScale = 6,
            IncludedQuantity = 1.000001m,
            CarryForwardCap = 0.000001m,
            RateTables =
            [
                new MeterRateTable
                {
                    CurrencyCode = "CHF",
                    Tiers = [new MeterTier { UpToQuantity = 0.500005m, UnitAmountMinor = 3 }]
                }
            ]
        };

        var restored = BsonSerializer.Deserialize<PlanMeter>(meter.ToBsonDocument());

        restored.QuantityScale.Should().Be(6);
        restored.IncludedQuantity.Should().Be(1.000001m);
        restored.CarryForwardCap.Should().Be(0.000001m);
        restored.RateTables[0].Tiers[0].UpToQuantity.Should().Be(0.500005m);
    }

    private static SubscriptionDetail NewSubscription() => new()
    {
        TenantId = "tenant-1",
        OrganizationId = "org-1",
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Trialing,
        CurrencyCode = "CHF",
        OrderId = "sub:sub-1",
        Plan = new PlanSnapshot
        {
            PlanId = "plan-1",
            Code = "professional",
            DisplayName = "Professional",
            FeaturesJson = """{"pep_screening":true}""",
            PlanVersion = 3,
            Entitlements =
            [
                new PlanEntitlement
                {
                    Key = "pep_screening",
                    LimitKind = EntitlementLimitKind.Count,
                    Limit = 500,
                    MeterKey = "screening",
                    UnitLabel = "screening"
                }
            ],
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "screening",
                    UnitLabel = "screening",
                    IncludedQuantity = 500,
                    ThresholdPercents = [80, 100],
                    RateTables =
                    [
                        new MeterRateTable
                        {
                            CurrencyCode = "CHF",
                            Tiers =
                            [
                                new MeterTier { UpToQuantity = 500, UnitAmountMinor = 100 },
                                new MeterTier { UpToQuantity = null, UnitAmountMinor = 85 }
                            ]
                        }
                    ]
                }
            ],
            QuantityItems =
            [
                new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat" }
            ]
        },
        Price = new PriceSnapshot
        {
            PriceId = "price-1",
            CurrencyCode = "CHF",
            UnitAmountMinor = 8900,
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            QuantityItemKey = "seat",
            PriceVersion = 1
        },
        QuantityItems =
        [
            new SubscriptionQuantityItem
            {
                ItemKey = "seat",
                UnitLabel = "seat",
                Quantity = 12,
                UnitAmountMinor = 8900
            }
        ],
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorDayOfMonth = 31,
            TimeZoneId = "Europe/Zurich",
            AnchorInstantUtc = new DateTime(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc)
        },
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorDayOfMonth = 1,
            TimeZoneId = "Europe/Zurich",
            AnchorInstantUtc = new DateTime(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc)
        },
        Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            Grants =
            [
                new TrialMeterGrant { MeterKey = "screening", IncludedQuantity = 25 }
            ]
        }
    };
}
