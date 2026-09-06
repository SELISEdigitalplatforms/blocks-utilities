using System.Text;
using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Naming a caller that authenticates as an application rather than as a person.
/// </summary>
public sealed class PaymentTokenActorTests
{
    private const string ClientId = "5e862913-cc88-4c05-aef9-d7ef390f05e4";

    [Fact]
    public void The_client_id_claim_names_the_application()
    {
        var token = Token(new { client_id = ClientId, sub = "blocks|" + ClientId });

        PaymentTokenActor.ClientId(token).Should().Be("client:" + ClientId);
    }

    [Fact]
    public void The_subject_names_it_when_there_is_no_client_id_claim()
    {
        var token = Token(new { sub = "blocks|" + ClientId });

        PaymentTokenActor.ClientId(token).Should().Be("client:" + ClientId);
    }

    /// <summary>
    /// The namespace in front of the subject is the issuer's bookkeeping, and carrying it into
    /// the audit trail would mean the same application is recorded under two different names
    /// depending on which claim happened to be read.
    /// </summary>
    [Fact]
    public void The_issuer_namespace_is_not_part_of_the_identifier()
    {
        var token = Token(new { sub = "blocks|" + ClientId });

        PaymentTokenActor.ClientId(token).Should().NotContain("blocks|");
    }

    [Fact]
    public void A_subject_without_a_namespace_is_taken_whole()
    {
        var token = Token(new { sub = ClientId });

        PaymentTokenActor.ClientId(token).Should().Be("client:" + ClientId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    [InlineData("only.two")]
    [InlineData("a.b.c.d.e")]
    [InlineData("header.!!!not-base64!!!.signature")]
    public void An_unreadable_token_leaves_the_caller_without_an_actor(string? token)
    {
        // Never throws: authentication has already accepted this request, and a payload this
        // cannot parse is not grounds to fail one.
        PaymentTokenActor.ClientId(token).Should().BeNull();
    }

    [Fact]
    public void An_encrypted_token_is_not_guessed_at()
    {
        var token = string.Join('.', "header", "key", "iv", "ciphertext", "tag");

        PaymentTokenActor.ClientId(token).Should().BeNull();
    }

    [Fact]
    public void A_payload_that_names_no_application_yields_nothing()
    {
        var token = Token(new { tenant_id = "tenant-1", roles = "clouduser" });

        PaymentTokenActor.ClientId(token).Should().BeNull();
    }

    [Fact]
    public void A_scheme_travelling_with_the_token_is_tolerated()
    {
        var token = "Bearer " + Token(new { client_id = ClientId });

        PaymentTokenActor.ClientId(token).Should().Be("client:" + ClientId);
    }

    /// <summary>
    /// The identifier reaches audit records and log lines, and it came off the wire.
    /// </summary>
    [Fact]
    public void An_identifier_carrying_control_characters_is_refused()
    {
        var token = Token(new { client_id = "abc\ndef" });

        PaymentTokenActor.ClientId(token).Should().BeNull();
    }

    [Fact]
    public void A_non_string_claim_is_refused()
    {
        var token = Token(new { client_id = 42 });

        PaymentTokenActor.ClientId(token).Should().BeNull();
    }

    // ---- through the resolver ----

    [Fact]
    public void The_resolver_names_a_client_credentials_caller_by_its_application()
    {
        // Tenant and organization present, no user id and no email: the shape of a
        // client-credentials token, which used to be refused outright.
        Establish(oauthToken: Token(new { client_id = ClientId }));
        try
        {
            var resolution = new PaymentExecutionContextResolver().Resolve("corr-1");

            resolution.IsSuccess.Should().BeTrue();
            resolution.Context!.ActorId.Should().Be("client:" + ClientId);
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    /// <summary>
    /// The same rule that keeps an email out of the user id. A fallback names the caller; it does
    /// not get to pass itself off as the thing it stood in for, or the payments collection ends
    /// up holding application ids in a field named for a person.
    /// </summary>
    [Fact]
    public void The_resolver_does_not_record_a_client_id_as_the_user_id()
    {
        Establish(oauthToken: Token(new { client_id = ClientId }));
        try
        {
            var resolution = new PaymentExecutionContextResolver().Resolve("corr-2");

            resolution.Context!.UserId.Should().BeNull();
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    /// <summary>
    /// The token is the last rung, not the first. A caller the context already names is named by
    /// the context, or the same person would be recorded differently depending on which of the
    /// two happened to be consulted.
    /// </summary>
    [Fact]
    public void A_caller_the_context_names_is_not_renamed_by_its_token()
    {
        Establish(userId: "user-1", oauthToken: Token(new { client_id = ClientId }));
        try
        {
            var resolution = new PaymentExecutionContextResolver().Resolve("corr-3");

            resolution.Context!.ActorId.Should().Be("user-1");
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    [Fact]
    public void The_email_still_outranks_the_token()
    {
        Establish(
            email: "shopper@example.com",
            oauthToken: Token(new { client_id = ClientId }));
        try
        {
            var resolution = new PaymentExecutionContextResolver().Resolve("corr-4");

            resolution.Context!.ActorId.Should().Be("shopper@example.com");
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    /// <summary>
    /// Naming the caller was never the only requirement. A token that carries no tenant is still
    /// refused, because an actor without one has nothing it could be acting on.
    /// </summary>
    [Fact]
    public void A_token_actor_does_not_excuse_a_missing_tenant()
    {
        Establish(
            tenantId: string.Empty,
            oauthToken: Token(new { client_id = ClientId }));
        try
        {
            var resolution = new PaymentExecutionContextResolver().Resolve("corr-5");

            resolution.IsSuccess.Should().BeFalse();
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    /// <summary>
    /// An authenticated context carrying whatever the caller proved, and nothing else.
    /// </summary>
    private static void Establish(
        string tenantId = "tenant-1",
        string? userId = null,
        string? email = null,
        string? oauthToken = null) =>
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId,
            null,
            userId,
            true,
            null,
            "org-1",
            DateTime.UtcNow.AddHours(1),
            email,
            null,
            null,
            null,
            null,
            oauthToken,
            tenantId));

    private static string Token(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return string.Join('.', "header", encoded, "signature");
    }
}
