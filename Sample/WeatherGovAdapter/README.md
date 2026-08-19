# WeatherGovAdapter Sample

This sample adapter polls latest observations from [Weather.gov](https://api.weather.gov) and emits normalized dynamic values to OMF egress.

## Key features

Use the Weather.gov sample adapter as a reference implementation for Adapter Framework development. The following features demonstrate common capabilities, design patterns, and framework integrations used when building custom adapters. 

Key features include:

- Polls the latest observations from the public NOAA/National Weather Service REST API (`https://api.weather.gov`) on framework-managed schedules.
- Collects per-station measurements such as temperature, dewpoint, relative humidity, wind speed and direction, barometric pressure, visibility, and the text description.
- Normalizes raw observations into dynamic values and egresses them to OMF endpoints. For example, CONNECT data services or PI Server through PI Web API.
- Generates stable, station-based stream IDs from a template. For example, `WEATHERGOV.STATION.KSEA`.
- Supports station discovery, including seeding discovery from latitude/longitude points.
- Provides resilient HTTP access with configurable request timeouts, retry count, and retry backoff for transient failures.
- Optionally drops null measurements so only valid samples are emitted.

## Build

The Build section shows how to compile the WeatherGov sample adapter and verify that all required packages and project dependencies are available before running the adapter locally. 

```powershell
cd Sample/WeatherGovAdapter
dotnet build AdapterFramework.Data.Adapter.WeatherGovAdapter\AdapterFramework.Data.Adapter.WeatherGovAdapter.csproj -c Debug --ignore-failed-sources
```

## Run locally

The following procedure explains how to start and test the WeatherGov sample adapter in a local development environment to verify connectivity. 

1. Use [Edge-Module/WeatherGovAdapter_defaultConfig.json](Edge-Module/WeatherGovAdapter_defaultConfig.json) as your baseline configuration.
2. Keep the host appsettings in [AdapterFramework.Data.System.Host/appsettings.json](AdapterFramework.Data.System.Host/appsettings.json).
3. Start the host:

```powershell
cd Sample/WeatherGovAdapter
dotnet run --project AdapterFramework.Data.System.Host\AdapterFramework.Data.System.Host.csproj --configuration Debug
```

## Configuration placement

The following section identifies the locations of the sample configuration files used by the WeatherGov adapter. Use the list to locate where to find the complete adapter configuration as well as example configurations for data source, data selection, schedules, and discovery settings. 

- Full edge-style config: [Edge-Module/WeatherGovAdapter_defaultConfig.json](Edge-Module/WeatherGovAdapter_defaultConfig.json)
- DataSource example: [ConfigurationExamples/WeatherGov.DataSource.json](ConfigurationExamples/WeatherGov.DataSource.json)
- DataSelection example: [ConfigurationExamples/WeatherGov.DataSelection.json](ConfigurationExamples/WeatherGov.DataSelection.json)
- Schedules example: [ConfigurationExamples/WeatherGov.Schedules.json](ConfigurationExamples/WeatherGov.Schedules.json)
- Discoveries example: [ConfigurationExamples/WeatherGov.Discoveries.json](ConfigurationExamples/WeatherGov.Discoveries.json)

## Sample adapter configuration

This section documents the configuration models and settings used by the WeatherGov sample adapter. Use the following to understand how to configure connectivity to the Weather.gov service, select stations and measurement data to collect, define collection schedules, and configure discovery behavior. 

### Data source

The data source controls how the adapter connects to and polls the weather service.

- `baseUrl` (default `https://api.weather.gov`) - Base URL of the weather service. Must be HTTPS unless `allowInsecureBaseUrl` is enabled.
- `requestTimeoutMs` (default `10000`) - HTTP request timeout in milliseconds. Must be greater than 0.
- `maxRetries` (default `3`) - Maximum number of retry attempts for transient HTTP failures. Cannot be negative.
- `retryBackoffMs` (default `1000`) - Base backoff, in milliseconds, between retry attempts. Cannot be negative.
- `userAgent` (default `WeatherGovAdapterSample/1.0 (support@example.com)`) - User-Agent header sent with each request. Required by api.weather.gov.
- `streamIdPrefix` (default `weathergov`) - Prefix applied to generated stream IDs.
- `dropNullMeasurements` (default `true`) - When true, null measurement values are not emitted.
- `allowInsecureBaseUrl` (default `false`) - Allows a non-TLS (HTTP) base URL. For local development and testing only; must remain false in production.

### Data selection

Selects which stations and measurement fields are collected and how their streams are named.

- `stationId` (required) - Station ID to collect. For example, `KSEA`.
- `includeFields` (default `[]`) - Measurement fields to include (for example, `temperature_c`, `wind_speed_mps`). If this is left empty, all supported fields are included.
- `scheduleId` (required) - ID of the collection schedule for this station. One of `1` (1 min), `2` (5 min), or `3` (10 min); periods are defined in the Schedules facet.

### Schedules

Collection cadence is set through the framework `Schedules` facet and not the data source. Each entry maps a schedule ID to a period; data selection items reference one via `scheduleId`. See [ConfigurationExamples/WeatherGov.Schedules.json](ConfigurationExamples/WeatherGov.Schedules.json).

- `id` (required) - Schedule ID referenced by `scheduleId` (`1`, `2`, or `3`).
- `period` (required) - Collection interval as an `hh:mm:ss` TimeSpan (for example, `00:01:00` for one minute).

## Validate data flow

Use the following steps to confirm adapter startup, data collection, OMF egress connectivity, stream generation, and runtime error handling.

1. Confirm adapter startup logs contain "Starting WeatherGov adapter".
2. Confirm periodic polling logs for configured stations. For example, KSEA, KPDX.
3. Confirm OMF egress is configured with a reachable endpoint in the `OmfEgress.DataEndpoints` section.
4. Validate stream IDs in downstream target are stable and station-based. For example, `WEATHERGOV.STATION.KSEA`.
5. If OMF endpoint is unavailable, validate by checking that polling runs without fatal errors and retry warnings are logged for transient HTTP failures.
