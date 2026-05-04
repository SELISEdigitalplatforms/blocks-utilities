# PDF Generator Service - Implementation Summary

## Overview
This implementation adds a complete PDF Generator service to the l2-net-blocks-utilities project following the new architecture pattern established by the TemplateEngine service.

## What Has Been Created

### 1. **Domain Service Layer** (`src/DomainService/PdfGenerator/`)

#### Request/Response DTOs
- `MergePdfsRequest.cs` - Merge multiple PDFs
- `CreatePdfsFromHtmlRequest.cs` - Convert HTML to PDF
- `ExtractTextFromPdfsRequest.cs` - Extract text from PDFs
- `CreatePdfsFromHtmlUsingTERequest.cs` - HTML to PDF with template engine
- `CreatePdfsFromHtmlUsingTEBulkRequest.cs` - Bulk HTML to PDF with template engine
- `FixPdfsRequest.cs` - Fix corrupted PDFs
- `StampImageToPdfRequest.cs` - Add images to PDF
- `StampTextToPdfRequest.cs` - Add text to PDF
- `StampIntoPdfRequest.cs` - Add both images and text to PDF

All requests implement `IProjectKey` for multi-tenancy support.

#### Events (`Events/PdfGeneratorEvents.cs`)
- `MergePdfsEvent`
- `CreatePdfsFromHtmlEvent`
- `ExtractTextFromPdfsEvent`
- `CreatePdfsFromHtmlUsingTEEvent`
- `CreatePdfsFromHtmlUsingTEBulkEvent`
- `FixPdfsEvent`
- `StampImageToPdfEvent`
- `StampTextToPdfEvent`
- `StampIntoPdfEvent`

#### Entities (`Entities/`)
- `PdfUtilityProfile.cs` - Stores PDF generation configuration (margins, orientation, page numbering, etc.)
- `PdfExtractDump.cs` - Stores extracted text from PDFs

#### Services (`service/`)
- `IPdfGeneratorService` / `PdfGeneratorService` - Main service with all 9 APIs
- `IPdfGeneratorRepository` / `PdfGeneratorRepository` - Data access using IDbContextProvider
- `IPdfGeneratorNotificationService` / `PdfGeneratorNotificationService` - Handles notifications

#### Utilities
- `Constants.cs` - Queue names and message configuration

### 2. **API Layer** (`src/Api/`)

#### Controller
- `PdfGeneratorController.cs` - 9 endpoints:
  1. `MergePdfs` - POST
  2. `CreatePdfsFromHtml` - POST
  3. `ExtractTextFromPdfs` - POST
  4. `CreatePdfsFromHtmlUsingTemplateEngine` - POST
  5. `CreatePdfsFromHtmlUsingTemplateEngineBulk` - POST
  6. `FixPdfs` - POST
  7. `StampImageToPdf` - POST
  8. `StampTextToPdf` - POST
  9. `StampIntoPdf` - POST

All endpoints use `[Authorize]` attribute and `ChangeControllerContext` for tenant context management.

### 3. **Service Registration**
- Updated `Api/ServiceRegistry.cs` with PDF Generator services
- Updated `Worker/ServiceRegistry.cs` with PDF Generator services (consumers commented out)

## MongoDB Collections Accessed

### 1. `PdfUtilityProfiles`
**Purpose**: Store PDF generation configuration profiles
**Used by**: CreatePdfsFromHtml APIs (when Profile parameter is provided)
**Fields**:
- `Id` (string) - Profile GUID
- `MarginLeft`, `MarginRight`, `HeaderSpacing`, `FooterSpacing`
- `Width`, `Height`, `Zoom`
- `PageNumberPosition`, `PageNumberText`, `PageNumberOffset`
- `RemoveHeaderFromPage`, `RemoveFooterFromPage`
- `PageNumberFont`
- `AsyncStream`, `ExecuteUsingWrapper`
- `RemoveHeaderFooterFromCoverPage`
- `Orientation` (Portrait/Landscape)
- `WkCustomArgs` - Custom WkHtmlToPdf arguments

**⚠️ Note**: No passwords or API keys stored in this collection.

### 2. `PdfExtractDumps`
**Purpose**: Store extracted text from PDFs
**Used by**: ExtractTextFromPdfs API
**Fields**:
- `Id` (ObjectId)
- `Text` - Extracted text content
- `MessageCorrelationId` - Request correlation ID
- `PdfId` - Source PDF file ID
- `ItemId` - RecordId from request
- Tenant and audit fields (TenantId, CreatedBy, CreateDate, etc.)

**⚠️ Note**: No passwords or API keys stored in this collection.

## Key Architectural Patterns Followed

### 1. **Request/Response Pattern**
- Uses `Request` suffix instead of `Command` (old architecture)
- All requests implement `IProjectKey` for multi-tenancy
- All responses extend `BaseResponse` from Blocks.Genesis

### 2. **Event-Driven Architecture**
- Service layer sends events to message queues via `IMessageClient.SendToConsumerAsync()`
- Worker consumers process events asynchronously
- Following the same pattern as `TemplateEngineService`

### 3. **Repository Pattern**
- Uses `IDbContextProvider` instead of direct MongoDB connection
- Repository methods are tenant-aware using `BlocksContext.GetContext()?.TenantId`

### 4. **Notification Pattern**
- Follows `ITemplateEngineNotificationService` pattern
- Sends notifications to external notification service
- Uses crypto service for authentication

## What Needs to Be Implemented

### 1. **Worker Consumers** (`src/Worker/Consumers/`)
The actual PDF processing logic needs to be implemented in consumer classes:

- `MergePdfsConsumer.cs` - Implement PDF merging logic
- `CreatePdfsFromHtmlConsumer.cs` - Implement HTML to PDF conversion
- `ExtractTextFromPdfsConsumer.cs` - Implement text extraction
- `CreatePdfsFromHtmlUsingTEConsumer.cs` - Implement TE-based PDF generation
- `CreatePdfsFromHtmlUsingTEBulkConsumer.cs` - Implement bulk TE-based PDF generation
- `FixPdfsConsumer.cs` - Implement PDF repair logic
- `StampImageToPdfConsumer.cs` - Implement image stamping
- `StampTextToPdfConsumer.cs` - Implement text stamping
- `StampIntoPdfConsumer.cs` - Implement combined stamping

Each consumer should follow the pattern from `RenderWithJsonConsumer`:
- Implement `IConsumer<TEvent>`
- Inject required services (StorageHelper, Repository, NotificationService, etc.)
- Process the event
- Save results to storage
- Send notifications on completion

### 2. **PDF Processing Dependencies**
You'll need to add NuGet packages for PDF operations:
- **Aspose.Words** (Engine 1) - For PDF generation/manipulation
- **WkHtmlToPdf wrapper** (Engine 2) - Alternative PDF engine
- PDF text extraction library (e.g., iTextSharp, PdfPig)

### 3. **Storage Helper**
Consider creating a `PdfStorageHelper` similar to the TemplateEngine's `StorageHelper` for:
- Getting PDF files from storage
- Saving generated PDFs
- Managing temporary files during processing

### 4. **Constants Update**
Update `Worker/Constants.cs` to include PDF Generator queue names in the message configuration.

### 5. **Testing**
- Create unit tests for services
- Create integration tests for API endpoints
- Test consumer processing logic

## Migration from Old Architecture

### Key Differences from l2-net-generic-pdf:

| Old Architecture | New Architecture |
|------------------|------------------|
| Command suffix | Request suffix |
| CommandHandler | Consumer (IConsumer) |
| Direct MongoDB connection | IDbContextProvider |
| `_messageClient.PublishAsync()` | `_messageClient.SendToConsumerAsync()` |
| Queue-based (RabbitMQ) | Azure Service Bus queues |
| EntityBase inheritance | Tenant-aware fields |
| ValidationHandler | FluentValidation (if needed) |

### Data Migration:
- `PdfUtilityProfile` collection structure remains compatible
- `PdfExtractDump` collection structure remains compatible
- No data migration required if collection names are kept the same

## Configuration Requirements

Add to `appsettings.json`:
```json
{
  "BlocksAppNotificationReceiver": "pdf-generator",
  "NotificationServiceUrl": "<notification-service-url>",
  "RootTenantId": "<root-tenant-id>",
  "AzureServiceBus": {
    "ConnectionString": "<connection-string>"
  }
}
```

## Next Steps

1. **Implement Consumer Logic**: Start with `ExtractTextFromPdfsConsumer` as it's the simplest
2. **Add PDF Libraries**: Install required NuGet packages for PDF processing
3. **Create Storage Helper**: Build PDF-specific storage utilities
4. **Test End-to-End**: Test one API flow completely before implementing others
5. **Add Validation**: Implement FluentValidation validators if needed
6. **Error Handling**: Add comprehensive error handling and logging
7. **Performance**: Consider implementing retry logic and circuit breakers

## API Documentation

All endpoints are documented with XML comments and will appear in Swagger UI automatically.

Example usage:
```http
POST /PdfGenerator/MergePdfs
Authorization: Bearer <token>
Content-Type: application/json

{
  "projectKey": "tenant-123",
  "outputPdfFileId": "merged-output-123",
  "outputPdfFileName": "merged.pdf",
  "messageCoRelationId": "correlation-123",
  "engine": 1,
  "pdfFilesToBeMerged": [
    { "order": 1, "pdfFileId": "file1-id" },
    { "order": 2, "pdfFileId": "file2-id" }
  ],
  "openInBrowser": false,
  "handleCorruptedPdf": true
}
```

## Summary

✅ **Completed:**
- All request/response DTOs
- All event definitions
- Service interface and implementation
- Repository with IDbContextProvider
- Notification service
- API Controller with 9 endpoints
- Service registration
- Constants and configuration structure
- Entity definitions

⏳ **Remaining:**
- Worker consumers with actual PDF processing logic
- PDF processing library integration
- Storage helper for PDF operations
- Comprehensive testing
- Error handling and validation

The architecture is now ready for implementing the actual PDF processing logic in the worker consumers.
