# Content upload and download improvements

## Goals

Improve ShareIt's content lifecycle and transfer mechanism without weakening the existing security
model: the server stores opaque bytes, protected content is decrypted only in the browser, wrong
passphrases do not consume accesses, and logs/metrics do not gain sensitive labels.

The work is split into small PRs so lifecycle correctness lands before protocol and crypto changes.

## Baseline after PR1–PR3

- The composer creates a package and uploads one item per multipart request. Stable package/item IDs
  support retries, and the sealed metadata is cached per item so an encrypted retry keeps the same
  idempotency fingerprint. Files are handed to the browser as-is; only sealed items are read into
  memory, and those are encrypted as one `SIE2` envelope.
- The upload endpoint parses the multipart body with `MultipartReader` and streams the file part to
  S3 while counting it; ASP.NET never buffers the payload. The S3 handler keeps a 1 MiB probe plus one
  reusable 8 MiB part buffer and aborts the multipart upload on failure or cancellation.
- Package details inline small payloads. Larger items are downloaded with the package's short-lived
  access session; nothing is minted per item. The session is returned by the open, stored in
  `sessionStorage`, and reused for retries and parallel downloads.
- The API streams S3 downloads with the stored content length and cancels them when the client goes
  away, without range processing. Unencrypted downloads can go straight to a file the user picks;
  encrypted items and browsers without a writable destination still buffer a `Blob`.
- Owner deletion and cleanup delete PostgreSQL metadata and queue S3 objects in the same transaction;
  the cleanup service drains that queue. Exhausted packages remain until their retention window and
  the last access session have elapsed.

## PR1 — Lifecycle and download reliability (implemented)

1. **Delete blobs as well as metadata.** Route owner deletion and cleanup through `IStorageHandler`.
   Package/content metadata deletion records object keys in a durable blob-deletion queue in the same
   PostgreSQL transaction. The request path only queues; the cleanup service drains queued objects
   idempotently and only then completes each queue entry. Missing objects count as successfully
   deleted; failed objects remain queued for retry.
2. **Keep final-access downloads usable.** Record the last successful package access and retain an
   exhausted package for a configurable period that is at least the access-session lifetime. Expiry
   remains a hard boundary. Cleanup also retries older queued blob deletions.
3. **Do not charge for empty packages.** Load content metadata after unlock validation but before the
   access claim, reject empty packages without incrementing the counter, then claim and produce the
   response.
4. **Harden rollback.** Failed-upload compensation uses an independent bounded timeout rather than a
   request token that may already be canceled.
5. **Test the failure windows.** Cover owner-delete queueing followed by cleanup-driven S3 deletion,
   queued retries, retention before cleanup eligibility, and opening an empty package followed by a
   successful upload/open.

## PR2 — Retryable access sessions (implemented)

Decisions: an access is a **successful package open**, not a completed download. The session is the
only download grant; there are no per-item tokens or leases. Sessions are fixed-lifetime (15 minutes
by default) and never renew automatically. Token/range work that needs a lease model moves to PR4.

1. Return a short-lived, recipient-bound access session after a successful unlock. Store only its
   SHA-256 hash; deliver the token in the response body and keep it in `sessionStorage`.
2. Authorize downloads directly with the session. Nothing is minted at package-open time, so the
   first click on an item is already lazy. Retries and parallel downloads reuse the same grant.
3. Do not renew a session automatically: a re-open after expiry claims another access if one remains.
   The limit counts openings, and the UI labels it as “accesses” rather than “downloads”.
4. Prepare the response first, then atomically claim the access and insert the session in one
   transaction. If preparation fails, nothing is consumed; there is no counter decrement.
5. Defer range requests to PR4. Until ranges are propagated to S3 and a lease model exists, do not
   advertise `enableRangeProcessing`.
6. Test interrupted response streams, session expiry in an idle tab, parallel downloads, cleanup
   during active sessions, and that a re-fetch with a valid session does not consume another access.

## PR3 — Low-risk transfer improvements (implemented)

Decisions: the upload stays a single `multipart/form-data` request, now read with `MultipartReader`
from `HttpContext.Request.Body` instead of a buffered `[FromForm] IFormFile`. The three-call
upload-session protocol (JSON create → raw body → complete) is deferred to PR4, which needs that shape
for presigned parts, quota reservations and abandoned-upload cleanup. The idempotency fingerprint is
kind + declared size + opaque meta, because hashing on the client would mean re-reading an
unencrypted file — exactly the copy PR3 removes. Because sealed metadata uses a fresh random IV on
every `seal()` call, the composer caches its base64 form per item and reuses it when a failed upload is
resumed; otherwise a lost response would turn the retry into a false 409 conflict.

1. Upload unencrypted `File`/`Blob` objects directly instead of calling `arrayBuffer()` and constructing
   another `File`; only sealed items are materialized.
2. Parts are ordered `Id`, `Kind`, `Meta`, `Size`, `File` (last). Metadata is validated before the
   payload is read; duplicate parts or sections after `File` are rejected. The file part is streamed
   through a length-checking stream that stops forwarding as soon as the declared size is exceeded
   (draining only to decide between 400 and 413) and enforces the per-identity limit.
3. Bodies up to 1 MiB are stored with one `PutObject`; larger bodies are sent as explicit S3
   multipart parts with one reusable buffer (8 MiB), and an interrupted upload is aborted.
4. Downloads keep the API proxy, now with the stored content length, an explicit stream lifetime and
   request-abort cancellation, and still no range processing.
5. Unencrypted downloads stream `response.body` into a writable destination picked with
   `showSaveFilePicker` where available; the existing Blob path stays as fallback.
6. Submit re-validates pending items against the current limit (files and text), an already-aborted
   signal never sends an XHR, and reusing an item id with a different kind/size/meta returns 409
   instead of silently succeeding. Duplicate metadata parts and sections after `File` are rejected.

## PR4 — Chunked encryption and resumable large-file transfer

1. Add a versioned `SIE3` envelope with bounded frames. Derive a unique IV per chunk and authenticate
   package/item IDs, chunk index, total size/count, and final-chunk state. Never emit a chunk before
   its GCM tag verifies.
2. Encrypt `File.slice()` chunks rather than whole files. Network chunks and crypto frames are
   independent; readers must buffer a complete frame before decrypting it.
3. For large files, use direct S3 multipart upload with short-lived part URLs and XHR for real
   per-part progress, cancellation, and retry. Keep small items on the simple upload path.
4. Authorize upload creation/completion in the API, reserve quotas atomically, verify completed object
   size, commit metadata only after completion, and abort/clean abandoned multipart uploads.
5. Stream downloads through a frame reader and decrypt transform into
   `FileSystemWritableFileStream` where supported; retain Blob fallback. A service-worker download is
   an optional later enhancement.
6. Consider direct S3 downloads only with session-scoped semantics: presigned URLs are reusable until
   expiry. Keep the streamed API proxy if strict one-time authorization is required.

## PR5 — Product hardening

1. Add draft/ready/deleting package states and finalize only after all item uploads complete. Replace
   “show link anyway” with resumable drafts.
2. Enforce item-count, package-byte, identity storage, and upload-rate quotas with atomic reservations.
3. Add aggregate and per-item progress, download cancellation, and a server-processing phase. Use
   labeled native `<progress>` elements and avoid live-announcing every transfer tick.
4. Measure completed/failed/canceled transfers, actual bytes, retries, decrypt failures, and deletion
   queue depth using aggregate-only metric labels.
5. Benchmark representative file sizes and concurrency on desktop and mobile before raising limits.

## Operational notes

- Owner deletion queues blobs and returns immediately; physical deletion happens on the next cleanup
  pass. With the default five-minute interval, blobs can outlive their metadata by up to that interval
  (plus queue backlog).
- `DownloadSettings:ExhaustedPackageRetentionMinutes` must be at least
  `DownloadSettings:AccessSessionLifetimeMinutes`. Deployments that override only the session lifetime
  must raise the retention window too, otherwise startup validation fails by design.
- Fetch request streams (`duplex: 'half'`) are not the primary browser upload mechanism: support and
  HTTP/proxy constraints are significant, and they do not provide XHR-equivalent upload progress.
- Keep `SIE2` read compatibility when introducing `SIE3` unless an explicit breaking-format decision
  is made for existing links.
