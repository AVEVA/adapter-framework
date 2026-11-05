# Schema partition keys

OMF 2.0 schema writes accept a partition key for types, streams, and schema relationships. Write the same schema once per key used for dependent data. A key controls the service's processing route; it doesn't create a separate schema lifecycle.

Keyed schema writes require the configured protocol to be explicitly OMF 2.0. OMF 1.2, OMF 1.3,
and undefined protocol versions are rejected with `NotSupportedException` by both the adapter-facing
processor and the underlying data processor. This restriction does not change unkeyed schema writes
or existing dynamic-data partition-key behavior.

## Concurrency and object ownership

Group schema writes by key to improve batching, but do not pass the same schema object instance
to concurrent write-method invocations: a second call must not enter while another call using that
instance has not returned. This includes calls from different threads, for different keys, or through
single-item and batch overloads. A sequential per-key loop that waits for each write method to return
before calling the next does not violate this invocation restriction. Pending pipeline work is not
an active write call.

This means the same object reference, not two separate instances with equal schema IDs
or values. Preparation modifies the supplied instance even when the caller does not mutate it:
for example, it prefixes stream IDs and merges adapter metadata into caller-provided dictionaries.
Concurrent calls sharing an instance can both decide its ID needs prefixing and prefix it twice.
They can also modify the same metadata dictionary concurrently.

Shallow copies can still share mutable nested objects or collections, so copying only the outer
schema object does not remove the shared-state race. Independent deep copies of mutable state
avoid that preparation race; sharing immutable strings is fine. Deep copies do not, by themselves,
guarantee ordering between schema writes and dependent data or provide end-to-end pipeline safety.

When the instrumentation cache is used, separate instances with the same schema ID still update
one resend-cache entry. If concurrent writes have different contents or actions, callers must not
rely on a defined winner. The latest-state resend semantics described below apply to these copies
as well as to repeated writes of one instance.

The stream-ID tracking prevents repeated prefixing during sequential per-key rewrites; it does
not make preparation atomic or provide end-to-end thread safety.

Separately, returning from a write does not mean queued objects have been converted to JSON.
The pipeline queues object references rather than immutable snapshots, and the instrumentation cache can retain schema
objects for resend. After submission, callers must not modify those objects, their nested
collections, or submitted batch arrays. Use fresh objects and arrays for later changes.
Calling write methods one at a time does not prevent queued JSON conversion from overlapping
with preparation in a later write call; the invocation restriction alone does not solve that issue.

End-to-end ownership and concurrency support across preparation, queueing, serialization, and
resend requires a separate investigation; this change does not introduce that guarantee.

## Schema resend

The instrumentation cache keeps one latest object and action for each schema ID. It also remembers every key used for that ID. A schema resend sends the latest state to all remembered keys. If an unkeyed write occurred, resend also sends that state without a key. Schema relationships follow the same rule for their cached identity.

For example, create a type with `Key1` and `Key2`, then delete that type with `Key2`. A later schema resend sends the delete with both keys. It doesn't restore the earlier create for `Key1`. Don't rely on per-key writes to keep separate resend state.

## Hot failover and disk buffers

Hot failover retains the source OMF version and partition key in memory and on disk. Messages with OMF 1.2 and no key still use the existing V2 failover record. Messages with another OMF version or a key use V3 records. The V3 failover payload contains the type, count, body, process timestamp, key, action, and OMF version. A zero key byte represents no key.

The new reader accepts legacy V2 records with OMF 1.2 and no key. It can't recover metadata that an older writer discarded.

**Rollback warning:** An older failover reader doesn't understand the V3 payload layout. It can include the new metadata bytes in the body and read the wrong action. Drain the hot-failover disk buffer before a downgrade. If a drain isn't possible, archive or clear that buffer before an older release reads it; a clear discards pending messages.