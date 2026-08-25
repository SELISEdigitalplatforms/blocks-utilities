using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;

namespace XUnitTest.Subscription;

public sealed class BillingAlignmentJsonBindingTests
{
    [Theory]
    [InlineData("CalendarMonth", BillingAlignment.CalendarMonth)]
    [InlineData("Anniversary", BillingAlignment.Anniversary)]
    public async Task Price_request_accepts_named_billing_alignment(
        string wireValue,
        BillingAlignment expected)
    {
        var (result, modelState) = await BindAsync($$"""
            {
              "planId": "plan-1",
              "currencyCode": "CHF",
              "unitAmountMinor": 14500,
              "interval": 2,
              "intervalCount": 1,
              "billingAlignment": "{{wireValue}}"
            }
            """);

        result.HasError.Should().BeFalse();
        modelState.IsValid.Should().BeTrue();
        result.Model.Should().BeOfType<CreatePriceRequest>()
            .Which.BillingAlignment.Should().Be(expected);
    }

    [Fact]
    public async Task Omitted_billing_alignment_defaults_to_anniversary()
    {
        var (result, modelState) = await BindAsync("""
            {
              "planId": "plan-1",
              "currencyCode": "CHF",
              "unitAmountMinor": 14500,
              "interval": 2,
              "intervalCount": 1
            }
            """);

        result.HasError.Should().BeFalse();
        modelState.IsValid.Should().BeTrue();
        result.Model.Should().BeOfType<CreatePriceRequest>()
            .Which.BillingAlignment.Should().Be(BillingAlignment.Anniversary);
    }

    [Fact]
    public async Task Unknown_billing_alignment_is_a_field_specific_binding_error()
    {
        var (result, modelState) = await BindAsync("""
            {
              "planId": "plan-1",
              "currencyCode": "CHF",
              "unitAmountMinor": 14500,
              "interval": 2,
              "intervalCount": 1,
              "billingAlignment": "EndOfMonth"
            }
            """);

        result.HasError.Should().BeTrue();
        modelState.IsValid.Should().BeFalse();
        modelState.Keys.Should().Contain("$.billingAlignment");
        modelState.Keys.Should().NotContain("request");
    }

    [Fact]
    public async Task Numeric_billing_alignment_remains_compatible()
    {
        var (result, modelState) = await BindAsync("""
            {
              "planId": "plan-1",
              "currencyCode": "CHF",
              "unitAmountMinor": 14500,
              "interval": 2,
              "intervalCount": 1,
              "billingAlignment": 1
            }
            """);

        result.HasError.Should().BeFalse();
        modelState.IsValid.Should().BeTrue();
        result.Model.Should().BeOfType<CreatePriceRequest>()
            .Which.BillingAlignment.Should().Be(BillingAlignment.CalendarMonth);
    }

    private static async Task<(InputFormatterResult Result, ModelStateDictionary ModelState)> BindAsync(
        string json)
    {
        var options = new JsonOptions();
        var formatter = new SystemTextJsonInputFormatter(
            options,
            NullLogger<SystemTextJsonInputFormatter>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var metadata = new EmptyModelMetadataProvider()
            .GetMetadataForType(typeof(CreatePriceRequest));
        var modelState = new ModelStateDictionary();
        var context = new InputFormatterContext(
            httpContext,
            "request",
            modelState,
            metadata,
            (stream, encoding) => new StreamReader(stream, encoding));

        return (await formatter.ReadAsync(context), modelState);
    }
}
