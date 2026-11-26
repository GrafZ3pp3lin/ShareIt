# Application observability

The API pushes OpenTelemetry metrics, traces and logs to a single OTLP
collector endpoint when one is configured. It does **not** expose a Prometheus
`/metrics` endpoint. The collector is expected to run on the
internal network; no observability endpoint is publicly routed.

The shared collector/Grafana stack lives in the `kroet.server` repository; this
application only exports telemetry.

## Configuration

Metrics, traces and logs use the standard OpenTelemetry environment variables:

| Setting | Environment variable |
|---|---|
| Endpoint | `OTEL_EXPORTER_OTLP_ENDPOINT` |
| Metrics endpoint | `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` |
| Traces endpoint | `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` |
| Logs endpoint | `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT` |
| Protocol | `OTEL_EXPORTER_OTLP_PROTOCOL` |
| Disable the SDK | `OTEL_SDK_DISABLED` |
| Trace sampler | `OTEL_TRACES_SAMPLER` |
| Trace sampler argument | `OTEL_TRACES_SAMPLER_ARG` |

The SDK is only registered when `OTEL_EXPORTER_OTLP_ENDPOINT` or at least one
signal-specific endpoint is configured. Without an endpoint the API does not
fall back to the SDK's default `localhost:4317` exporter, so local runs and
tests do not attempt to connect to a collector. Signal-specific endpoints take
precedence over the shared endpoint.

Supported protocols:

- `grpc` (default), normally port `4317`
- `http/protobuf`, normally port `4318`

The OTLP resource has `service.name=shareit-api`, `service.version` from the
assembly and `deployment.environment.name` from the hosting environment.

At startup the API creates the configured S3 bucket before it starts serving
requests. The S3 health check then uses `HeadBucket`, which requires
`s3:ListBucket` in addition to the bucket creation permission.

Logs are exported through the OpenTelemetry logging provider, which shares the
same resource and exporter settings as metrics and traces. They are only
exported when a shared or logs-specific endpoint is configured; otherwise the
API logs to the console only.

Example container configuration:

```yaml
services:
  api:
    environment:
      OTEL_EXPORTER_OTLP_ENDPOINT: http://otel-collector:4317
      OTEL_EXPORTER_OTLP_PROTOCOL: grpc
      OTEL_TRACES_SAMPLER: parentbased_traceidratio
      OTEL_TRACES_SAMPLER_ARG: "0.25"
    networks:
      - observability
```

The API needs egress to the collector. There is no inbound scrape endpoint to
protect and no Prometheus-specific package in the application.

Use `parentbased_traceidratio` with `OTEL_TRACES_SAMPLER_ARG` to sample traces
in production. Without a sampler configuration the SDK exports every trace,
which is reasonable for development but unnecessarily expensive at scale.

## Metrics

Metrics include both the application's own counters/gauges and the standard
ASP.NET Core and .NET runtime instrumentation:

- request duration, status codes, routing, authentication, rate limiting
- GC, memory, thread pool, exceptions
- the application metrics below

The application instruments are emitted from the `ShareIt` meter.

| Metric | Type | Meaning |
|---|---|---|
| `shareit_packages_created_total` | counter | Packages successfully created |
| `shareit_packages_deleted_total{reason}` | counter | Packages deleted: `cleanup` or `owner` |
| `shareit_package_accesses_total` | counter | Successful package access claims (link opens) |
| `shareit_items_uploaded_total{kind}` | counter | Items uploaded, split into `text` and `file` |
| `shareit_bytes_uploaded_total` | counter | Stored item bytes uploaded |
| `shareit_access_sessions_reused_total` | counter | Package reads that reused a valid access session instead of claiming another access |
| `shareit_bytes_authorized_total` | counter | Logical content bytes whose download was authorized by an access session |
| `shareit_unlock_failures_total` | counter | Failed unlock attempts |
| `shareit_rate_limited_total` | counter | Requests rejected by the package-read, short-code lookup, download or upload rate limiter |
| `shareit_short_code_lookups_total{result}` | counter | Short-code locator lookup results: `found`, `not_found` or `invalid` |
| `shareit_short_code_allocations_total{result}` | counter | Locator allocation results: `created`, `existing`, `exhausted` or `invalid_generator` |
| `shareit_short_code_burns_total` | counter | Code-mode packages invalidated after five failed unlock proofs |
| `shareit_packages{state}` | gauge | Package counts: `total`, `active`, `expired`, `exhausted`, `protected`, `empty` |
| `shareit_items` | gauge | Stored content items |
| `shareit_storage_bytes` | gauge | Logical size of stored content |
| `shareit_blob_deletion_queue_depth` | gauge | Queued blob deletions waiting for the cleanup service |
| `shareit_cleanup_pending` | gauge | Packages currently eligible for cleanup |
| `shareit_cleanup_last_success_timestamp_seconds` | gauge | Last run that completed session cleanup and package metadata deletion, or 0 |
| `shareit_dependency_up{component}` | gauge | `postgres` / `s3` reachability |
| `shareit_build_info{version,environment}` | gauge | Running build information |

`shareit_packages{state}` is a set of independent counts, not a partition:
`expired`, `exhausted`, `protected` and `empty` can overlap.
`active` excludes expired packages and exhausted packages whose retention
window has elapsed; an exhausted package that is still inside that window
counts as active. `shareit_cleanup_pending` is `total - active`: packages
eligible for cleanup at snapshot time, not the blob-deletion queue depth.
The queue depth has its own gauge: `shareit_blob_deletion_queue_depth`.

Alert on sustained `shareit_short_code_lookups_total{result="not_found"}`: it indicates
short-code enumeration rather than normal manual entry. Lookup rate limiting is per remote IP; there
is no process-wide budget.

Important distinctions:

- `shareit_package_accesses_total` counts successful link opens, not file
  downloads. A valid access session lets the same recipient re-fetch and
  download without another claim.
- `shareit_access_sessions_reused_total` counts re-fetches that presented a
  valid session and therefore skipped the claim.
- `shareit_bytes_authorized_total` is recorded after the session is validated
  and the blob has been opened, before the response body is streamed. It
  measures authorization, not completed transfers. A client disconnect or
  storage failure during streaming still records the full logical content size.
  Retries and parallel downloads are counted per authorized response.
- Every item's bytes are fetched through a session-authorized download, so
  every item contributes to the authorized-bytes counter when its download is
  authorized.
- Counters reset when the API process restarts. PromQL `increase()` and `rate()`
  handle resets; the dashboard should use those rather than reading raw
  counter totals.
- Database-derived gauges are refreshed every 60 seconds by
  `MetricsSnapshotService` when SQL storage and OTLP metrics export are both
  configured. The in-memory development store intentionally has no snapshot
  provider. The OTLP export never queries Postgres or S3 directly from a
  metric callback.
- Blob-deletion queue failures are logged and retried by cleanup. The queue
  depth is exported as the `shareit_blob_deletion_queue_depth` gauge (see the
  table above); no per-failure counter is exported.
- The metric labels are aggregate-only. Never add package IDs, user IDs,
  guest IDs, access sessions, URLs or content to metric labels. Traces and logs are
  per-request and may contain opaque package/content IDs, but never access sessions,
  unlock proofs, user/guest IDs, filenames or content.

## Traces

ASP.NET Core requests, outbound HTTP, Npgsql queries and AWS SDK calls are
instrumented automatically. Application spans are emitted from the `ShareIt`
activity source:

| Span | Meaning |
|---|---|
| `package.create` | Create a package (and allocate its locator in code mode) |
| `package.resolve_code` | Resolve a three-character locator to the canonical package id |
| `package.upload` | Upload one content item |
| `package.open` | Open a package, reuse or claim an access session, and prepare items |
| `package.download` | Validate an access session and open the content blob |
| `package.delete` | Delete an owned package |
| `cleanup.run` | Background cleanup of expired sessions and packages |
| `metrics.snapshot` | Periodic database-derived gauge refresh |

Spans carry counts and sizes such as `shareit.package.items`,
`shareit.package.access_claimed`, `shareit.access.session_reused`,
`shareit.content.kind`, `shareit.content.size_bytes`, `shareit.short_code.result`
and `shareit.cleanup.*`. Package and content IDs are not added as separate
attributes; where relevant they are already in the request URL recorded by
ASP.NET Core. Locators are opaque aliases and capabilities: they appear in the
request body but are never metric labels and are not logged as a separate
attribute. The `package.download` span ends after the session is validated
and the blob is opened; streaming the response body is outside that span.

Sampling is configured through the standard SDK environment variables. The
collector stack in `kroet.server` stores traces in VictoriaTraces (14-day
retention by default) and exposes them through its Jaeger-compatible API.

## Logs

Logs use `Microsoft.Extensions.Logging` with the OpenTelemetry logging provider
registered in `ObservabilityExtensions`. The provider batches and retries
exports while the collector is temporarily unavailable, and it shares the
metrics/traces resource (`service.name`, `service.version`,
`deployment.environment.name`).

Message template values are exported as structured attributes
(`ParseStateValues`), the rendered message is the record body
(`IncludeFormattedMessage`), and `ILogger.BeginScope` values are exported as
attributes (`IncludeScopes`). The provider attaches the current `TraceId` and
`SpanId` automatically, so request logs can be followed from VictoriaLogs into
the matching trace in Grafana. Logs that fire outside a request (for example
during startup) simply have no trace context.

The console uses the default `simple` formatter. Log level filtering is
configured under `Logging:LogLevel` in `appsettings.json`;
`Microsoft.AspNetCore` is set to `Warning` so request logs come from the API's
own middleware (`RequestLoggingMiddleware`) as one structured line per request.

Logs include the normal application log fields. Ensure the collector and
Grafana access are restricted to administrators; do not add secrets or tokens
to log messages.

## Local verification

Run a local OTLP collector and point the API at it:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 \
OTEL_EXPORTER_OTLP_PROTOCOL=grpc \
dotnet run --project kroet.ShareIt.Api
```

The endpoint variable enables metrics, traces and logs. Then verify in the
collector that the `shareit-api` service reports all three. There is no
`/metrics` endpoint to call on the API.
