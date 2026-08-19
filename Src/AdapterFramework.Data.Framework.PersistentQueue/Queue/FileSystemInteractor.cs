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
using System.IO;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;

namespace AdapterFramework.Data.Framework.PersistentQueue.Queue;

internal class FileSystemInteractor : IFileSystemInteractor
{
    private const int DefaultBufferSize = 4096;

    public void DeleteFile(string path)
    {
        File.Delete(path);
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public void MoveFile(string oldPath, string newPath)
    {
        File.Move(oldPath, newPath);
    }

    public Stream OpenFile(string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.ReadWrite, int bufferSize = DefaultBufferSize, FileOptions options = FileOptions.None)
    {
        return new FileStream(path, mode, access, share, bufferSize, options);
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public string[] GetFilesInDirectory(string path, string searchPattern)
    {
        return Directory.GetFiles(path, searchPattern);
    }
}
