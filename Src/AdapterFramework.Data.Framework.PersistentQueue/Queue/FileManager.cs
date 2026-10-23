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
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;

namespace AdapterFramework.Data.Framework.PersistentQueue.Queue;

internal class FileManager : IFileManager
{
    internal const string MaxBufferSizeReachedMessage = "Maximum persistent buffer size reached for the endpoint with ID: {0}. The oldest buffer file \'{1}\' has been deleted.";

    private const string StateFileName = "state";
    private const string DataFileName = "data";
    private const char PaddingCharacter = '0';
    private const int WriterBufferSize = 0x10000; // Note: Testing has not been done regarding ideal buffer size. Potential opportunity if performance improvements needed.
    private const string PrefixSeparator = "_";
    private readonly IFileSystemInteractor _fileSystem;
    private readonly ILogger _logger;
    private readonly List<string> _filesToDelete;
    private readonly string _directory;
    private readonly string _fileNamePrefix;
    private readonly string _stateFilePath;
    private readonly string _endpointId;

    public FileManager(IFileSystemInteractor fileSystem, string directory, string fileNamePrefix = "", string endpointId = "", ILogger logger = null)
    {
        _fileSystem = fileSystem;
        _directory = directory;
        _fileNamePrefix = fileNamePrefix;
        _logger = logger;
        _stateFilePath = GetStateFilePath();
        _filesToDelete = new List<string>();
        _endpointId = endpointId;

        EnsureDirectoryExists(directory);
    }

    public Stream CreateWriterStream(int dataFileNumber)
    {
        // Note: FileOptions.Asynchronous introduces a condition where writes will expand the file size prior to writing the data
        //       This results in a race condition if another stream is concurrently reading the same file. It will see the file
        //       length has increased but upon reading will only get bytes with a value of 0.  See https://github.com/dotnet/corefx/issues/33081
        //       for discussion and repro code. At time of writing this race condition would break the logic of FileQueue.
        //       Be careful if you want to introduce FileOptions.Asynchronous!
        return _fileSystem.OpenFile(GetDataFilePath(dataFileNumber), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, WriterBufferSize, FileOptions.SequentialScan);
    }

    public Stream CreateReaderStream(int dataFileNumber)
    {
        return _fileSystem.OpenFile(GetDataFilePath(dataFileNumber), FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite, WriterBufferSize, FileOptions.SequentialScan); // TODO: is this ideal buffer size?
    }

    public Stream CreateStateStream()
    {
        return _fileSystem.OpenFile(_stateFilePath, FileMode.OpenOrCreate);
    }

    public void AddPendingDeletion(int fileNumber)
    {
        _filesToDelete.Add(GetDataFilePath(fileNumber));
    }

    public void DeleteFilesPendingDeletion()
    {
        foreach (var file in _filesToDelete)
        {
            TryDeleteFile(file);
        }

        _filesToDelete.Clear();
    }

    public void DeleteOldestFilePendingDeletion()
    {
        _logger?.LogInformation(MaxBufferSizeReachedMessage, _endpointId, _filesToDelete[0].Replace('\\', '/'));
        TryDeleteFile(_filesToDelete[0]);
        _filesToDelete.RemoveAt(0);
    }

    public bool AnyPendingFileDeletions()
    {
        return _filesToDelete.Count > 0;
    }

    public void RepairDirectoryAndUpdateState(QueueState queueState, int maxQueueFiles = 0)
    {
        // Gather info regarding what data files exist
        var dataFiles = GetSortedDataFiles();
        if (dataFiles.Length == 0)
        {
            ResetQueueState(queueState); // No data files means we need a clean state
            return;
        }

        // Data files exist, let's process them
        var fileNumbers = GetFileNumbers(dataFiles);

        // Check if we have too many queue files due to the limit - if we do, delete the old ones so that we satisfy the limit
        if (maxQueueFiles > 0)
        {
            if (fileNumbers.Length > maxQueueFiles)
            {
                var numFilesToDelete = fileNumbers.Length - maxQueueFiles;
                int[] newFiles = new int[maxQueueFiles];
                string[] dataFilePaths = new string[maxQueueFiles];
                int fileNum = 0;
                while (fileNum < fileNumbers.Length)
                {
                    if (fileNum < numFilesToDelete)
                    {
                        TryDeleteFile(dataFiles[fileNum]);
                    }
                    else
                    {
                        newFiles[fileNum - numFilesToDelete] = fileNumbers[fileNum];
                        dataFilePaths[fileNum - numFilesToDelete] = dataFiles[fileNum];
                    }

                    fileNum++;
                }

                dataFiles = dataFilePaths;
                fileNumbers = newFiles;
            }
        }

        var expectedReaderFileFound = DetermineCorrectReaderFile(queueState, fileNumbers, out var readerFileIndex, out var readerFileNumberFound);

        if (!expectedReaderFileFound)
        {
            // Set the queue file to the closest found queue file found and position to 0
            queueState.SetReaderFileNumber(readerFileNumberFound);
            queueState.SetReaderPosition(0);
        }

        // Now let's ensure the remaining data files are sequential, and that the writer file exists
        var writerFileFound = EnsureDataFilesSequentialAndFindWriterFile(queueState, readerFileIndex, fileNumbers, dataFiles);

        if (!writerFileFound)
        {
            // If the writer file number is not found, let's set the writer to the last data file
            queueState.SetWriterFileNumber(fileNumbers[^1]);
        }
    }

    /// <summary>
    /// Deletes the directory being used by the file manager.
    /// </summary>
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    /// <summary>
    /// If there are data files which have a number less than the QueueState.ReaderFile, set the ReaderFile to the oldest (least number)
    /// If the expected reader file given by the queueState is not found, get the first file with a number greater than the expected file number.
    /// </summary>
    private static bool DetermineCorrectReaderFile(QueueState queueState, int[] fileNumbers, out int readerFileIndex, out int readerFileNumberFound)
    {
        readerFileNumberFound = -1;
        readerFileIndex = -1;
        var expectedReaderFileFound = false;
        for (int i = 0; i < fileNumbers.Length; i++)
        {
            if (fileNumbers[i] < queueState.ReaderFileNumber)
            {
                readerFileNumberFound = fileNumbers[i];
                readerFileIndex = i;
                break;
            }

            if (fileNumbers[i] == queueState.ReaderFileNumber)
            {
                readerFileNumberFound = fileNumbers[i];
                readerFileIndex = i;
                expectedReaderFileFound = true;
                break;
            }

            if (fileNumbers[i] > queueState.ReaderFileNumber)
            {
                readerFileNumberFound = fileNumbers[i];
                readerFileIndex = i;
                break;
            }
        }

        return expectedReaderFileFound;
    }

    private static int GetDataFileNumber(string fileName)
    {
        var numString = string.Concat(fileName.ToArray().Reverse().TakeWhile(char.IsNumber).Reverse());
        return int.Parse(numString, CultureInfo.InvariantCulture);
    }

    private static void ResetQueueState(QueueState queueState)
    {
        queueState.SetReaderFileNumber(0);
        queueState.SetReaderPosition(0);
        queueState.SetWriterFileNumber(0);
        queueState.SetWriterPosition(0);
    }

    private static int[] GetFileNumbers(string[] dataFiles)
    {
        var fileNumbers = new int[dataFiles.Length];
        for (int i = 0; i < dataFiles.Length; i++)
        {
            fileNumbers[i] = GetDataFileNumber(dataFiles[i]);
        }

        return fileNumbers;
    }

    private bool EnsureDataFilesSequentialAndFindWriterFile(QueueState queueState, int readerFileIndex, int[] fileNumbers, string[] dataFiles)
    {
        var writerFileFound = false;
        for (int i = readerFileIndex + 1; i < fileNumbers.Length; i++)
        {
            var fileNumberShouldBe = fileNumbers[i - 1] + 1;

            if (fileNumbers[i] != fileNumberShouldBe)
            {
                if (queueState.WriterFileNumber == fileNumbers[i])
                {
                    writerFileFound = true;
                    queueState.SetWriterFileNumber(fileNumberShouldBe);
                }

                _fileSystem.MoveFile(dataFiles[i], GetDataFilePath(fileNumberShouldBe));

                fileNumbers[i] = fileNumberShouldBe;
            }
            else if (queueState.WriterFileNumber == fileNumbers[i])
            {
                writerFileFound = true;
            }
        }

        return writerFileFound;
    }

    private string GetStateFilePath()
    {
        return Path.Combine(_directory, $"{_fileNamePrefix}{PrefixSeparator}{StateFileName}");
    }

    private string GetDataFilePath(int fileNumber)
    {
        return Path.Combine(_directory, $"{_fileNamePrefix}{PrefixSeparator}{DataFileName}{fileNumber}");
    }

    private void EnsureDirectoryExists(string directory)
    {
        if (!_fileSystem.DirectoryExists(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error deleting file: {Path}.", path);
        }
    }

    private string[] GetSortedDataFiles()
    {
        var dataFiles = _fileSystem.GetFilesInDirectory(_directory, $"{_fileNamePrefix}{PrefixSeparator}{DataFileName}*");
        var sortedDataFiles = dataFiles.OrderBy(fileName => Regex.Replace(fileName, @"\d+", eval => eval.Value.PadLeft(4, PaddingCharacter))).ToArray();

        return sortedDataFiles;
    }
}
