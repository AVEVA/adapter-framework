# Component-scoped OMF 2.0 Resource IO Rate Diagnostics

## Background

Adapter components already publish diagnostics streams next to their OMF health data. These include `StreamCount`, `ErrorRate`, and `IORate`. They are linked to the component's **DataCollectorHealth** asset. These streams are defined in [AdapterDiagnosticsOmfMessageCreator.cs](../Src/AdapterFramework.Data.Framework.AdapterCommon/Diagnostics/AdapterDiagnosticsOmfMessageCreator.cs) and published by [AdapterDiagnosticsService.cs](../Src/AdapterFramework.Data.Framework.AdapterCommon/Diagnostics/AdapterDiagnosticsService.cs). Stream names and type IDs are in [DiagnosticsConstants.cs](../Src/AdapterFramework.Data.Framework.Common/Constants/DiagnosticsConstants.cs). The existing [IO rate](https://docs.aveva.com/bundle/adapter-for-modbus-tcp/page/1230253.html) diagnostic reports how many data values a component writes per second. It does not show what kind of OMF 2.0 resource was written.

[PR #7](https://github.com/AVEVA/adapter-framework/pull/7) added OMF 2.0 resource IO rates (`StreamIORate`, `AssetIORate`, `EventIORate`), but only for each **egress endpoint**. When several source servers feed one adapter, the endpoint rates combine their traffic, so you cannot see how much each server produces.

This document describes the per-component rates added in [PR #21](https://github.com/AVEVA/adapter-framework/pull/21). They use the same model as the existing component diagnostics. Because the usual deployment runs one adapter component per source server, these rates show each source server's output.

## Design

Each adapter component publishes three more diagnostics streams next to its existing `IORate`. They follow the same conventions as the other component diagnostics:

| Aspect | Existing `IORate` | New resource IO rates |
| --- | --- | --- |
| Scope | Per adapter component | Per adapter component |
| Stream ID | `{prefix}{componentId}.IORate` | `{prefix}{componentId}.StreamIORate`, `.AssetIORate`, `.EventIORate` |
| OMF type | `Diagnostics.IORate` | `Diagnostics.IORate` (reused, no new types) |
| Linked to | Component health asset | Component health asset |
| Sampling | Every 1 second | Every 1 second |
| Value | 60-second moving average, per second | 60-second moving average, per second |
| Publish cadence | Every 60 seconds | Every 60 seconds |
| Failure handling | A write failure stops the stream | A write failure stops only the failing stream |
| OMF version | All | OMF 2.0 only |

`{prefix}` is the diagnostics stream ID prefix: `{health prefix}{host}.{service name}.`.

### What each stream measures

The **Diagnostics.IORate** type has these properties:

- **Timestamp**: string, date-time, marked as `isindex`.
- **IORate**: number (float64). The moving average rate per second.

Each stream counts one kind of OMF 2.0 resource that the component writes through [InstrumentedMessageProcessor.cs](../Src/AdapterFramework.Data.Framework.MessageProcessor/InstrumentedMessageProcessor.cs):

| Stream | Counts | Message processor calls |
| --- | --- | --- |
| `StreamIORate` | Stream values written per second | `WriteValue`, `WriteValues`, `WriteDynamicValue`, `WriteDynamicValues` |
| `AssetIORate` | Assets written per second | `WriteStaticValue` |
| `EventIORate` | Events written per second | `WriteEvent` |

The counting rules match the egress endpoint rates:

- Messages with `MessageAction.Delete` are not counted. The existing `IORate` still counts deletes.
- A write that throws is not counted.
- `AdapterDiagnosticsService` reads and resets the counters on every 1-second tick, adds the sample to a 60-second moving average, and publishes the average every 60 seconds, just like `IORate`.

### Component rates and egress rates

Both sets of rates are kept because they measure different points in the pipeline:

- **Component rates** show what each source produced, counted when the component writes.
- **Egress endpoint rates** show what each endpoint actually accepted, counted when the message is delivered.

Comparing them helps you find backlog or rejected messages. For example, if a component writes 500 values/s but the endpoint accepts only 300 values/s, data is building up between them.

The PR also considered counting component rates at delivery time. That approach would require carrying the component ID through serialization, the buffer, and the egress writer. The endpoint rates already show delivered totals, so the extra work was not justified.

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
      - **AssetCount** (dynamic Diagnostics.AssetCount), OMF 2.0
      - **EventCount** (dynamic Diagnostics.EventCount), OMF 2.0
      - **StreamIORate** (dynamic Diagnostics.IORate), OMF 2.0, **New**
      - **AssetIORate** (dynamic Diagnostics.IORate), OMF 2.0, **New**
      - **EventIORate** (dynamic Diagnostics.IORate), OMF 2.0, **New**
    - **{host}.{service name}.{egress instance id}** (static EgressHealth)
      - **DeviceStatus** (dynamic DeviceStatus)
      - **NextHealthMessageExpected** (dynamic NextHealthMessageExpected)
      - **{endpoint id}.StreamIORate**, **.AssetIORate**, **.EventIORate** (dynamic Diagnostics.IORate), OMF 2.0, from PR #7

## Constraints

- Diagnostics follow the existing `EnableDiagnostics` general setting. If diagnostics are disabled, these streams are not published.
- The streams are published only when the adapter uses OMF 2.0. OMF 1.x adapters see no change in types, streams, or links.
- No new OMF types are introduced. The streams reuse `Diagnostics.IORate`.
- `IInstrumentedMessageProcessor.GetAndResetResourceCounters()` is a default interface member that returns zero counts. Other implementations still compile and report `0`.
- A failure writing one resource IO rate stream stops only that stream. The other diagnostics streams keep publishing.

## OMF Example - Adapter Component

The following example shows adapter component `Source1` on host `MachineNameX` in service `Adapter` using OMF 2.0.

**Types**

No new types. The existing `Diagnostics.IORate` type is reused:

```json
{
    "id": "Diagnostics.IORate",
    "classification": "dynamic",
    "type": "object",
    "properties": {
        "Timestamp": { "type": "string", "format": "date-time", "isindex": true },
        "IORate":    { "type": "number", "format": "float64" }
    }
}
```

**Streams**

```json
[
    { "id": "MachineNameX.Adapter.Source1.IORate",       "typeid": "Diagnostics.IORate", "name": "IORate" },
    { "id": "MachineNameX.Adapter.Source1.StreamIORate", "typeid": "Diagnostics.IORate", "name": "StreamIORate" },
    { "id": "MachineNameX.Adapter.Source1.AssetIORate",  "typeid": "Diagnostics.IORate", "name": "AssetIORate" },
    { "id": "MachineNameX.Adapter.Source1.EventIORate",  "typeid": "Diagnostics.IORate", "name": "EventIORate" }
]
```

**Links**

Each new stream is linked to the component health asset in the same way as `IORate`:

```json
{
    "typeid": "__Link",
    "values": [
        {
            "source": { "typeid": "DataCollectorHealth", "index": "MachineNameX.Adapter.Source1" },
            "target": { "containerid": "MachineNameX.Adapter.Source1.StreamIORate" }
        }
    ]
}
```

**Data**

Each stream receives a value every 60 seconds:

```json
[
    { "containerid": "MachineNameX.Adapter.Source1.StreamIORate", "values": [ { "Timestamp": "2026-11-05T12:00:00Z", "IORate": 512.4 } ] },
    { "containerid": "MachineNameX.Adapter.Source1.AssetIORate",  "values": [ { "Timestamp": "2026-11-05T12:00:00Z", "IORate": 0.2 } ] },
    { "containerid": "MachineNameX.Adapter.Source1.EventIORate",  "values": [ { "Timestamp": "2026-11-05T12:00:00Z", "IORate": 3.5 } ] }
]
```
