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
using System.IO;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;

namespace AdapterFramework.Data.Framework.PersistentQueue.Tests;

/// <summary>
/// A fake file system for testing the FileQueue.
///
/// The primary purpose of the FakeFileSystem is to enable integration testing of disk space errors from the file system.
/// 
/// Note that functionality is limited. While this was created to mimic the file system it is possible that there are errors in the implementation or
///  that actual file systems will have different behaviors. Some methods are also not fully implemented. For that reason it is recommended to only
///  use this in test cases or which it is difficult or impossible to perform automated testing using mocks or the actual file system.
/// </summary>
internal class FakeFileSystem
{
    private readonly IDictionary<string, byte[]> _files;
    private int _diskSize;
    private int _totalDiskUsed;

    public FakeFileSystem(int diskSizeMB = 5)
    {
        _diskSize = diskSizeMB * 1024 * 1024;
        _files = new Dictionary<string, byte[]>();
        _totalDiskUsed = 0;
    }

    /// <summary>
    /// Simulate additional or less disk space being available.
    /// </summary>
    public void SetDiskSize(int newDiskSizeMB)
    {
        _diskSize = newDiskSizeMB * 1024 * 1024;
    }

    public FakeFileStream Open(string path)
    {
        if (!_files.TryGetValue(path, out var _))
        {
            var file = Array.Empty<byte>();
            _files.Add(path, file);
        }

        return new FakeFileStream(this, path);
    }

    public void Delete(string path)
    {
        var file = _files[path];
        _totalDiskUsed -= file.Length;
        _files.Remove(path);
    }

    public void Move(string oldPath, string newPath)
    {
        _files[newPath] = _files[oldPath];
        _files.Remove(oldPath);
    }

    public bool Exists(string path)
    {
        return _files.ContainsKey(path);
    }

    public int GetLength(string path)
    {
        return _files[path].Length;
    }

    public void SetLength(string path, int length)
    {
        var file = _files[path];
        var originalSize = file.Length;
        Array.Resize(ref file, length);
        _files[path] = file;

        _totalDiskUsed += length - originalSize;
    }

    public int Read(string path, int position, byte[] buffer, int offset, int count)
    {
        var file = _files[path];
        if (position + count > file.Length)
        {
            count = file.Length - position;
        }

        Buffer.BlockCopy(file, position, buffer, offset, count);
        return count;
    }

    public void Write(string path, int position, byte[] buffer, int offset, int count)
    {
        if (_totalDiskUsed + count > _diskSize)
        {
            throw new IOException("No space left on device", 28); // Ubuntu exception thrown on low disk space

            // Windows version would be: new IOException($"There is not enough space on the disk : \'{path}\'", -2147024784)
        }

        _totalDiskUsed += count;

        var file = _files[path];
        if (file.Length < position + count)
        {
            Array.Resize(ref file, position + count);
            _files[path] = file;
        }

        Buffer.BlockCopy(buffer, offset, file, position, count);
    }
}

#pragma warning disable SA1402 // File may only contain a single type
internal class FakeFileStream : Stream
#pragma warning restore SA1402 // File may only contain a single type
{
    private readonly FakeFileSystem _fs;
    private readonly string _path;

    private long _position;

    public FakeFileStream(FakeFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => _fs.GetLength(_path);

    public override long Position
    {
        get { return _position; }
        set { _position = value; }
    }

    public override void Flush()
    {
        return;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var countRead = _fs.Read(_path, (int)_position, buffer, offset, count);
        _position += countRead;

        return countRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = offset;
        return _position;
    }

    public override void SetLength(long value)
    {
        throw new System.NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _fs.Write(_path, (int)_position, buffer, offset, count);
        _position += count;
    }
}

#pragma warning disable SA1402 // File may only contain a single type
internal class FakeFileSystemInteractor : IFileSystemInteractor
#pragma warning restore SA1402 // File may only contain a single type
{
    private readonly FakeFileSystem _fs;

    public FakeFileSystemInteractor(FakeFileSystem fs)
    {
        _fs = fs;
    }

    public void DeleteFile(string path)
    {
        _fs.Delete(path);
    }

    public bool FileExists(string path)
    {
        return _fs.Exists(path);
    }

    public void MoveFile(string oldPath, string newPath)
    {
        _fs.Move(oldPath, newPath);
    }

    public Stream OpenFile(string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.ReadWrite, int bufferSize = 4096, FileOptions options = FileOptions.None)
    {
        return _fs.Open(path);
    }

    public bool DirectoryExists(string path)
    {
        return true;
    }

    public void CreateDirectory(string path)
    {
        return;
    }

    public string[] GetFilesInDirectory(string path, string searchPattern)
    {
        // Return no files. At time of writing FakeFileSystem is not being used in a way that would require the logic to properly simulate GetFilesInDirectory when files exist.
        // If that is required, it would likely be best to change the _files dictonary to an _directories dictionary or create a tree to properly simulate directories and files.
        return Array.Empty<string>();
    }
}
