using Api.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class DocumentConversionApiResultsTests
    {
        [Fact]
        public void AcceptedConversion_Is202NotOk()
        {
            // The document is queued, not converted. A 200 would tell a client the work is done.
            var result = DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>.Success(
                new ConvertDocumentToPdfAcceptedResponse { FileId = "doc-1" },
                "trace-1");

            var action = result.ToActionResult("trace-1", StatusCodes.Status202Accepted) as ObjectResult;

            action.Should().NotBeNull();
            action!.StatusCode.Should().Be(StatusCodes.Status202Accepted);

            var body = action.Value.Should().BeOfType<ApiResponse<ConvertDocumentToPdfAcceptedResponse>>().Subject;
            body.Success.Should().BeTrue();
            body.Data!.FileId.Should().Be("doc-1");
            body.Error.Should().BeNull();
            body.Meta.CorrelationId.Should().Be("trace-1");
        }

        [Fact]
        public void StatusRead_Is200()
        {
            var result = DocumentConversionResult<DocumentConversionStatusResponse>.Success(
                new DocumentConversionStatusResponse { FileId = "doc-1" },
                "trace-1");

            var action = result.ToActionResult("trace-1") as ObjectResult;

            action!.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        [Theory]
        [InlineData(DocumentConversionFailureKind.Validation, StatusCodes.Status400BadRequest)]
        [InlineData(DocumentConversionFailureKind.NotFound, StatusCodes.Status404NotFound)]
        [InlineData(DocumentConversionFailureKind.Unsupported, StatusCodes.Status422UnprocessableEntity)]
        [InlineData(DocumentConversionFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
        [InlineData(DocumentConversionFailureKind.Internal, StatusCodes.Status500InternalServerError)]
        public void FailureKind_MapsToTheSameCodesTheRestOfTheApiUses(
            DocumentConversionFailureKind kind,
            int expectedStatusCode)
        {
            var result = DocumentConversionResult<DocumentConversionStatusResponse>.Failure(
                kind,
                "some_code",
                "Something went wrong.",
                "trace-1");

            var action = result.ToActionResult("trace-1") as ObjectResult;

            action!.StatusCode.Should().Be(expectedStatusCode);
        }

        [Fact]
        public void Failure_CarriesCodeMessageAndTraceInTheStandardEnvelope()
        {
            var result = DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>.Failure(
                DocumentConversionFailureKind.Validation,
                "input_file_id_required",
                "inputFileId is required.",
                "trace-9",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["inputFileId"] = ["inputFileId is required."]
                });

            var action = result.ToActionResult("trace-9") as ObjectResult;
            var body = action!.Value.Should().BeOfType<ApiResponse<ConvertDocumentToPdfAcceptedResponse>>().Subject;

            body.Success.Should().BeFalse();
            body.Data.Should().BeNull();
            body.Error!.Code.Should().Be("input_file_id_required");
            body.Error.Message.Should().Be("inputFileId is required.");
            body.Error.TraceId.Should().Be("trace-9");
            body.Error.Fields.Should().ContainKey("inputFileId");
        }

        [Fact]
        public void SuccessStatusCodeOverride_DoesNotLeakIntoFailures()
        {
            // Asking for 202 on success must not turn a validation failure into a 202.
            var result = DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>.Failure(
                DocumentConversionFailureKind.Validation,
                "input_file_id_required",
                "inputFileId is required.",
                "trace-1");

            var action = result.ToActionResult("trace-1", StatusCodes.Status202Accepted) as ObjectResult;

            action!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        }
    }
}
