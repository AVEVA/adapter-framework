// Copyright 2018-2026 AVEVA Group Limited
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// SPDX-License-Identifier: Apache-2.0
namespace AdapterFramework.Data.Framework.AdapterCommon.Discovery.Constants;

public static class DiscoveryConstants
{
    public const string UriSeparator = "/";
    public const string ResultKeyword = "result";
    public const string DiscoveryIdAlreadyExists = "Discovery with ID {0} already exists.";
    public const string DiscoveryCanceledMessage = "Discovery operation has been canceled.";
    public const string DiscoveryIdNotFound = "Discovery with ID {0} is not found.";
    public const string InvalidOptionsSkipValue = "Skip property value must be equal to or greater than zero.";
    public const string InvalidOptionsCountValue = "Count property value must be equal to or greater than zero.";
    public const string DiscoveryResultNotFoundMessage = "Unable to find discovery result for component ID {0} and discovery ID {1}.";
    public const string UnableToSaveDataSelection = "Unable to save data selection configuration for component ID {0} and discovery ID {1}: {2}.";
    public const string FailedToAddDiscovery = "Failed to add discovery state to the discovery collection for discovery ID {0}.";
    public const string InvalidDiscoveriesConfiguration = "Discoveries configuration is invalid. {Errors}.";
    public const string ExistingDiscoveryInProcess = "Unable to start a new discovery operation since discovery with ID {0} is in progress. Only one active discovery operation is permitted at a time.";
    public const string CannotStartWithoutDataSource = "Cannot start discovery with ID {0} without a data source.";
    public const string StartingDiscovery = "Starting discovery with ID {DiscoveryId} and query string: {DiscoveryQuery}.";
    public const string CompletedDiscovery = "Discovery with ID {DiscoveryId} has been completed.";
    public const string DiscoveredItemsAreToBeAdded = "Discovered items are going to be added to data selection.";
    public const string FailedToSaveDiscoveryResult = "Failed to save discovery result for ID {DiscoveryId}.";
    public const string DiscoveryCanceled = "Discovery operation with ID {DiscoveryId} has been canceled.";
    public const string DiscoveryResultDeleted = "Discovery result has been deleted.";
    public const string DiscoveryFailedWithException = "Discovery operation failed: {0}.";
    public const string DiscoveryFailed = "Discovery operation with ID {DiscoveryId} failed.";
    public const string SaveDiscoveryStatesFailed = "Failed to save discovery states: {0}";
    public const string UnableToSerializeDiscoveryResult = "Unable to read discovery result for component ID {0} and discovery ID {1}: {2}.";
    public const string UnableToSerializeDataSelectionConfiguration = "Unable to read current data selection result for component ID {0}.";
    public const string MergeWithDataSelectionResponseException = "An exception occurred while trying to combine current data selection with discovery ID {0} for component ID {1}. Please see the message log for more information.";
    public const string MergeWithDataSelectionLogException = "After merging current Data Selection with discovery ID {DiscoveryId} for component ID {ComponentId}, saving the merged configuration failed with the following exception. No changes were applied: {Message}";
    public const string DiscoveryStateNotFoundMessage = "Unable to find discovery state for component ID {0} and discovery ID {1}.";
}
