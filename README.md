# Welcome to the Adapter Framework

The Adapter Framework is a developer-focused toolkit for building and extending data connectivity adapters software components that collect operational and industrial data and publish it in Open Message Format (OMF) for downstream consumption. 

It provides a consistent foundation for taking data from various sources (devices, historians, files, protocols, APIs, edge applications) and converting it into OMF 1.2 or OMF 2.0 compliant messages, ready for ingestion into AVEVA PI Server, CONNECT, Edge Data Store, or any other OMF-compatible destination. 

Being open source, the Adapter Framework makes it easier for developers to standardize how data collection applications are built and encourage collaboration across the community, so solutions evolve faster, are easier to validate, and can be reused across multiple projects and data sources.

The purpose of this README is to help you get started with developing your own adapter using the resources available to you. If you would like more detailed documentation, you can find it through this link: [Adapter Framework](https://docs.aveva.com/bundle/adapter-framework/)

## Key Benefits

- Reduces development effort by providing built-in capabilities for configuration, logging, monitoring, security, and data delivery. 
- Expands connectivity coverage beyond the standard AVEVA Adapter portfolio. 
- Enables faster access to operational data from specialized or unsupported systems. 
- Allows customers to work with internal development teams or partners to address connectivity requirements without waiting for AVEVA-developed adapters. 
- Supports connectivity to AVEVA PI Server, CONNECT, and AVEVA Edge Data Store using the same framework technology used by AVEVA Adapters. 

## Features of the Adapter Framework

At a high level, the framework provides:

- A system host that boots the runtime, configures services, and loads adapters.
- Common adapter base classes that manage registration, initialization, configuration handling, lifecycle callbacks, and operational services.
- Data flow, buffering, compression, and message processing components for preparing OMF payloads.
- Endpoint and egress services for delivering OMF messages to downstream systems.
- Supporting services for logging, diagnostics, configuration, security, failover, and runtime registries.

If you want the implementation details of the framework internals and the full project list in the solution, see [Src/README.md](Src/README.md).

## How to use the Adapter Framework

The Adapter Framework reduces the time and effort required to build custom adapters by providing common runtime services and infrastructure out of the box. Developers can focus on integrating their data source instead of implementing core capabilities such as configuration, buffering, diagnostics, security, and OMF data delivery. 

The Adapter Framework can be used in two ways. The recommended method is to reference the pre-compiled Adapter Framework packages on NuGet.org to make use of the features that are available in other Adapter Framework based products, such as AVEVA Adapters. See [Src/README.md](Src/README.md) for a full list of available packages.

If you want to change the framework to fit your needs, you can fork a branch of the Adapter Framework's source code and build your own packages to use however you like. See 

## Dependencies

These dependencies provide the foundational services and capabilities that support adapter development and runtime execution.

### Open Message Format (OMF)

Open Message Format (OMF) is a message-based specification used to describe data types, streams, metadata, and time-series values in a consistent way so that producers and consumers can exchange industrial data reliably. OMF is the transport contract used by the framework to serialize adapter output and send it to supported endpoints. The source code currently includes support for OMF versions 1.2 and 2.0, with the system host using OMF 2.0 for the current host entry point. 

OMF 1.2 and OMF 2.0 use different data modeling approaches. 

OMF 1.2 is based on the traditional Type, Container, and Data message model and is primarily focused on time-series data streams. Use OMF 1.2 when you need compatibility with existing OMF 1.2 based solutions or when your use case is primarily focused on traditional time-series data streams. 

OMF 2.0 uses a Schema and Instance message model that supports advanced data modeling scenarios, including entities, events, relationships, and streaming data. It is the recommended version for new Adapter Framework development. 

For additional information, see [OMF 1.2](https://docs.aveva.com/bundle/omf/page/1283981.html) or [OMF 2.0](https://docs.aveva.com/bundle/omf/page/1626561.html) documentation. 

### Third Party Dependencies

The Adapter Framework depends on a number of third-party components that are not authored by AVEVA and are not included with this project. AVEVA makes no warranties regarding these components and cannot guarantee their security or suitability for your purposes; each is governed by its own license, and you are responsible for reviewing and complying with those terms. 

> **See [DEPENDENCIES.md](./DEPENDENCIES.md) for the list of components and their licenses.** This is a best-effort list and may not be complete.

## Repository layout

- [Src](Src/README.md) - Core framework source code and the main `AdapterFramework.sln` solution.
- [Sample](Sample/README.md) - Documentation for sample adapter usage and the sample adapter source reference.
- [Adapter Template](Adapter%20Template/README.md) - Template-based starting point for creating a new adapter.
- [Tests](Tests) - Unit and test helper projects for the framework.

## Releases

Version: 2.1.0
- Release date: August 19, 2026
- Release notes: [2.1.0](https://docs.aveva.com/bundle/adapter-framework/page/1626192.html)

## Additional documentation

- [CONTRIBUTING.md](CONTRIBUTING.md) - How to contribute changes, tests, and documentation.
- [SECURITY.md](SECURITY.md) - How to report suspected vulnerabilities responsibly.
