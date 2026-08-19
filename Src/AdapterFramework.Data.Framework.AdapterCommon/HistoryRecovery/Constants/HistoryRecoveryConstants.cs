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
namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery.Constants;

public static class HistoryRecoveryConstants
{
    public const string HistoryRecoveryIdAlreadyExists = "History recovery with ID {0} already exists.";
    public const string HistoryRecoveryIdNotFound = "History recovery with ID {0} is not found.";
    public const string HistoryRecoveryStateNotFoundMessage = "Unable to find history recovery state for component ID {0} and history recovery ID {1}.";
    public const string FailedToAddHistoryRecovery = "Failed to add history recovery state to the history recovery collection for history recovery ID {0}.";
    public const string InvalidHistoryRecoveriesConfiguration = "History recoveries configuration is invalid. {Errors}.";
    public const string ExistingHistoryRecoveryInProcess = "Unable to start a new history recovery operation since history recovery with ID {0} is in progress. Only one active history recovery operation is permitted at a time.";
    public const string CannotStartWithoutDataSource = "Cannot start history recovery with ID {0} without a data source.";
    public const string StartingHistoryRecovery = "Starting history recovery with ID {HistoryRecoveryId}.";
    public const string CompletedHistoryRecovery = "History recovery with ID {HistoryRecoveryId} has been completed.";
    public const string FailedToSaveHistoryRecoveryResult = "Failed to save history recovery result for ID {HistoryRecoveryId}.";
    public const string HistoryRecoveryCanceled = "History recovery operation with ID {HistoryRecoveryId} has been canceled.";
    public const string HistoryRecoveryCanceledMessage = "History recovery operation has been canceled.";
    public const string HistoryRecoveryFailedWithException = "History recovery operation failed: {0}.";
    public const string SaveHistoryRecoveryStatesFailed = "Failed to save history recovery states: {0}";
    public const string MissingDataSelection = "No existing data selection configuration. Add a data selection configuration before starting history recovery.";

    public const int MaxAutomaticHistoryIntervalLengthDays = 4;
}
