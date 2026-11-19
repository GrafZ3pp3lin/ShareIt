# Crypto v2 — passphrase-gated packages

Protocol reference for the shipped package format: key schedule, framed encryption, transport, unlock
proof, metadata and access accounting. Keep this in sync with the code when changing anything that
affects existing links.

Scope: `frontend/src/shared/crypto`, share page, composer flow, package/item storage, `GET /package/{id}`
and the item download endpoint.

---

## 1. Goal

The package id is a bearer credential, but package metadata and content must not be readable without
proof of the passphrase. `GET /package/{id}` returns names, sizes, types, counters and an access
session only after that proof, and only then consumes an access.

Target:

- No request that reveals anything about the package succeeds without proof of the passphrase.
- The proof costs the caller nothing when it's wrong (no access consumed, no metadata leaked).
- The server never learns the passphrase, the derived key, names, MIME types or any plaintext.
- Links stay usable: no extra fragment that can be stripped.

Non-goals: hiding the package's existence, ciphertext length, timestamps/counters/owner from the
**server**; padding ciphertext; protection against a compromised client.

---

## 2. Where the salt comes from

PBKDF2 needs a salt, and the client must derive the unlock proof before it is allowed to fetch
anything — so the salt has to be computable from the link alone.

| | fragment `#k=<salt>` | `GET /{id}/challenge` | salt derived from the package id |
| --- | --- | --- | --- |
| requests before unlock | 0 | 1 | 0 |
| works if the fragment is stripped | **no** | yes | yes |
| reveals that a package exists | no | yes | no |
| new endpoint | no | yes | no |

**Decision: derive the salt from the package id.** A salt is not a secret (it ships in every sealed
blob anyway); its jobs are "unique per package" and "not precomputable". The id is a random 122-bit
value the caller already has.

```
salt = SHA-256( utf8("shareit/salt:") || packageId (16 bytes, big-endian) )
```

---

## 3. Key schedule

```
ITERATIONS  = 150_000                                   // client constant, not stored
key         = PBKDF2-SHA256(passphrase, salt, ITERATIONS, dkLen = 32)
verifier    = HMAC-SHA256(key, utf8("shareit/unlock:") || packageId)      // 32 bytes, stored
itemKey(i)  = HKDF-SHA256(key, salt = itemId, info = "shareit/item")      // 32 bytes
metaKey(i)  = HKDF-SHA256(key, salt = itemId, info = "shareit/item-meta") // 32 bytes
```

- `key` is `deriveBits`, not `deriveKey`: it is used as an HMAC key and as HKDF input.
- **Iterations are a constant in the client**, not on the package and not in the envelope: the client
  must derive the verifier before it can fetch anything, so there is nothing to read first. Raising it
  later breaks existing packages — accepted pre-release.
- Distinct labels keep the verifier, content keys and metadata keys in separate domains.
- The server stores the 32-byte verifier, compares it in constant time, and never sees the key.

Why a verifier at all: it is the smallest value that proves knowledge of the passphrase without
revealing it, and it is no weaker than the ciphertext, which already allows offline guessing at 150k
PBKDF2 per candidate.

---

## 4. Framed encryption

One container for everything — `meta`, text and files — a sequence of frames:

```
blob = "SIE3" magic + frames…
frame = magic (4) | chunkIndex u32 BE (4) | flags u8 (1: bit 0 = isFinal)
      | ciphertextLength u32 BE (4) | AES-GCM ciphertext + tag (ciphertextLength + 16)
```

- Fixed 4 MiB plaintext frames. Small items (`meta`, text, files ≤ 4 MiB) are a single final frame:
  `chunkIndex = 0, isFinal = 1`.
- Frame overhead: 13 header bytes + 16 GCM tag bytes per frame.
- The 12-byte AES-GCM IV is **derived, not stored**:
  `iv = SHA-256(utf8("shareit/frame-iv:") || chunkIndex u32 BE)[0..12]`. Keys are already unique per
  `(packageId, itemId, role)`, so the same IV across items is safe, and re-sealing one item produces
  byte-identical frames — retries are idempotent by construction.
- Files are encrypted slice-by-slice with `file.slice(...)`: the whole-file plaintext is never held
  as one `ArrayBuffer`. Downloads are decrypted frame by frame through a `TransformStream`; a frame's
  plaintext is emitted only after its GCM tag verifies.
- Detection follows package protection: the client only retains the package key for protected packages.
  With a key, the frame magic is required and a missing/invalid magic is an integrity error; without a
  key, the bytes are raw. `isFramed` remains the magic check inside the frame reader. No JSON parsing,
  no MIME marker, no per-type branch.

---

## 5. Transport rule — everything downloads through the session

Text and files are the same bytes and travel the same way: `GET /package/{id}` returns **only**
metadata (no blob reads at all), and every item's bytes are fetched with the package's access session:

```
GET /package/{packageId}/content/{contentId}/download
X-Access-Session: <opaque token>
```

`Size` is always the stored byte length. Nothing is minted per item: the session returned by the open
authorizes the download when the recipient opens the item, and downloads never consume it.

```ts
const bytes = await download(packageId, item.id, accessSession.token);
const plain = key
  ? await openFrames(itemKey(item.id), bytes, (chunkIndex, isFinal) =>
      frameAad(packageId, item.id, 'item', chunkIndex, isFinal)
    )
  : bytes;
```

- `kind` only decides *rendering* (document block vs file row), never transport.
- Unencrypted items are raw bytes (UTF-8 for text); the magic distinguishes them.
- Downloads stream: `response.body` pipes through the frame decryptor (protected packages) or
  straight through, into a writable destination picked with `showSaveFilePicker` where available, or
  into a `Blob` collected from the plaintext chunks as the fallback.

---

## 6. Item metadata — no manifest

Names and MIME types must not reach the server, but the share page has to list them without
downloading every file. A per-package manifest solves that, and adds a second write that can drift
from the item rows. Instead, **the metadata lives on the item**:

```
meta(bytes) = sealed frame( utf8(JSON { name, mime }) )   when the package is protected
            = utf8(JSON { name, mime })                   when it is not

PUT /{packageId}/content/{contentId}   headers: X-Content-Kind, X-Content-Meta (base64url)
GET  /{id} item                        { id, kind, size, meta: base64url(meta bytes) }
```

- The server stores `Meta` verbatim as opaque **text** (base64url) in the `contents.meta` column and
  never decodes or parses it. It is text, not `bytea`: the wire form is already the storage form, and
  nothing server-side needs the decoded bytes.
- Client: `bytes = base64urlDecode(item.meta)` → `key ? openFrames(metaKey(item.id), bytes, frameAad(..., 'meta')) : JSON.parse(bytes)`; with a key, plaintext metadata is an integrity error.
- Same rule as content, so the frame reader reads both.
- `{ name, mime }` is all that is needed; `kind` and `size` stay in the clear because the server needs
  them for limits and routing, and the ciphertext length leaks `size` anyway.
- Unprotected packages keep plaintext JSON — their content is public by definition, and the server
  storing the name is no longer a leak. It is still base64url-encoded on the wire and in the column.

Why not a manifest:

- no second write and no window where items exist without names;
- no "manifest entry without item" fallback, no size cap, no extra key/AAD/build step;
- the name is written with the item in the same row, so it can never drift;
- `file_name` / `mime_type` columns disappear entirely — `meta` replaces both.

The manifest would only win for *package-level* metadata (title, description, ordering). We dropped
titles, so there is nothing package-level left. If that returns, add a small sealed blob on the
package — it does not justify a manifest now.

Guardrails: the server caps the **decoded** `meta` at ~4 KB per item (base64url validity is checked,
the content never is), the client falls back to `item-<n>` when it is missing or invalid, and nothing
security-relevant ever trusts the server's `kind`.

---

## 7. AAD — binding every frame to its place

Every frame's GCM tag authenticates the AAD, which is never encrypted and never stored:

```
aad(packageId, itemId, role, chunkIndex, isFinal) =
  utf8("shareit/frame:1:" || packageId || ":" || itemId || ":" || role || ":" || chunkIndex || ":" || (isFinal ? 1 : 0))
  role ∈ { "item", "meta" }
```

- Ids are **normalised once** — lowercase, hyphenated UUID — in a single helper shared with key
  derivation. A mismatch otherwise surfaces as a decryption failure.
- Nothing about the AAD goes into the frame: both sides reconstruct it from the URL and the ids.
- Role, chunk index and final flag in the AAD mean an item frame can never be accepted as metadata,
  a reordered or duplicated chunk never verifies, and a non-final frame cannot be presented as a
  complete item.
- This is defence-in-depth: the key schedule (per-package salt, per-item HKDF, distinct `info`)
  already separates packages, items and roles. AAD makes it explicit and keeps it if the schedule
  changes.

---

## 8. Link formats

```
plain     …/s/{packageId}
combined  …/s/{packageId}#s=<passphrase>
```

- No `#k=`: the salt derives from the id, so the plain link is complete.
- The fragment keeps the passphrase out of server logs and `Referer` headers.
- After a successful unlock, strip `#s=` with `history.replaceState` and keep the key in memory only.

---

## 9. Unlock protocol

```
GET /api/v1/content/package/{packageId}
X-Unlock-Proof: <base64url(verifier)>        # required only if the package has a verifier
X-Access-Session: <opaque token>             # optional; reuses an existing access
```

Server, in order:

1. Load the package shell (id, verifier, expiry, counters). No items, no names, no session.
2. Expired → `410`.
3. Package has a verifier and the header is missing or does not match (constant-time) → `401`
   generic body. **No counter change, no metadata.**
4. Load content metadata; an empty package → `410` without claiming.
5. Validate `X-Access-Session` if present. A valid session skips the claim and is returned again.
6. Prepare the item list (metadata only — zero blob reads). Any failure here leaves the counter
   unchanged.
7. Atomically claim one access and insert the session in the same transaction; return the items and the
   session. Concurrent claims serialize on the package row and can never exceed the limit.

Rate limiting: 20 package reads / 15 min per `(packageId, remoteIp)`, 120 downloads / 1 min per
remote IP and 120 uploads / 1 min per remote IP (configurable via `RateLimitSettings`), successful
unlocks included, then `429` + `Retry-After`. Log failures without the proof or session value; never
log the headers.

`401` vs `404`: knowing the id equals holding the link, so "this id exists and is protected" is not
treated as a secret. `401` for protected, `404` for unknown.

Unencrypted packages: no verifier and no proof header. The client cannot discover protection without a
request, so the gate is shown for plain links and an empty submission fetches without a proof — that is
how unprotected packages open without an extra marker in the link.

Downloads are authorized by the session and **never consume it**: retries, parallel downloads and a
mid-stream failure all leave the session usable until it expires. A re-fetch with a valid session does
not consume another access.

---

## 10. Data model

`content_packages`

| Column | Notes |
| --- | --- |
| `Id` | client-supplied; salt and verifier derive from it |
| `UnlockVerifier` | `TEXT`, nullable: base64url HMAC verifier; null for unprotected packages |
| `UserId`, timestamps, counters | owner, access accounting |
| `ExpiresAt` | `TIMESTAMPTZ NOT NULL`: every package expires, so abandoned uploads are swept by cleanup |
| `Title`, `Description` | not stored; the links list names rows by creation timestamp |

`access_sessions`

| Column | Notes |
| --- | --- |
| `package_id`, `client_id`, `is_guest` | recipient binding; the client id is a user id or guest id |
| `token_hash` | SHA-256 hex of the opaque token; the token itself is never stored |
| `created_at`, `expires_at` | fixed lifetime, never renewed automatically |

`contents`

| Column | Notes |
| --- | --- |
| `Meta` | `TEXT` (base64url): framed or plaintext JSON `{ name, mime }`, stored verbatim, never decoded or parsed server-side |
| `ContentType` (`text` / `file`) | display and limits only; never decides transport |
| `Size` | stored byte length |
| `Id`, `Url`, `PackageId` | item id, blob URL, owning package |
| `FileName`, `MimeType` | not stored; replaced by `meta` |

Residual server knowledge: package id, owner, created/expiry, counters, item count, per-item kind,
ciphertext length, metadata blob length.

---

## 11. Client flows

### Upload

The package id must exist before anything is encrypted (salt and verifier derive from it).

1. Generate the package id (`crypto.randomUUID()`) and one id per item.
2. `POST /package` with `Id`, `UnlockVerifier` (null when password is off), `TimeToLiveHours`
   (required: every package expires) and `MaxAccessCount`.
3. For each item: frame the content slice-by-slice into 4 MiB frames (text and files alike) or
   leave it raw, build `meta` (framed or plaintext JSON), then
   `PUT /package/{id}/content/{contentId}` with the bytes as a raw `application/octet-stream` body and
   `X-Content-Kind` / `X-Content-Meta` headers. `Content-Length` is the declared size.
4. Result screen: plain link and combined link (§8). No salt fragment. The link is shown only after
   every item uploaded; failed uploads offer retry with the same ids.

Retries keep the ids stable and sealing is deterministic, so a duplicate upload is byte-for-byte
idempotent; a duplicate create from the same owner returns the existing package.

### Share

1. Parse `location.hash` for `#s=`.
2. With a fragment: derive salt → key → verifier and fetch immediately (**no gate flash**).
   Without one: show the gate and make no request until it is submitted. Because protection is not
   discoverable from the link, an empty submission fetches without a proof (unprotected packages open,
   protected ones answer `401`).
3. `GET /package/{id}` with the proof header when a passphrase was entered, plus the stored session
   when this tab already has one. A valid session avoids claiming another access.
4. Per item: decode `meta` → open frames or parse → `{ name, mime }`.
5. Per item content: always download with the access session; framed bytes are decrypted frame by
   frame (streamed into the save destination for files, buffered for text blocks).
6. Wrong passphrase → `401`, inline error, field kept, no access consumed.
7. After success, store the session in `sessionStorage` for this package and remove the fragment with
   `history.replaceState`; the key stays in memory only.

The gate is the whole page until step 3 succeeds; nothing from the package is rendered before.

### Links list

`GET /packages` (authenticated only). Rows carry the creation timestamp, id, expiry, aggregate size,
item count, counters and protection state — no names, no local storage. The list is keyset-paginated on
`(created_at, id)`; the client loads more pages explicitly.

---

## 12. Security properties

Improved:

- Server never receives the passphrase, the key, names, MIME types or any plaintext.
- A wrong passphrase costs the recipient nothing and leaks nothing.
- A crawler, chat preview or mail scanner cannot burn an access or read names.
- Names and real MIME types no longer sit in the database.
- Every frame is bound to its role, package, item, chunk index and final flag via AAD and the key
  schedule — moving, relabelling, reordering or replaying frames fails to decrypt.
- Access sessions are bearer grants: the token is returned only in the response body, stored in
  `sessionStorage`, and persisted server-side only as a SHA-256 hash. It is bound to the package and
  the caller's user/guest id, expires after 15 minutes, and is never put in a URL.
- Opening a package performs zero blob reads, so unlock cost does not scale with item sizes.

Residual, by design:

- Existence, ciphertext length, item count, timestamps, owner and counters are visible to the server.
- Ciphertext length reveals the plaintext length within 29 bytes per 4 MiB frame.
- Metadata blob length leaks the name/MIME length.
- Offline guessing against the ciphertext (and verifier) is possible; strength depends entirely on the
  passphrase.
- The verifier is a stable bearer proof: anyone who learns it can unlock without the passphrase. It
  must never be logged or cached by intermediaries.
- A stolen session token is usable by anyone who also presents the matching user/guest identity until
  it expires. When DPoP is required, authenticated sessions also need a valid DPoP-bound access token; guest ids
  are client-generated and therefore only a weak binding. The short lifetime and tab-scoped storage
  bound the exposure.

---

## 13. No legacy

Only the current format is read: the v1 per-item-salt envelopes (`{v:1, …}`, `SIE1`) and the `SIE2`
envelope are not supported. One format ships: the `SIE3` frame container.

---

## 14. Test coverage

The automated tests pin these down.

Unit (frontend):

- derive/verify vectors: same passphrase + id → same verifier; different id → different verifier.
- frame round-trips for arbitrary bytes; one helper reads both content and `meta`.
- deterministic sealing: identical plaintext, key and AAD produce identical frames.
- multi-frame round-trips; tampered, truncated, reordered and duplicated frames fail; trailing bytes
  after the final frame fail.
- the streaming decryptor handles byte-at-a-time and arbitrary chunk boundaries and never emits
  plaintext from a frame whose tag does not verify.
- AAD binding: a frame sealed for package A / item A / role A does not open under package B, item B or
  the other role; normalised ids produce identical AAD from differently formatted inputs.
- meta round-trip, framed and plaintext; fallback when missing/corrupt.

Integration (backend):

- `GET` without proof on a protected package → `401`, `CurrentAccessCount` unchanged.
- wrong proof → `401`, counter unchanged, no item fields in the body.
- correct proof → details, counter +1; second call at the limit → `410`.
- failed attempts hit the rate limit → `429`.
- unencrypted package → open, no header.
- `GET /package/{id}` performs zero blob reads (a missing object still answers normally).
- `meta` is returned verbatim (byte-for-byte after base64url decoding) and never parsed.
- every item downloads through the session; downloads retry, run in parallel and do not consume
  another access; an expired session → `401`; a package expired during a valid session → `410`.
- upload: raw binary PUT with `Content-Length` as the declared size; declared/actual mismatch → `400`,
  above the identity limit → `413`, missing `Content-Length` → `400`, wrong content type → `415`;
  idempotent retries reuse the stored item, reusing the id with different kind/size/meta → `409`.
- `TimeToLiveHours` is required (1..max); `MaxItemsPerPackage` bounds a package; the upload rate limit
  returns `429` after the permit limit.
- owner deletion queues blobs; cleanup retains an exhausted package for at least the session lifetime
  and deletes expired packages immediately.
