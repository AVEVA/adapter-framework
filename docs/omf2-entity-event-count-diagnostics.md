# OMF 2.0 Entity and Event Count Diagnostics

## Background

Adapter components already publish diagnostics streams next to their OMF health data. These include `StreamCount`, `ErrorRate`, and `IORate`, linked to the component's **DataCollectorHealth** asset. `StreamCount` reports how many streams and types the component has written. Nothing reports the OMF 2.0 entities and events that the component has written.

The implementation defines streams and health links in [AdapterDiagnosticsOmfMessageCreator.cs](../Src/AdapterFramework.Data.Framework.AdapterCommon/Diagnostics/AdapterDiagnosticsOmfMessageCreator.cs), samples and publishes values in [AdapterDiagnosticsService.cs](../Src/AdapterFramework.Data.Framework.AdapterCommon/Diagnostics/AdapterDiagnosticsService.cs), and defines stream names and type IDs in [DiagnosticsConstants.cs](../Src/AdapterFramework.Data.Framework.Common/Constants/DiagnosticsConstants.cs). The counts come from [InstrumentedMessageProcessor.cs](../Src/AdapterFramework.Data.Framework.MessageProcessor/InstrumentedMessageProcessor.cs).

This document describes the `EntityCount` and `EventCount` diagnostics added in [PR #6](https://github.com/AVEVA/adapter-framework/pull/6). They extend the existing component diagnostics rather than introducing a separate diagnostics model. `EntityCount` follows OMF 2.0 terminology, consistent with the `EntityIORate` stream from [PR #21](https://github.com/AVEVA/adapter-framework/pull/21).

## Design

Each adapter component publishes two more diagnostics streams next to its existing `StreamCount`. They follow the same conventions as the other component diagnostics:

| Aspect | Existing `StreamCount` | New `EntityCount` and `EventCount` |
| --- | --- | --- |
| Scope | Per adapter component | Per adapter component |
| Stream ID | `{prefix}{componentId}.StreamCount` | `{prefix}{componentId}.EntityCount`, `.EventCount` |
| Stream name | `StreamCount` (OMF 1.x), `{componentId}.StreamCount` (OMF 2.0) | `{componentId}.EntityCount`, `{componentId}.EventCount` |
| OMF type | `Diagnostics.StreamCount` | `Diagnostics.EntityCount`, `Diagnostics.EventCount` |
| Linked to | Component health asset | Component health asset |
| Sampling | Read every 60th tick of the 1-second timer | Read every 60th tick of the 1-second timer |
| Value | Current stream and type counts | `EntityCount`: current gauge. `EventCount`: cumulative total |
| Publish cadence | Every 60 seconds, only when the value changed | Every 60 seconds, only when the value changed |
| Failure handling | A write failure stops `IORate` and `StreamCount` publishing | A write failure stops only the failing count stream |
| OMF version | All | OMF 2.0 only |

`{prefix}` is the diagnostics stream ID prefix: `{health prefix}{host}.{service name}.`.

### Stream names and link property

With OMF 2.0, every component diagnostics stream (`ErrorRate`, `IORate`, `StreamCount`, `EntityCount`, `EventCount`, `StreamIORate`, `EntityIORate`, `EventIORate`) is named `{componentId}.{stream name}`, so the streams of different components can be told apart in stream search, data views, and stream lists. The link from the component health asset to each stream sets the OMF link `property` to the short stream name, for example `EntityCount`, so attribute names under the DataCollectorHealth asset stay short.

With OMF 1.x, stream names stay short and links do not set `property`, so the OMF messages are unchanged.

Stream IDs and type IDs are the same for all OMF versions. When an OMF 2.0 adapter is upgraded, endpoints that update a stream's name when the stream is re-sent rename the existing diagnostics streams. Consumers that look these streams up by name must use the stream ID or the new name.

### What each stream measures

The **Diagnostics.EntityCount** type has these properties:

- **Timestamp**: string, date-time, marked as `isindex`.
- **EntityCount**: integer (int32). The number of unique entities currently tracked.

The **Diagnostics.EventCount** type has these properties:

- **Timestamp**: string, date-time, marked as `isindex`.
- **EventCount**: integer (int64). The cumulative number of events written.

Both types and streams carry a description. The `EntityCount` description says the count is a gauge of tracked entities that are removed only by an explicit delete and kept across stop and start. The `EventCount` description says the count is cumulative and excludes deletes.

Each stream counts one kind of OMF 2.0 resource that the component writes through `InstrumentedMessageProcessor`:

| Stream | Counts | Message processor calls | Read through |
| --- | --- | --- | --- |
| `EntityCount` | Unique entity IDs currently tracked | OMF 2.0 `WriteStaticValue` overloads (`typeId`, `id`, `name`, ...) | `GetEntityCount()` |
| `EventCount` | Events written since start or the last stop | `WriteEvent` | `GetEventCount()` |

The counting rules are:

- Counts change only after the underlying message processor write returns successfully. This does not confirm delivery to an endpoint.
- `EntityCount` is a gauge. Writing the same entity again does not add to it. IDs are normalized with `ToOmfIdentifier()` and compared case-insensitively.
- A `MessageAction.Delete` for a tracked entity removes it from `EntityCount`. A delete for an unknown entity has no effect.
- `EventCount` is a running total, not a count of distinct events. Repeated writes to the same event count again. Deletes are not counted.
- The legacy `WriteStaticValue(id, extendedPropertyDefinitions, ...)` overload, type writes, stream writes, and relationship writes do not change either count.
- `AdapterDiagnosticsService` reads both counts every 60 ticks of its 1-second timer, on the same timer as `IORate` and `StreamCount`. This periodic collection publishes a value only when it differs from the last value sent.
- The first tick after the service is created publishes both counts immediately, unless `ResendTypesAndStreams()` has already sent the same value. Stop and start keep the 60-tick countdown and the last sent values, so after a restart a count is published at the next reporting boundary, and only if it changed.

### Stop, start, and resend

`AdapterDiagnosticsService.StopAsync()` calls `IInstrumentedMessageProcessor.ClearCounters()`. The two counts behave differently:

- **EventCount** resets to 0, like `StreamCount` and the `IORate` counter.
- **EntityCount** keeps its tracked identities. Entities still exist downstream after a stop, so the count does not fall to 0 when an adapter does not re-send its static values on start. An adapter that re-sends them on start does not double count them.

Data selection changes do not affect `EntityCount`. Selection is keyed by stream ID, and entities are removed only by an explicit delete.

`ResendTypesAndStreams()` re-creates the diagnostics types and streams, then re-sends the last published `EntityCount` and `EventCount`. If nothing has been published yet, it sends the live count and records it as sent, so the next timer tick does not publish the same value again. It skips a count whose stream has already failed, and skips both counts if creating the diagnostics types failed.

### Alternatives considered

- **Locks around the inner write.** An early version held striped locks while calling the wrapped message processor, so tracking stayed exact at the reset boundary. A slow downstream write could then block `StopAsync()`. The final design writes first and then updates tracking without a lock.
- **A separate entity counter.** Keeping a counter next to the identity set lets the two drift permanently when a create and a delete of the same ID race. `GetEntityCount()` returns the size of the identity set instead.
- **Clearing entities on stop.** This would make `EntityCount` depend on every adapter re-sending its entities on start. Retaining identities avoids that, at the cost described in [Constraints](#constraints).
- **Reporting events as a rate.** `IORate` already includes event writes as a rate. A running total lets a consumer derive a rate over any window, while a smoothed rate cannot be turned back into a total.

## Summary

The following shows where the new streams appear in the existing health hierarchy. **New** marks the additions. An asterisk means multiple instances are possible. Names under the DataCollectorHealth asset are the link `property` values; with OMF 2.0 the streams themselves are named `{adapter instance id}.{name}`. The resource IO rate streams are described in [Component-scoped OMF 2 resource IO rate diagnostics](Component-scoped%20OMF%202%20resource%20IO%20rate%20diagnostics.md).

- **Adapters** (static DataCollectors)
  - **{host}.{service name}** (static DataCollectorService) *
    - **{host}.{service name}.{adapter instance id}** (static DataCollectorHealth) *
      - **DeviceStatus** (dynamic DeviceStatus)
      - **NextHealthMessageExpected** (dynamic NextHealthMessageExpected)
      - **ErrorRate** (dynamic Diagnostics.Adapter.ErrorRate)
      - **StreamCount** (dynamic Diagnostics.StreamCount)
      - **IORate** (dynamic Diagnostics.IORate)
      - **EntityCount** (dynamic Diagnostics.EntityCount), OMF 2.0, **New**
      - **EventCount** (dynamic Diagnostics.EventCount), OMF 2.0, **New**
      - **StreamIORate**, **EntityIORate**, **EventIORate** (dynamic Diagnostics.IORate), OMF 2.0

## Constraints

- Diagnostics follow the existing `EnableDiagnostics` general setting. If diagnostics are disabled, these streams are not published.
- The streams are published only when the adapter uses OMF 2.0 or later. OMF 1.x adapters see no change in types, streams, stream names, or links.
- `IInstrumentedMessageProcessor.GetEntityCount()` and `GetEventCount()` are default interface members that return 0. Other implementations still compile and report `0`.
- A failure writing one count stream stops only that stream. The other diagnostics streams keep publishing.
- An adapter that drops entities across a stop and start without sending deletes keeps them in `EntityCount` until the process restarts.

## Example - Adapter Component

For component `Source1` on host `MachineNameX` in service `Adapter`, with no health prefix, the new streams are:

| Stream ID | Stream name | Link `property` | Example value | Meaning |
| --- | --- | --- | --- | --- |
| `MachineNameX.Adapter.Source1.EntityCount` | `Source1.EntityCount` | `EntityCount` | `EntityCount`: 42 | 42 unique entities are currently tracked |
| `MachineNameX.Adapter.Source1.EventCount` | `Source1.EventCount` | `EventCount` | `EventCount`: 1250 | 1250 events written since the component last started |

Both link to the component's health asset, alongside `MachineNameX.Adapter.Source1.StreamCount`. Periodic samples contain a UTC `Timestamp` and the count at the time it was read, and appear only when the count has changed since the last one. `ResendTypesAndStreams()` republishes the last sent count with a new `Timestamp` even when it has not changed, so a resent timestamp is not a fresh measurement.
