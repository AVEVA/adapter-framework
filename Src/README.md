# Adapter Framework

Adapter Framework is a cross-platform .NET framework for building adapters that collect industrial data from external systems and egress that data to Open Message Format (OMF) endpoints such as CONNECT data services and on-premises PI Server through PI Web API. 

The framework is the reusable runtime and library set that removes the common plumbing required to build an industrial adapter. Instead of every application handling universal functionality (such as hosting, configuration, and buffering) in its own unique way, the framework provides those shared capabilities as reusable components. 

At a high level, the framework provides: 

- A system host that boots the runtime, configures services, and loads adapters. 
- Common adapter base classes that manage registration, initialization, configuration handling, lifecycle callbacks, and operational services. 
- Data flow, buffering, compression, and message processing components for preparing OMF payloads. 
- Endpoint and egress services for delivering OMF messages to downstream systems. 
- Supporting services for logging, diagnostics, configuration, security, failover, and runtime registries. 
- Optional health and diagnostics that can be incorporated into custom adapters. 


## NuGet Package reference

The Adapter Framework provides a set of modular NuGet packages that can be combined with adapter applications. You can reference only the packages required by your solution or combine them to create a complete adapter host. The packages provide reusable components for configuration, hosting, discovery, buffering, messaging, security, and OMF data delivery. 

All packages share the `AdapterFramework.Data` prefix and can be installed with `dotnet add package <PackageName>`.

### Core

| Package | Description |
|---------|-------------|
| **AdapterFramework.Data.DataModel** | Core data model/types shared across the adapter framework. |
| **AdapterFramework.Data.Framework.Abstractions** | Shared interfaces and contracts for configuration, security, events, health, and message processing. |
| **AdapterFramework.Data.Framework.Common** | Common shared utilities, HTTP communication constants, security helpers, and health models. |
| **AdapterFramework.Data.Framework.AdapterCommon** | Common adapter base classes, history recovery, scheduling, and discovery. |
| **AdapterFramework.Data.Framework.Host** | Executable host application that composes and runs the full adapter system. |

### Configuration and identity

| Package | Description |
|---------|-------------|
| **AdapterFramework.Data.Framework.ConfigurationProvider** | Configuration management and persistence with ASP.NET Core integration and command handling. |
| **AdapterFramework.Data.Framework.ComponentIdProvider** | Component identity service for registering and resolving adapter component IDs. |
| **AdapterFramework.Data.Framework.Registry** | Component registry for adapter registration, discovery, and configuration mapping. |
| **AdapterFramework.Data.Framework.CancellationTokenService** | Centralized cancellation-token management for framework operations. |

### Messaging and data flow

| Package | Description |
|---------|-------------|
| **AdapterFramework.Data.Framework.Messages** | OMF message type definitions and serialized message models. |
| **AdapterFramework.Data.Framework.Messageprocessor** | Adapter message processing logic with data filtering and partitioning. |
| **AdapterFramework.Data.Framework.Dataflow** | Data flow pipeline components for routing and processing OMF messages between adapter stages. |
| **AdapterFramework.Data.Framework.Serialization** | OMF message serialization and deserialization. |
| **AdapterFramework.Data.Framework.Compression** | Message compression and decompression for OMF payloads sent to endpoints. |

### Buffering and delivery

| Package | Description |
|---------|-------------|
| **AdapterFramework.Data.Framework.Buffering** | Persistent OMF message buffering and queuing for reliable delivery. |
| **AdapterFramework.Data.Framework.PersistentQueue** | Low-level durable file-based queue implementation with CRC32 integrity checks. |
| **AdapterFramework.Data.Framework.EgressComponent** | Egress component for outbound OMF data delivery to configured endpoints. |
| **AdapterFramework.Data.Framework.EndpointManager** | HTTP endpoint management, OMF egress, and multi-endpoint orchestration. |
| **AdapterFramework.Data.Framework.Failover** | Failover and high-availability support with heartbeat-based group coordination. |

### Diagnostics, logging, and security

| Package | Description |
|---------|-------------|
| **AdapterFramework.Data.Framework.Logger** | Structured logging implementation using Serilog with console and file sinks. |
| **AdapterFramework.Data.Framework.Diagnostics** | Health monitoring, diagnostics counters, and status reporting for adapter components. |
| **AdapterFramework.Data.Framework.Dataprotectionprovider** | Bootstraps and provides ASP.NET Core Data Protection services for the adapter. |
| **AdapterFramework.Data.Framework.DataProtector** | Encryption and decryption of sensitive configuration values using ASP.NET Core Data Protection. |
| **AdapterFramework.Data.Framework.Extensions** | Low-level utility and extension methods with unsafe code support. |

## Target framework

These packages target **.NET 10**.

## Project and source

- Project site: [https://github.com/AVEVA/adapter-framework](https://github.com/AVEVA/adapter-framework)
- Source repository: [https://github.com/AVEVA/adapter-framework](https://github.com/AVEVA/adapter-framework)

## License

Licensed under the [Apache License, Version 2.0](https://www.apache.org/licenses/LICENSE-2.0).

```text
Copyright 2018-2026 AVEVA Group Limited

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

SPDX-License-Identifier: Apache-2.0
```

## About AVEVA

AVEVA is a global leader in industrial software. For more information, visit
[https://www.aveva.com](https://www.aveva.com).
