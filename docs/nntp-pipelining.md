# NNTP Pipelining

NzbDav uses **UsenetSharp 3.x** batch BODY requests to send multiple NNTP
commands on one connection without waiting for each response. Responses are read
strictly in order with bounded backpressure.

There are **two separate toggles**:

| Setting | Location | Default | What it controls |
|---------|----------|---------|------------------|
| `usenet.pipelining.enabled` | Settings → Usenet | off | Queue first-segment fetch and provider benchmark batch downloads |
| `usenet.pipelined-body-requests` | Settings → WebDAV | on | WebDAV streaming read-ahead via `DecodedBodiesAsync` batches |

## What the Usenet toggle speeds up

| Path | Without pipelining | With pipelining |
|------|-------------------|-----------------|
| Queue first-segment fetch (0→50%) | one `BODY` per file, concurrent across connections | first segments fetched in depth-sized batches on one connection |
| Provider benchmark | one `BODY` per article | depth-sized `DecodedBodiesAsync` batches |

Health checks and import existence checks always run concurrent `STAT` requests
across the connection pool. They are not affected by this toggle.

## Enabling queue pipelining

Settings → Usenet → **NNTP Pipelining**:

- **Enable NNTP pipelining** — toggles `usenet.pipelining.enabled`.
- **Pipeline depth** — `usenet.pipelining.depth`, requests per BODY batch (1–64,
  default 8). Each provider can override this in its own settings.

For WebDAV playback, use Settings → WebDAV → **Pipelined article downloads**
(`usenet.pipelined-body-requests`).

## How it's built

UsenetSharp exposes batch BODY pipelining through `DecodedBodiesAsync`. The
client chain is:

- `BaseNntpClient` — delegates batch calls to UsenetSharp
- `MultiConnectionNntpClient` — leases one connection per batch
- `MultiProviderNntpClient` — provider selection and byte counting
- `DownloadingNntpClient` / `WrappingNntpClient` — permits and delegation

## Testing

Validate with the Usenet toggle **on** against your providers before relying on
it for queue first-segment fetches. The provider benchmark can recommend a depth
and whether pipelining helps at your connection count.

## Limitations

- **Queue / WebDAV pipelined BODY batches use the same per-segment failover as
  `DecodedBodiesAsync`.** Each depth-sized chunk selects an ordered provider list
  and retries individual misses on the primary (then backups) before yielding
  `Found = false`. Queue first-segment rescue still re-fetches any remaining null
  slots with full per-article failover.
- The per-queue-item article cache bypasses pipelined queue paths when caching is
  enabled (pre-existing; first segments may be re-fetched during RAR header parse).
