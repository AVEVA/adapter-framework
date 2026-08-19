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
using System.Collections;
using System.Collections.Generic;

namespace AdapterFramework.Data.Framework.Common.Scheduling;

public partial class Scheduler
{
    private class JobCollection : IEnumerable<Job>, IDisposable
    {
        private readonly Dictionary<string, Job> _jobs = new Dictionary<string, Job>();
        private bool _disposed;
        public event EventHandler Changed;

        public void Add(Job job)
        {
            _jobs.Add(job.Id, job);
            Changed?.Invoke(null, null);
        }

        public void Remove(Job job)
        {
            _jobs.Remove(job.Id);
            Changed?.Invoke(null, null);
        }

        public bool Contains(Job job) => _jobs.ContainsKey(job.Id);

        public IEnumerator<Job> GetEnumerator() => _jobs.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            
            foreach (var job in _jobs.Values)
            {
                job.Dispose();
            }

            _disposed = true;
        }
    }
}
