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
using System.Reflection;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.DataFilters;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.MessageProcessor.Reflection;

namespace AdapterFramework.Data.Framework.MessageProcessor.DataFilters;

public class DataFilterCachedValues<T> : IDataFilterCache
{
    private readonly int _timestampIndex;
    private readonly int[] _valueIndexes;
    private readonly int[] _dataQualityIndexes;
    private readonly IDataFilter _dataQualityFilter;
    private T _previousValue;
    private T _lastValue;
    private T _currentValue;
    
    public DataFilterCachedValues()
    {
        _currentValue = default;
        _previousValue = default;
        _lastValue = default;

        var valueType = typeof(T);
        var properties = valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var valueIndexes = new List<int>();
        var qualityIndexes = new List<int>();
        for (var index = 0; index < properties.Length; index++)
        {
            if (Attribute.IsDefined(properties[index], typeof(TimestampAttribute)))
            {
                _timestampIndex = index;
            }
            else if (Attribute.IsDefined(properties[index], typeof(ValueAttribute)))
            {
                valueIndexes.Add(index);
            }
            else if (Attribute.IsDefined(properties[index], typeof(QualityAttribute)))
            {
                qualityIndexes.Add(index);
            }
        }

        _valueIndexes = [.. valueIndexes];
        _dataQualityIndexes = [.. qualityIndexes];
        if (_dataQualityIndexes.Length > 0)
        {
            _dataQualityFilter = new AbsoluteDeadbandDataFilter(null, 0);
        }
    }

    public T GetPreviousValue() => _previousValue;

    public void SetPreviousValue(T instance) => _previousValue = instance;

    public void SetCurrentValue(T instance) => _currentValue = instance;

    public bool CheckDataFilter(IDataFilter dataFilter, out bool sendPrevious)
    {
        ThrowHelper.ThrowIfArgumentNull(dataFilter, nameof(dataFilter));

        sendPrevious = false;

        if (_lastValue == null)
        {
            _previousValue = _currentValue;
            _lastValue = _currentValue;
            return true; // First Data Value
        }

        var propertyValueGetters = PropertyValueGetter.GetPropertyValueGetters(typeof(T));

        var currentTimestamp = (DateTime)propertyValueGetters[_timestampIndex].GetValue(_currentValue);
        var previousTimestamp = (DateTime)propertyValueGetters[_timestampIndex].GetValue(_previousValue);

        if (currentTimestamp < previousTimestamp)
        {
            return true; // Out Of Order Data
        }

        var expirationPeriodElapsed = false;
        var lastTimestamp = (DateTime)propertyValueGetters[_timestampIndex].GetValue(_lastValue);

        if (dataFilter.ExpirationPeriod != null)
        {
            if (currentTimestamp - lastTimestamp >= dataFilter.ExpirationPeriod)
            {
                expirationPeriodElapsed = true; // Expiration Period Elapsed
            }
        }

        var sendCurrentData = false;

        // Data Quality check 
        for (var index = 0; index < _dataQualityIndexes.Length && !sendCurrentData; index++)
        {
            sendCurrentData = _dataQualityFilter.CheckDataValue(
                propertyValueGetters[_dataQualityIndexes[index]].GetValue(_currentValue), 
                propertyValueGetters[_dataQualityIndexes[index]].GetValue(_lastValue));
        }

        for (var index = 0; !sendCurrentData && index < _valueIndexes.Length; index++)
        {
            sendCurrentData = dataFilter.CheckDataValue(
                propertyValueGetters[_valueIndexes[index]].GetValue(_currentValue),
                propertyValueGetters[_valueIndexes[index]].GetValue(_lastValue));
        }

        if (sendCurrentData || expirationPeriodElapsed)
        {
            // if sending data because expirationPeriod elapsed, but current value is not outside filter range then do not send previous
            // if sending current data because outside filter range, then run check for previous
            if (sendCurrentData)
            {
                if (previousTimestamp > lastTimestamp)
                {
                    sendPrevious = true;
                }
            }

            _lastValue = _currentValue;
            sendCurrentData = true;
        }

        if (sendPrevious == false)
        {
            _previousValue = _currentValue;
        }

        return sendCurrentData;
    }
}
