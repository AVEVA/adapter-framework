# Awaitable scopes and failover

**Updated:** 2026-10-23.

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
configuration is not a capacity failure. There is no setting that enables scopes in hot mode.

## Configuration and runtime changes

Each transition into hot mode logs a warning that awaitable scopes are unsupported. Reapplying
the same hot mode does not repeat the warning; leaving and re-entering hot mode does.

Entering hot mode permanently completes every unfinished scope observation as `Discarded`,
with reason `AcceptanceTrackingDisabled` (numeric code **16**). This includes unsealed scopes,
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

## Hot failover without scopes

Unscoped hot writes keep the existing buffering, promotion, and trimming rules. Heartbeats
advertise the last dispatched body's process time, not proven endpoint acceptance. The secondary's
existing `FailoverTimeout / 2` trim offset is unchanged. Peer trimming never completes a scope as
accepted.

Two things change for every hot body, with or without a scope:

- **OMF version and partition key are kept.** Earlier releases dropped both when a body entered
  the failover buffer, so OMF 2.0 bodies were sent labeled as OMF 1.2 and without a partition key.
  Bodies now leave the failover buffer with the same OMF version and partition key they entered with.
- **The failover buffer record format changes.** Earlier releases wrote V2 failover records. This
  release writes V3 records, which add the partition key and OMF version. The failover buffer never
  stores a scope ID. A body from a scope that was open when hot mode started is buffered like any
  unscoped body, and its scope stays discarded.

Hand-off time does not prove that an endpoint accepted a body. This is why enabling scopes with
only a warning would be unsafe for source-checkpoint advancement. This change adds no split-brain
fencing, cross-node checkpoint sharing, deduplication, or exactly-once delivery guarantee.

## Buffer compatibility and rollback

| Buffer | Unscoped body | Scoped body | Earlier releases wrote |
| --- | --- | --- | --- |
| Endpoint (writer) buffer | V3 | V4: V3 plus a 16-byte ID | V3 |
| Failover buffer | V3 | V3, without the ID | V2 |

This release reads every format in the table. Persisted IDs from a previous process do not
reattach to new scopes, so scoped bodies read back after a restart are delivered as ordinary data.

Earlier releases can't read these buffers correctly:

- **V4 endpoint buffer records** have a record marker that earlier releases don't know. They
  treat each one as a corrupt record and skip it, so that body is lost.
- **V3 failover records** are read with the V2 layout. No error is raised. Each body gets two extra
  bytes at the end, and its action is read from the OMF version byte, so an OMF 2.0 body is sent
  as `Update`. The endpoint receives a broken body with the wrong action.

Before rolling back to an earlier release, drain the endpoint buffers and the hot failover
buffers. If you can't drain them, delete them deliberately, knowing that deletion loses data.