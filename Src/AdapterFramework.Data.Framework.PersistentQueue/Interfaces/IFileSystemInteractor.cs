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

namespace AdapterFramework.Data.Framework.PersistentQueue.Interfaces;

/// <summary>
/// An abstraction for System.IO.File calls. This exists to improve testability in client classes.
/// </summary>
public interface IFileSystemInteractor
{
    bool FileExists(string path);

    Stream OpenFile(string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.ReadWrite, int bufferSize = 4096, FileOptions options = FileOptions.None);

    void MoveFile(string oldPath, string newPath);

    void DeleteFile(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    string[] GetFilesInDirectory(string path, string searchPattern);
}
