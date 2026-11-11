# Component-scoped OMF 2.0 Resource IO Rate Diagnostics

## Background

Adapter components already publish diagnostics streams next to their OMF health data. These include `StreamCount`, `ErrorRate`, and `IORate`, linked to the component's **DataCollectorHealth** asset. The existing [IO rate](https://docs.aveva.com/bundle/adapter-for-modbus-tcp/page/1230253.html) diagnostic reports the component's overall write rate, without separating stream values, entities, and events.

The implementation defines streams and health links in [AdapterDiagnosticsOmfMessageCreator.cs](../Src/AdapterFramework.Data.Framework.AdapterCommon/Diagnostics/AdapterDiagnosticsOmfMessageCreator.cs), samples and publishes values in [AdapterDiagnosticsService.cs](../Src/AdapterFramework.Data.Framework.AdapterCommon/Diagnostics/AdapterDiagnosticsService.cs), and defines stream names and type IDs in [DiagnosticsConstants.cs](../Src/AdapterFramework.Data.Framework.Common/Constants/DiagnosticsConstants.cs).

[PR #7](https://github.com/AVEVA/adapter-framework/pull/7) added OMF 2.0 resource IO rates, but only for each **egress endpoint**. When several source servers feed one adapter, the endpoint rates combine their traffic, so you cannot see how much each server produces.

This document describes the per-component rates added in [PR #21](https://github.com/AVEVA/adapter-framework/pull/21). They extend the existing component diagnostics rather than introducing a separate diagnostics model. When one component is configured per source server, these rates show each server's contribution; otherwise, they show the combined output of that component's sources.

## Design

Each adapter component publishes three more diagnostics streams next to its existing `IORate`. They follow the same conventions as the other component diagnostics:

| Aspect | Existing `IORate` | New resource IO rates |
| --- | --- | --- |
| Scope | Per adapter component | Per adapter component |
| Stream ID | `{prefix}{componentId}.IORate` | `{prefix}{componentId}.StreamIORate`, `.EntityIORate`, `.EventIORate` |
| Stream name | `IORate` (OMF 1.x), `{componentId}.IORate` (OMF 2.0) | `{componentId}.StreamIORate`, `.EntityIORate`, `.EventIORate` |
| OMF type | `Diagnostics.IORate` | `Diagnostics.IORate` (reused, no new types) |
| Linked to | Component health asset | Component health asset |
| Sampling | Every 1 second | Every 1 second |
| Value | 60-second moving average, per second | 60-second moving average, per second |
| Publish cadence | Every 60 seconds | Every 60 seconds |
| Failure handling | A write failure stops `IORate` and `StreamCount` publishing | A write failure stops only the failing resource rate stream |
| OMF version | All | OMF 2.0 only |

`{prefix}` is the diagnostics stream ID prefix: `{health prefix}{host}.{service name}.`.

With OMF 2.0, the health asset links set the OMF link `property` to the short stream name, so attribute names under the DataCollectorHealth asset stay short. See [Stream names and link property](omf2-entity-event-count-diagnostics.md#stream-names-and-link-property).

### What each stream measures

The **Diagnostics.IORate** type has these properties:

- **Timestamp**: string, date-time, marked as `isindex`.
- **IORate**: number (float64). The moving average rate per second.

Each stream counts one kind of OMF 2.0 resource that the component writes through [InstrumentedMessageProcessor.cs](../Src/AdapterFramework.Data.Framework.MessageProcessor/InstrumentedMessageProcessor.cs):

| Stream | Counts | Message processor calls |
| --- | --- | --- |
| `StreamIORate` | Stream values written per second | `WriteValue`, `WriteValues`, `WriteDynamicValue`, `WriteDynamicValues` |
| `EntityIORate` | Entities written per second | `WriteStaticValue` (all three overloads) |
| `EventIORate` | Events written per second | `WriteEvent` |

The counting rules match the egress endpoint rates:

- Messages with `MessageAction.Delete` are not counted. The existing `IORate` still counts deletes.
- Counts increase only after the underlying message processor write returns successfully. This does not confirm delivery to an endpoint.
- Batch writes count individual values, not API calls. For example, a successful batch of 100 stream values adds 100 to the stream counter.
- `EntityIORate` counts all three `WriteStaticValue` overloads, including the older overload that takes `extendedPropertyDefinitions`. `EntityCount` tracks only the typed overloads. The egress `EntityIORate` agrees with the component rate because that older overload is still sent as a `StaticStreamData` entity.
- These are write rates, not counts of distinct resources. Repeated writes to the same entity or event count again. Type, stream-definition, and relationship writes do not contribute.
- `AdapterDiagnosticsService` reads and resets the counters on every 1-second tick, adds the sample to a 60-second moving average, and publishes the average every 60 seconds, just like `IORate`.

### Component rates and egress rates

Both sets of rates are kept because they measure different points in the pipeline:

- **Component rates** show what each source produced, counted when the component writes.
- **Egress endpoint rates** show what each endpoint actually accepted, counted when the message is delivered.

Compare the same resource kind over comparable time windows and routing. If several components feed an endpoint, compare their combined rate with that endpoint's rate. Do not sum endpoint rates when the same data is sent to multiple endpoints.

For example, if a single component writes 500 stream values/s to one endpoint that accepts only 300 values/s over a sustained period, the difference can indicate backlog or rejected data. The rates are diagnostic signals, not an exact queue-depth or loss measurement.

The PR also considered counting component rates at delivery time. That approach would require carrying the component ID through serialization, the buffer, and the egress writer. The endpoint rates already show delivered totals, so the extra work was not justified.

### Consumers

The adapter publishes rates only per component and per endpoint. It does not roll them up. A consumer that needs a total for a group of components, such as all components that send data for the same destination or all servers on a site, sums the component rates for the same resource kind and time window. A consumer that needs one server's rate reads that component's streams directly.

## Summary

The following shows where the new streams appear in the existing health hierarchy. **New** marks the additions. An asterisk means multiple instances are possible.

- **Adapters** (static DataCollectors)
  - **{host}.{service name}** (static DataCollectorService) *
    - **{host}.{service name}.{adapter instance id}** (static DataCollectorHealth) *
      - **DeviceStatus** (dynamic DeviceStatus)
      - **NextHealthMessageExpected** (dynamic NextHealthMessageExpected)
      - **ErrorRate** (dynamic Diagnostics.Adapter.ErrorRate)
      - **StreamCount** (dynamic Diagnostics.StreamCount)
      - **IORate** (dynamic Diagnostics.IORate)
      - **EntityCount** (dynamic Diagnostics.EntityCount), OMF 2.0
      - **EventCount** (dynamic Diagnostics.EventCount), OMF 2.0
      - **StreamIORate** (dynamic Diagnostics.IORate), OMF 2.0, **New**
      - **EntityIORate** (dynamic Diagnostics.IORate), OMF 2.0, **New**
      - **EventIORate** (dynamic Diagnostics.IORate), OMF 2.0, **New**
    - **{host}.{service name}.{egress instance id}** (static EgressHealth)
      - **DeviceStatus** (dynamic DeviceStatus)
      - **NextHealthMessageExpected** (dynamic NextHealthMessageExpected)
      - **{endpoint id}.IORate** (dynamic Diagnostics.IORate)
      - **{endpoint id}.StreamIORate**, **.EntityIORate**, **.EventIORate** (dynamic Diagnostics.IORate), OMF 2.0

## Constraints

- Diagnostics follow the existing `EnableDiagnostics` general setting. If diagnostics are disabled, these streams are not published.
- The streams are published only when the adapter uses OMF 2.0. OMF 1.x adapters see no change in types, streams, or links.
- No new OMF types are introduced. The streams reuse `Diagnostics.IORate`.
- `IInstrumentedMessageProcessor.GetAndResetResourceCounters()` is a default interface member that returns zero counts. Other implementations still compile and report `0`.
- A failure writing one resource IO rate stream stops only that stream. The other diagnostics streams keep publishing.

## Example - Adapter Component

For component `Source1` on host `MachineNameX` in service `Adapter`, with no health prefix, the new streams are:

| Stream ID | Stream name | Example `IORate` value | Meaning |
| --- | --- | --- | --- |
| `MachineNameX.Adapter.Source1.StreamIORate` | `Source1.StreamIORate` | 512.4 | 512.4 stream values written per second |
| `MachineNameX.Adapter.Source1.EntityIORate` | `Source1.EntityIORate` | 0.2 | 0.2 entity writes per second |
| `MachineNameX.Adapter.Source1.EventIORate` | `Source1.EventIORate` | 3.5 | 3.5 event writes per second |

All three reuse `Diagnostics.IORate` and link to the component's health asset, alongside `MachineNameX.Adapter.Source1.IORate`. Each published sample contains a UTC `Timestamp` and an `IORate` value representing the moving average, not the total number of writes during that minute.
