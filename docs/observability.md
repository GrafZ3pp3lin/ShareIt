# Application observability

The API pushes OpenTelemetry metrics and traces plus Serilog logs to a single
OTLP collector endpoint when one is configured. It does **not** expose a
Prometheus `/metrics` endpoint. The collector is expected to run on the
internal network; no observability endpoint is publicly routed.

The shared collector/Grafana stack lives in the `kroet.server` repository; this
application only exports telemetry.

## Configuration

Metrics and traces use the standard OpenTelemetry environment variables:

| Setting | Environment variable |
|---|---|
| Endpoint | `OTEL_EXPORTER_OTLP_ENDPOINT` |
| Metrics endpoint | `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` |
| Traces endpoint | `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` |
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

Logs are configured separately in `appsettings.Production.json` under
`Serilog:WriteTo` using the `OpenTelemetry` sink. The sink also honors the
standard OTLP endpoint/protocol environment variables, so the endpoint can be
overridden without changing the file.

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
| `shareit_tokens_issued_total` | counter | Download tokens prepared for non-inline items |
| `shareit_tokens_consumed_total` | counter | Download tokens consumed; authorizes a download but does not prove body transfer |
| `shareit_bytes_authorized_total` | counter | Logical content bytes whose download was authorized by a consumed token |
| `shareit_unlock_failures_total` | counter | Failed unlock attempts |
| `shareit_rate_limited_total` | counter | Requests rejected by the package read limiter |
| `shareit_packages{state}` | gauge | Package counts: `total`, `active`, `expired`, `exhausted`, `protected`, `private`, `empty` |
| `shareit_items` | gauge | Stored content items |
| `shareit_storage_bytes` | gauge | Logical size of stored content |
| `shareit_cleanup_pending` | gauge | Packages currently eligible for cleanup |
| `shareit_cleanup_last_success_timestamp_seconds` | gauge | Last run that completed token cleanup and package metadata deletion, or 0 |
| `shareit_dependency_up{component}` | gauge | `postgres` / `s3` reachability |
| `shareit_build_info{version,environment}` | gauge | Running build information |

`shareit_packages{state}` is a set of independent counts, not a partition:
`expired`, `exhausted`, `protected`, `private` and `empty` can overlap.
`active` excludes expired packages and exhausted packages whose retention
window has elapsed; an exhausted package that is still inside that window
counts as active. `shareit_cleanup_pending` is `total - active`: packages
eligible for cleanup at snapshot time, not the blob-deletion queue depth.

Important distinctions:

- `shareit_package_accesses_total` counts link opens, not file downloads.
- A token is issued for each non-inline item every time a package is opened.
  `shareit_tokens_issued_total` therefore counts prepared tokens, not completed
  downloads.
- `shareit_tokens_consumed_total` and `shareit_bytes_authorized_total` are
  recorded after the token is consumed and the blob has been opened, before the
  response body is streamed. They measure authorization, not completed
  transfers. A client disconnect, storage failure during streaming, `HEAD`
  request, or range response still consumes the token and records the full
  logical content size.
- Inline items are delivered in the package response and are not represented in
  the token consumption counters.
- Counters reset when the API process restarts. PromQL `increase()` and `rate()`
  handle resets; the dashboard should use those rather than reading raw
  counter totals.
- Database-derived gauges are refreshed every 60 seconds by
  `MetricsSnapshotService` when SQL storage and OTLP metrics export are both
  configured. The in-memory development store intentionally has no snapshot
  provider. The OTLP export never queries Postgres or S3 directly from a
  metric callback.
- Blob-deletion queue failures are logged and retried by cleanup. No
  blob-queue depth or failure metric is exported yet (planned for PR5).
- The metric labels are aggregate-only. Never add package IDs, user IDs,
  guest IDs, tokens, URLs or content to metric labels. Traces and logs are
  per-request and may contain opaque package/content IDs, but never tokens,
  unlock proofs, user/guest IDs, filenames or content.

## Traces

ASP.NET Core requests, outbound HTTP, Npgsql queries and AWS SDK calls are
instrumented automatically. Application spans are emitted from the `ShareIt`
activity source:

| Span | Meaning |
|---|---|
| `package.create` | Create a package |
| `package.upload` | Upload one content item |
| `package.open` | Open a package, claim access and prepare inline/token items |
| `package.download` | Validate a download token and open the content blob |
| `package.delete` | Delete an owned package |
| `cleanup.run` | Background cleanup of expired tokens and packages |
| `metrics.snapshot` | Periodic database-derived gauge refresh |

Spans carry counts and sizes such as `shareit.package.items`,
`shareit.package.inline_items`, `shareit.package.token_items`,
`shareit.content.kind`, `shareit.content.size_bytes` and `shareit.cleanup.*`.
Package and content IDs are not added as separate attributes; where relevant
they are already in the request URL recorded by ASP.NET Core. The
`package.download` span ends after the token is consumed and the blob is
opened; streaming the response body is outside that span.

Sampling is configured through the standard SDK environment variables. The
collector stack in `kroet.server` stores traces in VictoriaTraces (14-day
retention by default) and exposes them through its Jaeger-compatible API.

## Logs

Serilog continues to write the existing console logs, and an additional
OpenTelemetry sink exports log events to the same OTLP endpoint. The sink batches
and retries exports while the collector is temporarily unavailable.

The sink automatically attaches the current `TraceId` and `SpanId` to exported
log records, so request logs can be followed from VictoriaLogs into the matching
trace in Grafana. Existing logs that fire outside a request (for example during
startup) simply have no trace context.

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

The endpoint variable enables metrics and traces. Then verify in the collector
that the `shareit-api` service reports them. There is no `/metrics` endpoint to
call on the API.

The OpenTelemetry Serilog sink is only configured in
`appsettings.Production.json`. A default Development run therefore keeps logs
on the console. To export logs locally, either add the sink to
`appsettings.Development.json` or run with `ASPNETCORE_ENVIRONMENT=Production`
and provide local storage settings.
