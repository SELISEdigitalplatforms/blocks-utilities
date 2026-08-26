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
