# Content upload and download improvements

## Goals

Improve ShareIt's content lifecycle and transfer mechanism while keeping the system simple, secure,
and easy to use: the server stores opaque bytes, protected content is decrypted only in the browser,
wrong passphrases do not consume accesses, and logs/metrics do not gain sensitive labels.

The app is not live yet, so breaking protocol, schema, and envelope changes are accepted whenever
they remove complexity.

## Baseline after PR1–PR4

- The composer creates a package and uploads one item per raw binary `PUT` (`X-Content-Kind` /
  `X-Content-Meta` headers, `Content-Length` as the declared size). Stable package/item IDs support
  retries, and sealing is deterministic per item (derived per-frame IVs), so an encrypted retry is
  byte-for-byte idempotent. Unencrypted files are handed to the browser as-is; only protected items
  are read into memory, and files are encrypted slice-by-slice into 4 MiB `SIE3` frames — never as
  one whole-file plaintext buffer.
- The upload endpoint validates headers and `Content-Length` before reading `Request.Body`, then
  streams through `DeclaredLengthStream` into the bounded S3 uploader (1 MiB probe plus one reusable
  8 MiB part buffer; interrupted multipart uploads are aborted).
- `GET /package/{id}` performs zero blob reads: it returns package metadata, item metadata (`Id`,
  `Kind`, `Size`, `Meta`) and the `AccessSession`. Every item's bytes — text blocks and file rows
  alike — download through the short-lived session; nothing is minted per item and nothing is inlined.
- Downloads stream from S3 with the stored content length and cancel when the client goes away.
  Encrypted and unencrypted downloads share one path: `response.body` pipes through a frame-decrypting
  `TransformStream` (when protected) into `showSaveFilePicker`'s writable destination, or into a
  `Blob` collected from plaintext chunks as the fallback.
- Owner deletion and cleanup delete PostgreSQL metadata and queue S3 objects in the same transaction;
  the cleanup service drains that queue. Exhausted packages remain until their retention window and
  the last access session have elapsed; every package has a required `expires_at`, so abandoned
  uploads are always swept without a draft/ready state machine.

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
by default) and never renew automatically.

1. Return a short-lived, recipient-bound access session after a successful unlock. Store only its
   SHA-256 hash; deliver the token in the response body and keep it in `sessionStorage`.
2. Authorize downloads directly with the session. Nothing is minted at package-open time, so the
   first click on an item is already lazy. Retries and parallel downloads reuse the same grant.
3. Do not renew a session automatically: a re-open after expiry claims another access if one remains.
   The limit counts openings, and the UI labels it as “accesses” rather than “downloads”.
4. Prepare the response first, then atomically claim the access and insert the session in one
   transaction. If preparation fails, nothing is consumed; there is no counter decrement.
5. Test interrupted response streams, session expiry in an idle tab, parallel downloads, cleanup
   during active sessions, and that a re-fetch with a valid session does not consume another access.

## PR3 — Low-risk transfer improvements (implemented)

1. Upload unencrypted `File`/`Blob` objects directly instead of calling `arrayBuffer()` and constructing
   another `File`; only sealed items are materialized.
2. Parts are ordered `Id`, `Kind`, `Meta`, `Size`, `File` (last). Metadata is validated before the
   payload is read; duplicate parts or sections after `File` are rejected. The file part is streamed
   through a length-checking stream that stops forwarding as soon as the declared size is exceeded
   (draining only to decide between 400 and 413) and enforces the per-identity limit.
3. Bodies up to 1 MiB are stored with one `PutObject`; larger bodies are sent as explicit S3
   multipart parts with one reusable buffer (8 MiB), and an interrupted upload is aborted.
4. Downloads keep the API proxy, now with the stored content length, an explicit stream lifetime and
   request-abort cancellation, and no range processing.
5. Unencrypted downloads stream `response.body` into a writable destination picked with
   `showSaveFilePicker` where available; the existing Blob path stays as fallback.
6. Submit re-validates pending items against the current limit (files and text), an already-aborted
   signal never sends an XHR, and reusing an item id with a different kind/size/meta returns 409
   instead of silently succeeding. Duplicate metadata parts and sections after `File` are rejected.

## PR4 — Framed encryption, protocol simplification, and hardening (implemented)

Decisions: because the API already streams uploads to S3 through a single 8 MiB buffer and streams
downloads from S3 with a 64 KiB buffer, we do **not** introduce direct browser-to-S3 presigned
multipart uploads, multi-step upload sessions, or storage reservation tables. And because the app is
not live yet, we replace `SIE2`, the upload wire format, and inline payloads in-place rather than
carrying legacy compatibility paths.

1. **Replace `SIE2` in-place with a single framed format.**
   - Use fixed 4 MiB plaintext frames for all sealed data (`meta`, text, and files); small items are
     simply a single final frame (`chunkIndex = 0, isFinal = 1`).
   - Because `itemKey` and `metaKey` are already uniquely derived per `(packageId, itemId, role)` via
     HKDF-SHA256, derive each frame's 12-byte AES-GCM IV deterministically from the chunk index and
     authenticate `(packageId, itemId, role, chunkIndex, isFinal)` in the AAD. Never emit a frame
     before its GCM tag verifies.
   - Encrypt files slice-by-slice (`file.slice(...)`) into 4 MiB encrypted chunks rather than
     allocating a whole-file plaintext `ArrayBuffer`. Because sealing is deterministic per item,
     remove `sealedMeta` retry caching from the composer.
2. **Unify streaming downloads for encrypted and unencrypted files.**
   - Pipe `response.body` through a frame-reader and decrypt `TransformStream` when the package is
     protected, or straight through when unprotected.
   - Write the resulting stream to `FileSystemWritableFileStream` (`showSaveFilePicker`) where
     supported, and collect 4 MiB plaintext chunks into a `Blob` as the fallback.
3. **Replace `multipart/form-data` upload with a raw binary `PUT`.**
   - Change `POST /package/{packageId}/upload` to
     `PUT /package/{packageId}/content/{contentId}` with `Content-Type: application/octet-stream`,
     standard `Content-Length`, and headers `X-Content-Kind` (`text` | `file`) and `X-Content-Meta`
     (base64url).
   - Validate metadata and `Content-Length` before reading `Request.Body`, then stream through
     `DeclaredLengthStream` into the bounded S3 uploader.
   - Delete all `MultipartReader` parsing, boundary/disposition helpers, trailing-section rollback,
     `FormData` wrapping, and `UploadContentRequest` / `IFormFile`.
4. **Drop inline `Payload` from `GET /package/{id}`.**
   - `GET /package/{id}` returns only package metadata, item metadata (`Id`, `Kind`, `Size`, `Meta`),
     and `AccessSession` — zero S3 reads during unlock.
   - Fetch all item bytes (both text blocks and file rows) via
     `GET /package/{id}/content/{contentId}/download` with `X-Access-Session`.
   - Remove `Payload`, `InlinePayloadLimitBytes`, `InlinePayloadBudgetBytes`, `InlinePayloadTests`,
     and the inline-vs-download branches across backend and frontend.
5. **Require expiration, prevent partial links, and bound uploads.**
   - Require `TimeToLiveHours` (`1..MaxLifetimeHours`, `expires_at NOT NULL`) on every package to
     match the UI options, ensuring abandoned uploads always expire and are swept by `CleanupService`
     without a `draft/ready/deleting` state machine.
   - Remove `"Show link anyway"` (`showLinkAnyway` / `result.incomplete`) from the composer so links
     are only shown once all items are uploaded; failed uploads offer `Retry`.
   - Add `MaxItemsPerPackage` to `UploadSettings` and a per-IP upload rate-limit policy.
   - Use a labeled native `<progress>` element in the composer and avoid announcing every percentage
     tick in `aria-live`.
6. **Prune backend dead weight and finish observability.**
   - Remove `kroet.Permissions`, `kroet.Permission.AspNetCore`, and `kroet.Permission.Test`; use
     standard ASP.NET Core `.RequireAuthorization()` on `/packages` and remove `X-Guest-ID` from
     authenticated-only endpoints.
   - Remove `IsPrivate` across schema, models, DTOs, and metrics.
   - Remove unused batch/existence methods on `IContentStorageHandler`, `IStorageHandler`, and
     `IBlobStorageHandler` (including the redundant `HEAD`-before-`DELETE` in
     `S3BlobStorageHandler.DeleteAsync`), and collapse `ExternalContent` into `Content`.
   - Add the `blob_deletion_jobs` queue depth gauge to the 60-second SQL metrics snapshot.

## Operational notes

- Owner deletion queues blobs and returns immediately; physical deletion happens on the next cleanup
  pass. With the default five-minute interval, blobs can outlive their metadata by up to that interval
  (plus queue backlog).
- `DownloadSettings:ExhaustedPackageRetentionMinutes` must be at least
  `DownloadSettings:AccessSessionLifetimeMinutes`. Deployments that override only the session lifetime
  must raise the retention window too, otherwise startup validation fails by design.
- Fetch request streams (`duplex: 'half'`) are not the primary browser upload mechanism: support and
  HTTP/proxy constraints are significant, and they do not provide XHR-equivalent upload progress.
