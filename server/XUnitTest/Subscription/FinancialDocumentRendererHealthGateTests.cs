using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// The gate is the whole of the fix's runtime behavior: whether delivery work touches the renderer
/// at all comes down to what <see cref="FinancialDocumentRendererHealthGate.IsHealthy"/> answers.
/// </summary>
public sealed class FinancialDocumentRendererHealthGateTests
{
    [Fact]
    public void Starts_healthy_with_no_evidence_either_way()
    {
        // A worker that has not probed yet has not seen the renderer fail. Starting unhealthy
        // would refuse document delivery for however long startup takes even on a renderer that
        // works fine — the opposite of what a probe with no result yet should assume.
        new FinancialDocumentRendererHealthGate(
                NullLogger<FinancialDocumentRendererHealthGate>.Instance)
            .IsHealthy.Should().BeTrue();
    }

    [Fact]
    public void A_failure_turns_it_unhealthy()
    {
        var gate = new FinancialDocumentRendererHealthGate(
            NullLogger<FinancialDocumentRendererHealthGate>.Instance);

        gate.RecordFailure(new InvalidOperationException("boom"), "the renderer threw");

        gate.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void A_success_after_a_failure_turns_it_healthy_again()
    {
        var gate = new FinancialDocumentRendererHealthGate(
            NullLogger<FinancialDocumentRendererHealthGate>.Instance);

        gate.RecordFailure(null, "no bytes");
        gate.RecordSuccess();

        gate.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public void Repeated_failures_stay_unhealthy()
    {
        var gate = new FinancialDocumentRendererHealthGate(
            NullLogger<FinancialDocumentRendererHealthGate>.Instance);

        gate.RecordFailure(null, "first");
        gate.RecordFailure(null, "second");

        gate.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void Repeated_successes_stay_healthy()
    {
        var gate = new FinancialDocumentRendererHealthGate(
            NullLogger<FinancialDocumentRendererHealthGate>.Instance);

        gate.RecordSuccess();
        gate.RecordSuccess();

        gate.IsHealthy.Should().BeTrue();
    }
}
