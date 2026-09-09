using Api.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.PdfIngestion;

namespace XUnitTest.PdfIngestion
{
    public class PdfIngestionApiResultsTests
    {
        [Fact]
        public void AcceptedBatch_Is202NotOk()
        {
            // The files are queued, not inspected yet. A 200 would tell a client the verdict is in.
            var result = PdfIngestionResult<IngestPdfsBatchResponse>.Success(
                new IngestPdfsBatchResponse
                {
                    Results =
                    [
                        new() { FileId = "file-1", Accepted = true, Status = PdfIngestionStatus.Queued }
                    ],
                    AcceptedCount = 1,
                    RejectedCount = 0
                },
                "trace-1");

            var action = result.ToActionResult("trace-1", StatusCodes.Status202Accepted) as ObjectResult;

            action.Should().NotBeNull();
            action!.StatusCode.Should().Be(StatusCodes.Status202Accepted);

            var body = action.Value.Should().BeOfType<ApiResponse<IngestPdfsBatchResponse>>().Subject;
            body.Success.Should().BeTrue();
            body.Data!.Results.Should().ContainSingle().Which.FileId.Should().Be("file-1");
            body.Data.AcceptedCount.Should().Be(1);
            body.Error.Should().BeNull();
            body.Meta.CorrelationId.Should().Be("trace-1");
        }

        [Fact]
        public void StatusBatchRead_Is200()
        {
            var result = PdfIngestionResult<PdfIngestionStatusBatchResponse>.Success(
                new PdfIngestionStatusBatchResponse
                {
                    Results =
                    [
                        new() { FileId = "file-1", Found = true, Status = PdfIngestionStatus.Completed }
                    ]
                },
                "trace-1");

            var action = result.ToActionResult("trace-1") as ObjectResult;

            action!.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        [Fact]
        public void StatusBatch_PerFileOutcomesDoNotChangeTheOverallStatusCode()
        {
            // A file that was never submitted, or whose ingestion failed outright, is still a
            // successful read of the batch as a whole. Only a structurally invalid request (an
            // empty fileIds list) is a 4xx.
            var result = PdfIngestionResult<PdfIngestionStatusBatchResponse>.Success(
                new PdfIngestionStatusBatchResponse
                {
                    Results =
                    [
                        new() { FileId = "file-1", Found = true, Status = PdfIngestionStatus.Failed, ErrorCode = "ingestion_error" },
                        new() { FileId = "file-2", Found = false, ErrorCode = "ingestion_not_found" }
                    ]
                },
                "trace-1");

            var action = result.ToActionResult("trace-1") as ObjectResult;

            action!.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        [Theory]
        [InlineData(PdfIngestionFailureKind.Validation, StatusCodes.Status400BadRequest)]
        [InlineData(PdfIngestionFailureKind.NotFound, StatusCodes.Status404NotFound)]
        [InlineData(PdfIngestionFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
        [InlineData(PdfIngestionFailureKind.Internal, StatusCodes.Status500InternalServerError)]
        public void FailureKind_MapsToTheSameCodesTheRestOfTheApiUses(
            PdfIngestionFailureKind kind,
            int expectedStatusCode)
        {
            var result = PdfIngestionResult<PdfIngestionStatusBatchResponse>.Failure(
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
            var result = PdfIngestionResult<IngestPdfsBatchResponse>.Failure(
                PdfIngestionFailureKind.Validation,
                "file_ids_required",
                "fileIds must contain at least one file ID.",
                "trace-9",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["fileIds"] = ["fileIds must contain at least one file ID."]
                });

            var action = result.ToActionResult("trace-9") as ObjectResult;
            var body = action!.Value.Should().BeOfType<ApiResponse<IngestPdfsBatchResponse>>().Subject;

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
            var result = PdfIngestionResult<IngestPdfsBatchResponse>.Failure(
                PdfIngestionFailureKind.Validation,
                "file_ids_required",
                "fileIds must contain at least one file ID.",
                "trace-1");

            var action = result.ToActionResult("trace-1", StatusCodes.Status202Accepted) as ObjectResult;

            action!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        }
    }
}
