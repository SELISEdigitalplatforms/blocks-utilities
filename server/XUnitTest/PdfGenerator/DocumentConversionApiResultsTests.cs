using Api.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class DocumentConversionApiResultsTests
    {
        [Fact]
        public void AcceptedBatch_Is202NotOk()
        {
            // The documents are queued, not converted. A 200 would tell a client the work is done.
            var result = DocumentConversionResult<ConvertDocumentsToPdfBatchResponse>.Success(
                new ConvertDocumentsToPdfBatchResponse
                {
                    Results = new List<DocumentConversionAcceptance>
                    {
                        new() { FileId = "doc-1", Accepted = true, Status = DocumentConversionStatus.Queued }
                    },
                    AcceptedCount = 1,
                    RejectedCount = 0
                },
                "trace-1");

            var action = result.ToActionResult("trace-1", StatusCodes.Status202Accepted) as ObjectResult;

            action.Should().NotBeNull();
            action!.StatusCode.Should().Be(StatusCodes.Status202Accepted);

            var body = action.Value.Should().BeOfType<ApiResponse<ConvertDocumentsToPdfBatchResponse>>().Subject;
            body.Success.Should().BeTrue();
            body.Data!.Results.Should().ContainSingle().Which.FileId.Should().Be("doc-1");
            body.Data.AcceptedCount.Should().Be(1);
            body.Error.Should().BeNull();
            body.Meta.CorrelationId.Should().Be("trace-1");
        }

        [Fact]
        public void StatusBatchRead_Is200()
        {
            var result = DocumentConversionResult<DocumentConversionStatusBatchResponse>.Success(
                new DocumentConversionStatusBatchResponse
                {
                    Results = new List<DocumentConversionStatusResult>
                    {
                        new() { FileId = "doc-1", Found = true, Status = DocumentConversionStatus.Succeeded }
                    }
                },
                "trace-1");

            var action = result.ToActionResult("trace-1") as ObjectResult;

            action!.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        [Fact]
        public void StatusBatch_PerFileOutcomesDoNotChangeTheOverallStatusCode()
        {
            // A file that was never submitted, or one whose conversion failed, is still a
            // successful read of the batch as a whole -- the question of what happened to each file
            // was answered. Only a structurally invalid request (an empty fileIds list) is a 4xx.
            var result = DocumentConversionResult<DocumentConversionStatusBatchResponse>.Success(
                new DocumentConversionStatusBatchResponse
                {
                    Results = new List<DocumentConversionStatusResult>
                    {
                        new() { FileId = "doc-1", Found = true, Status = DocumentConversionStatus.Failed, ErrorCode = "conversion_failed" },
                        new() { FileId = "doc-2", Found = false, ErrorCode = "conversion_not_found" }
                    }
                },
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
            var result = DocumentConversionResult<DocumentConversionStatusBatchResponse>.Failure(
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
            var result = DocumentConversionResult<ConvertDocumentsToPdfBatchResponse>.Failure(
                DocumentConversionFailureKind.Validation,
                "file_ids_required",
                "fileIds must contain at least one file ID.",
                "trace-9",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["fileIds"] = ["fileIds must contain at least one file ID."]
                });

            var action = result.ToActionResult("trace-9") as ObjectResult;
            var body = action!.Value.Should().BeOfType<ApiResponse<ConvertDocumentsToPdfBatchResponse>>().Subject;

            body.Success.Should().BeFalse();
            body.Data.Should().BeNull();
            body.Error!.Code.Should().Be("file_ids_required");
            body.Error.Message.Should().Be("fileIds must contain at least one file ID.");
            body.Error.TraceId.Should().Be("trace-9");
            body.Error.Fields.Should().ContainKey("fileIds");
        }

        [Fact]
        public void SuccessStatusCodeOverride_DoesNotLeakIntoFailures()
        {
            // Asking for 202 on success must not turn a validation failure into a 202.
            var result = DocumentConversionResult<ConvertDocumentsToPdfBatchResponse>.Failure(
                DocumentConversionFailureKind.Validation,
                "file_ids_required",
                "fileIds must contain at least one file ID.",
                "trace-1");

            var action = result.ToActionResult("trace-1", StatusCodes.Status202Accepted) as ObjectResult;

            action!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        }
    }
}
