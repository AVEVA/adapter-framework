# Awaitable scopes and failover

**Updated:** 2026-10-22.

Awaitable scopes require an OMF 2.0 pipeline. They remain available to writers without failover,
and with warm or cold failover. **Hot failover does not support awaitable scopes.**

## Supported configurations

| Configuration | Scope behavior |
| --- | --- |
| OMF 2.0, failover not configured | Supported; endpoint acceptance requires an exact HTTP 202 |
| OMF 2.0, warm or cold failover | Supported; endpoint acceptance requires an exact HTTP 202 |
| Hot failover, either role | New scopes throw `NotSupportedException` |
| OMF 1.2 or processor chain without scope support | New scopes throw `NotSupportedException` |

`TryCreateScope` returns `false` only when the active-scope limit is reached. An unsupported
configuration is not a capacity failure. There is no hot-scope opt-in or acceptance-watermark setting.

## Configuration and runtime changes

Each transition into hot mode logs a warning that awaitable scopes are unsupported. Reapplying
the same hot mode does not repeat the warning; leaving and re-entering hot mode does.

Entering hot mode permanently completes every unfinished scope observation as `Discarded`,
with reason `AcceptanceTrackingDisabled` (numeric code **18**). This includes unsealed scopes,
scopes whose bodies have not materialized, and scopes awaiting an endpoint response.
Already completed outcomes remain unchanged. Leaving hot mode does not resurrect discarded scopes;
create new scopes after returning to a supported mode.

Discarding an observation **does not cancel physical data delivery**. Writes already queued may
still be sent. An existing unsealed scope may still carry later writes, but its acceptance result
remains discarded. New scope creation is blocked throughout hot mode, in either role.

Writers must handle the unsupported configuration explicitly. Do not silently retry scoped work
as unscoped work or advance source checkpoints on a discarded result. If best-effort unscoped
delivery is desired, make it an explicit writer policy that does not promise scoped acceptance.
Drain and observe existing scopes before changing modes if their acceptance results are required.

## Legacy hot-failover behavior

Unscoped hot writes retain the existing buffering, promotion, and trimming behavior. Heartbeats
advertise the last dispatched body's process time, not proven endpoint acceptance. The secondary's
existing `FailoverTimeout / 2` trim offset is unchanged. Peer trimming never completes a scope as
accepted or peer-covered.

Hand-off time does not prove that an endpoint accepted a body. This is why enabling scopes with
only a warning would be unsafe for source-checkpoint advancement. This change adds no split-brain
fencing, cross-node checkpoint sharing, deduplication, or exactly-once delivery guarantee.

## Migration and buffer compatibility

The former hot acceptance opt-in has been removed. Remove the obsolete property from existing
configuration before applying it; configuration validation rejects unknown fields.
The boolean `UpdateMode` and `SetFailoverMode` overloads and endpoint/writer acceptance-watermark
APIs have also been removed. Call the single-parameter mode APIs instead.

Existing writer V3/V4/V5 and failover V2/V3/V4 readers are retained. No persisted format has been
renumbered or reinterpreted. Ordinary scoped writer records still retain their identity for local
acceptance tracking. Hot endpoint hand-off strips scope identity and process timestamps, preserving
legacy unscoped writer output. Persisted IDs from a previous process do not reattach to new scopes.

Compatibility outcome and reason enum values remain reserved; retaining them does not enable hot
scope support. `RecordPeerCoverage` is a compatibility no-op and cannot report acceptance.

Older builds may not understand newer buffer records. Drain endpoint and failover buffers before
rolling back to an older build, or deliberately delete them knowing that deletion loses data.