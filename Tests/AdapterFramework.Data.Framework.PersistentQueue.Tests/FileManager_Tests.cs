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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Xunit;

namespace AdapterFramework.Data.Framework.PersistentQueue.Tests;

public class FileManager_Tests
{
    private const string StateFileName = "state";
    private const string DataFileName = "data";
    private const string TestDirectory = "testDir";

    [Fact]
    public void FileManager_Ctor_CreatesDirectoryIfItDoesNotExist()
    {
        bool directoryCreated = false;
        var dir = "exampleDir";
        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fileSystem => fileSystem.DirectoryExists(dir)).Returns(false);
        mockFileSystemInteractor.Setup(fileSystem => fileSystem.CreateDirectory(dir)).Callback(() => directoryCreated = true);
        _ = new FileManager(mockFileSystemInteractor.Object, dir);
        Assert.True(directoryCreated);
    }

    [Fact]
    public void FileManager_CreateStateStream_GetsStreamForStateFile()
    {
        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        using var correctStateStream = new MemoryStream();
        mockFileSystemInteractor.Setup(fsi => fsi.OpenFile(GetStateFilePath(TestDirectory), It.IsAny<FileMode>(), It.IsAny<FileAccess>(),
                It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>())).Returns(correctStateStream);

        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);
        var streamReturned = fileManager.CreateStateStream();
        
        Assert.Equal(correctStateStream, streamReturned);
    }

    [Fact]
    public void FileManager_CreateWriterStream_ReturnsStreamForCorrespondingDataFile()
    {
        var dataFile1 = 1;
        var dataFile27 = 27;
        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();

        using var dataFile1Stream = new MemoryStream();
        using var dataFile27Stream = new MemoryStream();

        mockFileSystemInteractor.Setup(fsi => fsi.OpenFile(GetDataFilePath(TestDirectory, dataFile1), It.IsAny<FileMode>(),
                It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>())).Returns(dataFile1Stream);
        mockFileSystemInteractor.Setup(fsi => fsi.OpenFile(GetDataFilePath(TestDirectory, dataFile27), It.IsAny<FileMode>(),
                It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>())).Returns(dataFile27Stream);

        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        using var stream1 = fileManager.CreateWriterStream(dataFile1);
        using var stream27 = fileManager.CreateWriterStream(dataFile27);

        Assert.Equal(dataFile1Stream, stream1);
        Assert.Equal(dataFile27Stream, stream27);
    }

    [Fact]
    public void FileManager_CreateReaderStream_ReturnsStreamForCorrespondingDataFile()
    {
        var dataFile1 = 1;
        var dataFile27 = 27;

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();

        using var dataFile1Stream = new MemoryStream();
        using var dataFile27Stream = new MemoryStream();

        mockFileSystemInteractor.Setup(fsi => fsi.OpenFile(GetDataFilePath(TestDirectory, dataFile1), It.IsAny<FileMode>(),
                It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>())).Returns(dataFile1Stream);
        mockFileSystemInteractor.Setup(fsi => fsi.OpenFile(GetDataFilePath(TestDirectory, dataFile27), It.IsAny<FileMode>(),
                It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>())).Returns(dataFile27Stream);

        var fm = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        using var stream1 = fm.CreateReaderStream(dataFile1);
        using var stream27 = fm.CreateReaderStream(dataFile27);

        Assert.Equal(dataFile1Stream, stream1);
        Assert.Equal(dataFile27Stream, stream27);
    }

    [Fact]
    public void FileManager_DeleteFilesPendingDeletion_DeletesFilesAddedForPendingDeletion()
    {
        var deletedFiles = new List<string>();

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.DeleteFile(It.IsAny<string>())).Callback<string>(path => deletedFiles.Add(path));

        var fileNumbersToDelete = new List<int> { 2, 5, 3, 9 };
        var fm = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        foreach (var num in fileNumbersToDelete) fm.AddPendingDeletion(num);

        Assert.True(deletedFiles.Count == 0, "Files should not be deleted until deletion is called.");

        fm.DeleteFilesPendingDeletion();

        Assert.Equal(fileNumbersToDelete.Count, deletedFiles.Count);
        for (int i = 0; i < fileNumbersToDelete.Count; i++)
        {
            Assert.Equal(GetDataFilePath(TestDirectory, fileNumbersToDelete[i]), deletedFiles[i]);
        }
    }

    [Fact]
    public void FileManager_DeleteOldestFilePendingDeletion_DeletesOldestFilePendingDeletion()
    {
        var deletedFiles = new List<string>();
        var testLogger = new AdapterFramework.Data.Framework.Tests.Helper.TestLogger();

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.DeleteFile(It.IsAny<string>()))
            .Callback<string>(path => deletedFiles.Add(path));

        var fileNumbersToDelete = new List<int> { 2, 5, 3, 9 };
        var fm = new FileManager(mockFileSystemInteractor.Object, TestDirectory, logger: testLogger);

        foreach (var num in fileNumbersToDelete) fm.AddPendingDeletion(num);

        Assert.True(deletedFiles.Count == 0, "Files should not be deleted until deletion is called.");

        fm.DeleteOldestFilePendingDeletion();

        Assert.Single(deletedFiles);
        Assert.Equal(GetDataFilePath(TestDirectory, fileNumbersToDelete[0]), deletedFiles[0]);

        var logMessages = testLogger.GetLogMessages();

        Assert.NotEmpty(logMessages);
        Assert.Equal(LogLevel.Information, logMessages[0].LogLevel);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, FileManager.MaxBufferSizeReachedMessage, "", deletedFiles[0].Replace('\\', '/')), logMessages[0].LogMessage);
    }

    [Fact]
    public void FileManager_AnyPendingFileDeletions_ReturnsTrueIfPendingDeletionsElseFalse()
    {
        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        Assert.False(fileManager.AnyPendingFileDeletions());
        fileManager.AddPendingDeletion(0);
        Assert.True(fileManager.AnyPendingFileDeletions());
    }

    #region RepairDirectoryAndUpdateState

    /// <summary>
    /// If there are no data files, we need to ensure the QueueState is reset to 0s so that we start with a new data file at index 0.
    /// </summary>
    [Fact]
    public void FileManager_RepairDirectoryAndUpdateState_NoDataFiles_ResetsQueueStateAndPersistsIt()
    {
        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.GetFilesInDirectory(TestDirectory, It.IsAny<string>())).Returns(Array.Empty<string>());

        var queueState = GetValidQueueStateWithNonZeroFields();
        var freshQueueState = new QueueState();
        Assert.False(QueueStatesEqual(queueState, freshQueueState));

        var fm = new FileManager(mockFileSystemInteractor.Object, TestDirectory);
        fm.RepairDirectoryAndUpdateState(queueState);

        // With no data files found, QS should be reset to 0s
        Assert.True(QueueStatesEqual(queueState, freshQueueState));
    }

    [Fact]
    public void FileManager_RepairDirectoryAndUpdateState_QueueStateWriterFileNumberLessThanNewestQueueFile_QueueStateWriterFileNumberUpdatedToNewestDataFile()
    {
        var existingFileNumbers = new[] { 0, 1, 2, 3, 4, 5, 6 };
        var dataFiles = new string[existingFileNumbers.Length];
        for (int i = 0; i < existingFileNumbers.Length; i++)
        {
            dataFiles[i] = GetDataFilePath(TestDirectory, existingFileNumbers[i]);
        }

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.GetFilesInDirectory(TestDirectory, It.IsAny<string>())).Returns(dataFiles);

        var deletedFiles = new List<string>();
        mockFileSystemInteractor.Setup(fsi => fsi.DeleteFile(It.IsAny<string>()))
            .Callback<string>(s => deletedFiles.Add(s));

        var queueState = new QueueState();
        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        fileManager.RepairDirectoryAndUpdateState(queueState);

        // No files should have been deleted
        Assert.Empty(deletedFiles);

        // QueueState Should be updated so that Reader is still on the first data file, writer is on the last file
        Assert.Equal(0, queueState.ReaderFileNumber);
        Assert.Equal(0, queueState.ReaderPosition);
        Assert.Equal(existingFileNumbers[^1], queueState.WriterFileNumber);
    }

    [Fact]
    public void FileManager_RepairDirectoryAndUpdateState_Sorting_Test()
    {
        var fileMoved = false;
        var existingFileNumbers = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var dataFiles = new string[existingFileNumbers.Length];
        for (int i = 0; i < existingFileNumbers.Length; i++)
        {
            dataFiles[i] = GetDataFilePath(TestDirectory, existingFileNumbers[i]);
        }

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.GetFilesInDirectory(TestDirectory, It.IsAny<string>())).Returns(dataFiles);

        var deletedFiles = new List<string>();
        mockFileSystemInteractor.Setup(fsi => fsi.DeleteFile(It.IsAny<string>())).Callback<string>(s => deletedFiles.Add(s));
        mockFileSystemInteractor.Setup(fsi => fsi.MoveFile(It.IsAny<string>(), It.IsAny<string>())).Callback(() => fileMoved = true);

        var queueState = new QueueState();
        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        fileManager.RepairDirectoryAndUpdateState(queueState);

        // No files should have been deleted
        Assert.Empty(deletedFiles);

        // QueueState Should be updated so that Reader is still on the first data file, writer is on the last file
        Assert.Equal(0, queueState.ReaderFileNumber);
        Assert.Equal(0, queueState.ReaderPosition);
        Assert.Equal(existingFileNumbers[^1], queueState.WriterFileNumber);
        Assert.False(fileMoved);
    }

    [Fact]
    public void FileManager_RepairDirectoryAndUpdateState_QueueStateWriterFileNumberGreaterThanNewestQueueFile_QueueStateWriterFileNumberUpdatedToNewestDataFile()
    {
        var existingFileNumbers = new[] { 0, 1, 2, 3, 4, 5, 6 };
        var dataFiles = new string[existingFileNumbers.Length];
        for (int i = 0; i < existingFileNumbers.Length; i++)
        {
            dataFiles[i] = GetDataFilePath(TestDirectory, existingFileNumbers[i]);
        }

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.GetFilesInDirectory(TestDirectory, It.IsAny<string>())).Returns(dataFiles);

        var deletedFiles = new List<string>();
        mockFileSystemInteractor.Setup(fsi => fsi.DeleteFile(It.IsAny<string>())).Callback<string>(s => deletedFiles.Add(s));

        var queueState = new QueueState();
        queueState.SetWriterFileNumber(27);
        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        fileManager.RepairDirectoryAndUpdateState(queueState);

        // No files should have been deleted
        Assert.Empty(deletedFiles);

        // QueueState Should be updated so that Reader is still on the first data file, writer is on the last file
        Assert.Equal(0, queueState.ReaderFileNumber);
        Assert.Equal(0, queueState.ReaderPosition);
        Assert.Equal(existingFileNumbers[^1], queueState.WriterFileNumber);
    }

    [Fact]
    public void FileManager_RepairDirectoryAndUpdateState_QueueStateReaderFileNumberLessThanOldestQueueFile_QueueStateReaderFileNumberUpdatedToOldestDataFile()
    {
        var existingFileNumbers = new[] { 2, 3, 4, 5, 6, 7, 8 };
        var dataFiles = new string[existingFileNumbers.Length];
        for (int i = 0; i < existingFileNumbers.Length; i++)
        {
            dataFiles[i] = GetDataFilePath(TestDirectory, existingFileNumbers[i]);
        }

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.GetFilesInDirectory(TestDirectory, It.IsAny<string>())).Returns(dataFiles);

        var deletedFiles = new List<string>();
        mockFileSystemInteractor.Setup(fsi => fsi.DeleteFile(It.IsAny<string>())).Callback<string>(s => deletedFiles.Add(s));

        var queueState = new QueueState();
        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        fileManager.RepairDirectoryAndUpdateState(queueState);

        // No files should have been deleted
        Assert.Empty(deletedFiles);

        // QueueState Should be updated so that Reader is still on the first data file, writer is on the last file
        Assert.Equal(existingFileNumbers[0], queueState.ReaderFileNumber);
        Assert.Equal(0, queueState.ReaderPosition);
        Assert.Equal(existingFileNumbers[^1], queueState.WriterFileNumber);
    }

    [Fact]
    public void FileManager_RepairDirectoryAndUpdateState_QueueStateReaderFileNumberGreaterThanOldestQueueFile_QueueStateReaderFileNumberSetToOldestFile()
    {
        var existingFileNumbers = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        var dataFiles = new string[existingFileNumbers.Length];
        for (int i = 0; i < existingFileNumbers.Length; i++)
        {
            dataFiles[i] = GetDataFilePath(TestDirectory, existingFileNumbers[i]);
        }

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.GetFilesInDirectory(TestDirectory, It.IsAny<string>())).Returns(dataFiles);

        var deletedFiles = new List<string>();
        mockFileSystemInteractor.Setup(fsi => fsi.DeleteFile(It.IsAny<string>())).Callback<string>(s => deletedFiles.Add(s));

        var queueState = new QueueState();
        queueState.SetReaderFileNumber(2);
        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        fileManager.RepairDirectoryAndUpdateState(queueState);

        // No files should have been deleted
        Assert.Empty(deletedFiles);

        // QueueState Should be updated so that Reader is on the first data file, writer is on the last file
        Assert.Equal(0, queueState.ReaderFileNumber);
        Assert.Equal(0, queueState.ReaderPosition);
        Assert.Equal(existingFileNumbers[^1], queueState.WriterFileNumber);
    }

    [Fact]
    public void FileManager_RepairDirectoryAndUpdateState_ReaderFileDeleted()
    {
        var existingFileNumbers = new[] { 0, 1, 4, 5, 6, 7, 8 }; // Missing 2,3
        var dataFiles = new string[existingFileNumbers.Length];
        for (int i = 0; i < existingFileNumbers.Length; i++)
        {
            dataFiles[i] = GetDataFilePath(TestDirectory, existingFileNumbers[i]);
        }

        // We expect the repair operation to rename the files so that they are consecutive
        var expectedDataFilesAfterRename = new string[dataFiles.Length];
        var expectedDataFileNumbersAfterRename = new int[dataFiles.Length];
        for (int i = 0; i < dataFiles.Length; i++)
        {
            expectedDataFileNumbersAfterRename[i] = i;
            expectedDataFilesAfterRename[i] = GetDataFilePath(TestDirectory, i);
        }

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.GetFilesInDirectory(TestDirectory, It.IsAny<string>())).Returns(dataFiles);

        var deletedFiles = new List<string>();
        mockFileSystemInteractor.Setup(fsi => fsi.DeleteFile(It.IsAny<string>())).Callback<string>(s => deletedFiles.Add(s));
        mockFileSystemInteractor.Setup(fsi => fsi.MoveFile(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((oldPath, newPath) =>
            {
                var dataFileIndex = Array.IndexOf(dataFiles, oldPath);
                dataFiles[dataFileIndex] = newPath;
            });

        var queueState = new QueueState();
        queueState.SetWriterFileNumber(8);
        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        fileManager.RepairDirectoryAndUpdateState(queueState);

        // No files should have been deleted
        Assert.Empty(deletedFiles);

        // Data files should have been renamed to match the expected
        Assert.Equal(dataFiles, expectedDataFilesAfterRename);

        // QueueState Should be updated so that Reader is still on the first data file, writer is on the renamed last file
        Assert.Equal(0, queueState.ReaderFileNumber);
        Assert.Equal(0, queueState.ReaderPosition);
        Assert.Equal(expectedDataFileNumbersAfterRename[^1], queueState.WriterFileNumber);
    }

    [Fact]
    public void FileManager_RepairDirectoryAndUpdateState_TooManyQueueFiles_OldestDeletedToFitLimitAndQueueStateUpdated()
    {
        var maxFiles = 3;
        var existingFileNumbers = new[] { 0, 1, 2, 3, 4, 5, 6 };
        var dataFiles = new string[existingFileNumbers.Length];
        for (int i = 0; i < existingFileNumbers.Length; i++)
        {
            dataFiles[i] = GetDataFilePath(TestDirectory, existingFileNumbers[i]);
        }

        var mockFileSystemInteractor = new Mock<IFileSystemInteractor>();
        mockFileSystemInteractor.Setup(fsi => fsi.GetFilesInDirectory(TestDirectory, It.IsAny<string>())).Returns(dataFiles);

        var deletedFiles = new List<string>();
        mockFileSystemInteractor.Setup(fsi => fsi.DeleteFile(It.IsAny<string>())).Callback<string>(s => deletedFiles.Add(s));

        var queueState = new QueueState();
        var fileManager = new FileManager(mockFileSystemInteractor.Object, TestDirectory);

        fileManager.RepairDirectoryAndUpdateState(queueState, maxFiles);

        // Oldest files should have been deleted to accommodate new limit
        Assert.Equal(dataFiles.Length - maxFiles, deletedFiles.Count);
        for (int i = 0; i < dataFiles.Length - maxFiles; i++)
        {
            Assert.Contains(dataFiles[i], deletedFiles);
        }

        // QueueState Should be updated so that Reader is on the oldest non-deleted file, writer is on the last file
        Assert.Equal(existingFileNumbers[(existingFileNumbers.Length - 1) - (maxFiles - 1)], queueState.ReaderFileNumber);
        Assert.Equal(0, queueState.ReaderPosition);
        Assert.Equal(existingFileNumbers[^1], queueState.WriterFileNumber);
    }

    #endregion

    private static string GetDataFileName(int fileNumber)
    {
        return "_" + DataFileName + fileNumber;
    }

    private static string GetStateFilePath(string dir)
    {
        return Path.Combine(dir, "_" + StateFileName);
    }

    private static string GetDataFilePath(string dir, int fileNumber)
    {
        return Path.Combine(dir, GetDataFileName(fileNumber));
    }

    private static QueueState GetValidQueueStateWithNonZeroFields()
    {
        return new QueueState(3, 5444, 4, 151515);
    }

    private static bool QueueStatesEqual(QueueState queueState1, QueueState queueState2)
    {
        return queueState1.ReaderFileNumber == queueState2.ReaderFileNumber &&
               queueState1.ReaderPosition == queueState2.ReaderPosition &&
               queueState1.WriterFileNumber == queueState2.WriterFileNumber &&
               queueState1.WriterPosition == queueState2.WriterPosition;
    }
}
