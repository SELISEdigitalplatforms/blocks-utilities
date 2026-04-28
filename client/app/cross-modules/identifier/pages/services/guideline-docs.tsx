export const managedServicesGuidelineSteps = [
  {
    id: "0",
    description: (
      <div className="text-sm text-medium-emphasis">
        <h4 className="mb-2 text-lg font-semibold text-high-emphasis">Managed Service Overview</h4>
        <p>
          Our Managed Service lets you register your applications and automatically collect logs and
          traces using our official NuGet package. Once integrated, all telemetry is securely
          ingested, stored, and visualized in real time through the platform dashboard. This guide
          will explain in detail the following step:
        </p>
        <ul className="ml-4 mt-2 list-inside list-disc text-sm">
          <li>Register your service in the cloud to obtain credentials and configuration details.</li>
          <li>Install the NuGet package- SeliseBlocks.LMT.Client.</li>
        </ul>
        <p className="mt-2 text-high-emphasis">
          Send structured logs, metrics, and traces automatically — no extra setup needed. Once
          configured, your telemetry will appear under the corresponding service in the monitoring
          interface within seconds.
        </p>
      </div>
    ),
  },
  {
    id: "1",
    description: (
      <div className="text-sm text-medium-emphasis">
        <h4 className="mb-2 text-lg font-semibold text-high-emphasis">NuGet Integration</h4>
        <p className="text-sm">
          Install the official client library from NuGet to send logs and traces directly from your
          .NET applications. The package automatically batches and transmits telemetry to the
          managed ingestion endpoint.
        </p>
        <p className="mt-2 font-semibold">
          Package:
          <a
            href="https://www.nuget.org/packages/SeliseBlocks.LMT.Client#readme-body-tab"
            target="_blank"
            rel="noopener noreferrer"
            className="ml-1 font-normal text-primary underline"
          >
            SeliseBlocks.LMT.Client
          </a>
        </p>
        <p className="mt-2 font-semibold">Features:</p>
        <ul className="ml-4 mt-2 list-inside list-disc text-sm">
          <li>Automatic retry and buffering on transient network errors.</li>
          <li>Supports logs, traces, and custom context properties.</li>
          <li>Minimal configuration via appsettings.json or environment variables.</li>
        </ul>
      </div>
    ),
  },
  {
    id: "2",
    description: (
      <div className="text-sm text-medium-emphasis">
        <h4 className="mb-2 text-lg font-semibold text-high-emphasis">Viewing Your Data</h4>
        <p className="text-sm">
          Once the client is active, all incoming telemetry becomes searchable and visualizable in
          the Managed Service dashboard.
        </p>
        <ul className="mt-2 list-inside list-disc text-sm">
          <li>Use the list view to browse logs and traces per service.</li>
          <li>Filter by severity, trace ID, service name, or custom tags.</li>
          <li>Leverage time-range filters for performance insights.</li>
        </ul>
      </div>
    ),
  },
];
