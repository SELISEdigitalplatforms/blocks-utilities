import { ReactNode } from "react";
import { TRACE_PROVIDERS } from "../../constants/trace.constant";

export type Step = {
  id: string;
  description: ReactNode;
};

export const TraceGuideSteps: Record<TRACE_PROVIDERS, Step[]> = {
  hot: [
    {
      id: "0",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">Hot Tier (Real‑Time)</h4>
          <p>
            Hot traces are stored in MongoDB Time Series collections for low‑latency, ad‑hoc
            querying.
          </p>
          <ul className="mt-2 list-inside list-disc text-sm">
            <li>Optimized for write throughput + real‑time dashboards.</li>
            <li>
              Retention target: first 60 days are fully indexed; total hot window 90–120 days.
            </li>
            <li>
              Automatic roll‑off begins after day 60 (older documents gradually marked for cold
              export).
            </li>
          </ul>
          <p className="mt-2 text-sm text-muted-foreground">
            You can browse & filter these traces directly in the list view. No admin action
            required.
          </p>
        </div>
      ),
    },
    {
      id: "1",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">What Happens After 60 Days?</h4>
          <p className="text-sm">
            A background process batches older hot traces and stages them for cold export (Parquet).
            During staging you still see them as normal until the export job confirms completion.
          </p>
          <ul className="mt-2 list-inside list-disc text-sm">
            <li>Export cadence: every 6 hours (configurable).</li>
            <li>Compression: Snappy (Parquet) in Azure Blob Storage.</li>
            <li>
              Partitioning: /year=YYYY/month=MM/day=DD/hour=HH/service=<code>name</code>
            </li>
          </ul>
        </div>
      ),
    },
    {
      id: "2",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">Query Tips</h4>
          <ul className="list-inside list-disc text-sm">
            <li>Use time range filters to leverage time‑series bucketing efficiently.</li>
            <li>Avoid regex on high‑cardinality fields; index service, traceId, spanId, status.</li>
            <li>Prefer aggregations with $match (time window) then $group.</li>
          </ul>
        </div>
      ),
    },
  ],
  cold: [
    {
      id: "0",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">Cold Tier (Parquet)</h4>
          <p>
            Cold traces (approx. 4–12 months old) live in Azure Blob Storage as partitioned Parquet
            files.
          </p>
          <ul className="mt-2 list-inside list-disc text-sm">
            <li>Optimized for cost & analytical scans, not millisecond lookups.</li>
            <li>
              Accessible via on‑demand query adapters (Spark / DuckDB / Azure Data Lake query).
            </li>
            <li>Schema evolution handled via additive columns; breaking changes versioned.</li>
          </ul>
        </div>
      ),
    },
    {
      id: "1",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">Accessing Cold Data</h4>
          <p className="text-sm">
            You can run batch queries from the UI by choosing a date range outside the hot window.
          </p>
          <ul className="mt-2 list-inside list-disc text-sm">
            <li>Expect higher latency (seconds) for large scans.</li>
            <li>
              Filters push down on partition columns (year, month, day, service) for efficiency.
            </li>
          </ul>
        </div>
      ),
    },
    {
      id: "2",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">Lifecycle</h4>
          <ul className="list-inside list-disc text-sm">
            <li>Files sealed after 24h ingestion window completes.</li>
            <li>Compaction groups small hourly files into daily sets for faster scans.</li>
            <li>After 12 months, segments transition to Archive tier.</li>
          </ul>
        </div>
      ),
    },
  ],
  archive: [
    {
      id: "0",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">Archive Tier (Compliance)</h4>
          <p>
            Archive spans (~1–3 years) are stored as deep‑compressed Parquet (larger row groups,
            higher compression) in a lower‑cost storage tier.
          </p>
          <ul className="mt-2 list-inside list-disc text-sm">
            <li>Not directly queryable from the UI.</li>
            <li>Intended for audits, incident forensics, legal hold.</li>
            {/* <li>Encryption at rest (customer-managed key optional).</li> */}
          </ul>
        </div>
      ),
    },
    {
      id: "1",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">How to Request Archive Data</h4>
          <ol className="mt-2 list-inside list-decimal text-sm">
            <li>Open an admin request ticket specifying: time range, services, trace filters.</li>
            <li>Admin validates purpose (compliance / security / incident).</li>
            <li>
              On approval, data is temporarily rehydrated to a secure cold workspace (time‑boxed).
            </li>
            <li>You receive a signed URL or a temporary query workspace link.</li>
            <li>After expiry, the rehydrated dataset is purged.</li>
          </ol>
          <p className="mt-2 text-xs text-muted-foreground">
            Contact your platform administrator to begin this process.
          </p>
        </div>
      ),
    },
    // {
    //   id: "2",
    //   description: (
    //     <div>
    //       <h4 className="text-lg text-high-emphasis">Retention & Deletion</h4>
    //       <ul className="list-inside list-disc text-sm">
    //         <li>Default retention: 3 years (configurable per environment).</li>
    //         <li>Early deletion requires dual approval (security + compliance).</li>
    //         <li>Legal hold suspends deletion timers for matched partitions.</li>
    //       </ul>
    //     </div>
    //   ),
    // },
  ],
};
