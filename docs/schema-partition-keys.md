# Schema partition keys

OMF 2.0 schema writes accept a partition key for types, streams, and schema relationships. Write the same schema once per key used for dependent data. A key controls the service's processing route; it doesn't create a separate schema lifecycle.

Keyed schema writes require the configured protocol to be explicitly OMF 2.0. OMF 1.2, OMF 1.3,
and undefined protocol versions are rejected with `NotSupportedException` by both the adapter-facing
processor and the underlying data processor. This restriction does not change unkeyed schema writes
or existing dynamic-data partition-key behavior.

## Schema resend

The instrumentation cache keeps one latest object and action for each schema ID. It also remembers every key used for that ID. A schema resend sends the latest state to all remembered keys. If an unkeyed write occurred, resend also sends that state without a key. Schema relationships follow the same rule for their cached identity.

For example, create a type with `Key1` and `Key2`, then delete that type with `Key2`. A later schema resend sends the delete with both keys. It doesn't restore the earlier create for `Key1`. Don't rely on per-key writes to keep separate resend state.

## Hot failover and disk buffers

Hot failover retains the source OMF version and partition key in memory and on disk. Messages with OMF 1.2 and no key still use the existing V2 failover record. Messages with another OMF version or a key use V3 records. The V3 failover payload contains the type, count, body, process timestamp, key, action, and OMF version. A zero key byte represents no key.

The new reader accepts legacy V2 records with OMF 1.2 and no key. It can't recover metadata that an older writer discarded.

**Rollback warning:** An older failover reader doesn't understand the V3 payload layout. It can include the new metadata bytes in the body and read the wrong action. Drain the hot-failover disk buffer before a downgrade. If a drain isn't possible, archive or clear that buffer before an older release reads it; a clear discards pending messages.