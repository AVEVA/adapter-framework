# Scoped Writes (Awaitable Scopes)

A scoped write groups a set of OMF 2.0 writes into one unit of work and lets you wait until every required endpoint has accepted it with `202 Accepted`.

All types are in the `AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable` namespace.

## Key types

| Type | Purpose |
| --- | --- |
| `IAwaitableMessageProcessor` / `IAwaitableAdapterMessageProcessor` | Creates scopes with `TryCreateAwaitableScope`. |
| `IAwaitableMessageScope` / `IAwaitableAdapterMessageScope` | A scope. It has the same `Write*` methods as the processor that created it, plus `Seal` and `WaitForAcceptanceAsync`. |
| `OmfAwaitableScopeOptions` | Sets `DefaultWaitTimeout` (default 15 minutes) and `FlushOnSeal`. |
| `OmfAcceptanceResult` | Contains `Outcome`, `Deliveries`, `Reason`, and `ObservedAtUtc`. |
| `OmfAcceptanceOutcome` | `Accepted`, `AcceptedWithDrops`, `Rejected`, `Discarded`, `Filtered`, or `TimedOut`. |
| `OmfDeliveryResult` | The state of one body sent to one endpoint, including `EndpointId`, `State`, `LastStatusCode`, `Reason`, and `Receipt`. |
| `OmfOutcomeReason` | Contains a machine-readable `Code` (`OmfReasonCode`) and a `Message`. |

## Basic example

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

public async Task<bool> WriteBatchAsync(
	IAwaitableAdapterMessageProcessor processor,
	IDataSelectionConfiguration selection,
	IReadOnlyList<MyValue> values,
	CancellationToken cancellationToken)
{
	var options = new OmfAwaitableScopeOptions
	{
		DefaultWaitTimeout = TimeSpan.FromMinutes(2),
		FlushOnSeal = true, // flush grouping blocks on seal for lower latency
	};

	if (!processor.TryCreateAwaitableScope(options, out var scope))
	{
		// The active-scope limit was reached. Retry later or write without a scope.
		return false;
	}

	// Always dispose the scope. It holds an active-scope slot until disposal.
	await using (scope)
	{
		scope.WriteDynamicValues(selection, values);

		// WaitForAcceptanceAsync seals the scope automatically. Later writes throw InvalidOperationException.
		var result = await scope.WaitForAcceptanceAsync(cancellationToken: cancellationToken);

		return result.Outcome switch
		{
			OmfAcceptanceOutcome.Accepted => true,
			OmfAcceptanceOutcome.AcceptedWithDrops => true, // oversized items were dropped; remaining deliveries were accepted
			OmfAcceptanceOutcome.Filtered => true, // filters removed every value, or nothing was written
			_ => false,
		};
	}
}
```

## Handle the result

```csharp
var result = await scope.WaitForAcceptanceAsync(TimeSpan.FromSeconds(30));

switch (result.Outcome)
{
	case OmfAcceptanceOutcome.Accepted:
		break;

	case OmfAcceptanceOutcome.AcceptedWithDrops:
		// The scope succeeded, but oversized items were not delivered. Do not retry the entire batch.
		logger.LogWarning("Scope accepted with oversized drops: {Code} {Message}", result.Reason.Code, result.Reason.Message);
		break;

	case OmfAcceptanceOutcome.TimedOut:
		// Delivery continues. You can wait again on the same scope.
		logger.LogInformation("Still pending: {Code} {Message}", result.Reason?.Code, result.Reason?.Message);
		result = await scope.WaitForAcceptanceAsync(TimeSpan.FromMinutes(5));
		break;

	case OmfAcceptanceOutcome.Rejected:
	case OmfAcceptanceOutcome.Discarded:
		logger.LogWarning("Scope failed: {Code} {Message}", result.Reason.Code, result.Reason.Message);
		foreach (var delivery in result.Deliveries)
		{
			logger.LogWarning(
				"Endpoint {Endpoint} ({Uri}): {State}, last status {Status}, reason {Reason}",
				delivery.EndpointId,
				delivery.TargetUri,
				delivery.State,
				delivery.LastStatusCode,
				delivery.Reason?.Code);
		}

		break;
}
```

> Later releases may add `OmfReasonCode` values. Always handle unknown codes.

## Seal explicitly and wait later

Call `Seal` when the producer finishes writing, and wait for acceptance somewhere else.

```csharp
if (!processor.TryCreateAwaitableScope(new OmfAwaitableScopeOptions(), out var scope))
{
    throw new InvalidOperationException("Active-scope limit reached.");
}

await using var _ = scope;

scope.WriteTypes(types);
scope.WriteStreams(streams);
scope.WriteDynamicValues(selection, values);

scope.Seal(); // waits for in-flight writes on other threads, and blocks while the pipeline is full

// ... other work ...

var result = await scope.WaitForAcceptanceAsync();
```

## Rules and behavior

- **Supported configurations:** Scopes require OMF 2.0. `TryCreateAwaitableScope` throws `NotSupportedException` for OMF 1.2, for a processor chain that doesn't support scopes, or when hot failover is configured.
- **Partition keys:** Keyed schema writes (types, streams, and schema relationships) and keyed dynamic values are tracked like unkeyed writes.
- **Range deletes:** A scoped range delete counts as one item and follows its body's endpoint decision. `Accepted` means the endpoint returned `202 Accepted` for the delete, not that deletion has finished, so it isn't a safe signal to write replacement values unless the destination processes requests in order. Retries and failover can still replay a delete after later writes. A delete that fails validation throws without being counted; one that the pipeline refuses throws and discards the scope with `PostRejected`.
- **Active-scope limit:** `TryCreateAwaitableScope` returns `false` when the limit is reached. A scope uses a slot until you dispose it, even after its outcome is final. Use `await using`.
- **Waiting never cancels delivery:** A timeout, a cancelled token, or disposal ends only the wait. Data continues to its endpoints.
- **Waits can be repeated:** `TimedOut` isn't final. Call `WaitForAcceptanceAsync` again.
- **Backpressure:** Writes, `Seal`, and the sealing step of `WaitForAcceptanceAsync` can block while the pipeline is full. The timeout and token apply only after sealing finishes.
- **Exceptions from `WaitForAcceptanceAsync`:** It throws `OperationCanceledException` when the token, scope disposal, or framework shutdown cancels the wait. It throws `ObjectDisposedException` when the scope is already disposed.
- **Accepted means exact 202:** Every endpoint writer that's active when a body is dispatched must return `202 Accepted`. Other 2xx statuses report `NonAcceptedSuccess`.
- **Oversized items are tolerated:** An item that still exceeds the configured body limit after splitting is dropped with `ItemTooLarge`. The scope completes as `AcceptedWithDrops` only after sealing, materialization, and exact-202 acceptance of every remaining required delivery. `Reason` contains the first oversized-drop reason. If every admitted item is oversized, the outcome is still `AcceptedWithDrops`, with no deliveries. Other item drops, body drops, and delivery failures retain their original `Discarded` or `Rejected` outcomes.
- **Scopes are in-process:** Scopes don't survive a restart or failover. If hot failover becomes active, new scopes are rejected and unfinished scopes complete as `Discarded` with `AcceptanceTrackingDisabled`. Data delivery still continues. For more information, see [HotFailoverAcceptance.md](HotFailoverAcceptance.md).
