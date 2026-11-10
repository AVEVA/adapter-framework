# Branch review: AssetCount to EntityCount (vs `main`)

Remaining findings after the merge of `main` and the doc fixes. None are blocking.

## Breaking change (accepted)

- `IInstrumentedMessageProcessor.GetAssetCount()` becomes `GetEntityCount()`. `DiagnosticsConstants.AssetCountTypeId` and `AssetCountStreamName` become `EntityCountTypeId` and `EntityCountStreamName`. `AssetCountEvent` becomes `EntityCountEvent`. No compatibility alias is kept.
- The published OMF type ID, stream name and property change from `Diagnostics.AssetCount` / `AssetCount` to `Diagnostics.EntityCount` / `EntityCount`.
- Downstream systems that received `Diagnostics.AssetCount` from pre-release `main` builds keep orphaned types and streams.
- This is acceptable only if `AssetCount` never shipped in a release. [docs/omf2-entity-event-count-diagnostics.md](../../docs/omf2-entity-event-count-diagnostics.md) states that it did not; confirm before merging.

## Changes unrelated to the rename

Consider moving these to their own commit, or calling them out in the PR description.

- [SerializationBlock.cs](../../Src/AdapterFramework.Data.Framework.DataFlow/SerializationBlock.cs): both `bytesPerItem` divisions now use `Math.Max(1, ...)`.
  - It only guards against a zero item count; floating-point division previously gave `Infinity` rather than throwing.
  - No test covers the zero-item case.
- [SerializationBlock_Tests.cs](../../Tests/AdapterFramework.Data.Framework.DataFlow.Tests/SerializationBlock_Tests.cs): `compressor.Object` is pulled into a local variable. No behaviour change; probably an analyzer fix.
- [FileQueue_IntegrationTests.cs](../../Tests/AdapterFramework.Data.Framework.PersistentQueue.Tests/FileQueue_IntegrationTests.cs): an explicit `Assert.NotNull` is added before `TestHelper.AssertEqual`. Fine; it makes failures clearer.

## Verification

- `AdapterFramework.Data.Framework.AdapterCommon.Tests`, filtered to `Diagnostics` and `AdapterMainBase`: 115 passed, 0 failed.
- The `MessageProcessor`, `Abstractions`, `DataFlow` and `PersistentQueue` test projects were not rerun after the merge.
