# Sample adapter

This folder contains a sample adapter created using the Adapter Framework for reference purposes. If you are new to the repository, begin with:

- [`../Adapter Template/README.md`](../Adapter%20Template/README.md) - Includes guidelines for creating a new adapter.
- [../Src/README.md](../Src/README.md) - Includes framework source layout and internals.
- [`WeatherGovAdapter/README.md`](WeatherGovAdapter/README.md) - what the sample does and how to build and run it.

## Available sample

### `WeatherGovAdapter/`

This is a complete sample adapter that polls the public [weather.gov API](https://api.weather.gov/) service. The sample adapter demonstrates how to structure an adapter solution, model data source and data selection configuration, collect data from an HTTP-based source, implement discovery and data processing workflows, and validate functionality through unit testing.

The sample includes:

- `WeatherGovAdapter.sln` - A sample solution.
- `AdapterFramework.Data.Adapter.WeatherGovAdapter/` - The main adapter project.
- `AdapterFramework.Data.System.Host/` - The local host used to run the adapter.
- `Tests/AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests/` - Unit tests.
- `ConfigurationExamples/` - Example configuration payloads.
- `Edge-Module/` - Default edge-style configuration.

