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
using System.Globalization;
using AdapterFramework.Data.Framework.Abstractions.DataFilters;

namespace AdapterFramework.Data.Framework.MessageProcessor.DataFilters;

public class AbsoluteDeadbandDataFilter : IDataFilter
{
    public AbsoluteDeadbandDataFilter(TimeSpan? expirationPeriod, double filterValue)
    {
        ExpirationPeriod = expirationPeriod;
        FilterValue = filterValue;
    }

    public TimeSpan? ExpirationPeriod { get; }
    public double FilterValue { get; }

    public bool CheckDataValue<T>(T currentValue, T lastValue)
    {
        var sendCurrentData = false;

        switch (currentValue)
        {
            case double _:
                if (Math.Abs(Convert.ToDouble(currentValue, CultureInfo.InvariantCulture) - Convert.ToDouble(lastValue, CultureInfo.InvariantCulture)) > FilterValue)
                    sendCurrentData = true;
                break;

            case float _:
                if (Math.Abs(Convert.ToSingle(currentValue, CultureInfo.InvariantCulture) - Convert.ToSingle(lastValue, CultureInfo.InvariantCulture)) > FilterValue)
                    sendCurrentData = true;
                break;

            case int _:
                if (Math.Abs(Convert.ToInt32(currentValue, CultureInfo.InvariantCulture) - Convert.ToInt32(lastValue, CultureInfo.InvariantCulture)) > FilterValue)
                    sendCurrentData = true;
                break;

            case long _:
                if (Math.Abs(Convert.ToInt64(currentValue, CultureInfo.InvariantCulture) - Convert.ToInt64(lastValue, CultureInfo.InvariantCulture)) > FilterValue)
                    sendCurrentData = true;
                break;

            case short _:
                if (Math.Abs(Convert.ToInt16(currentValue, CultureInfo.InvariantCulture) - Convert.ToInt16(lastValue, CultureInfo.InvariantCulture)) > FilterValue)
                    sendCurrentData = true;
                break;

            case uint _:
                uint currentValAsUInt32 = Convert.ToUInt32(currentValue, CultureInfo.InvariantCulture);
                uint lastValAsUInt32 = Convert.ToUInt32(lastValue, CultureInfo.InvariantCulture);
                uint diffUInt32 = currentValAsUInt32 > lastValAsUInt32 ? currentValAsUInt32 - lastValAsUInt32 : lastValAsUInt32 - currentValAsUInt32;
                if (diffUInt32 > FilterValue)
                    sendCurrentData = true;
                break;

            case ulong _:
                ulong currentValAsUInt64 = Convert.ToUInt64(currentValue, CultureInfo.InvariantCulture);
                ulong lastValAsUInt64 = Convert.ToUInt64(lastValue, CultureInfo.InvariantCulture);
                ulong diffUInt64 = currentValAsUInt64 > lastValAsUInt64 ? currentValAsUInt64 - lastValAsUInt64 : lastValAsUInt64 - currentValAsUInt64;
                if (diffUInt64 > FilterValue)
                    sendCurrentData = true;
                break;

            case ushort _:
                ushort currentValAsUInt16 = Convert.ToUInt16(currentValue, CultureInfo.InvariantCulture);
                ushort lastValAsUInt16 = Convert.ToUInt16(lastValue, CultureInfo.InvariantCulture);
                int diffUInt16 = currentValAsUInt16 > lastValAsUInt16 ? currentValAsUInt16 - lastValAsUInt16 : lastValAsUInt16 - currentValAsUInt16;
                if (diffUInt16 > FilterValue)
                    sendCurrentData = true;
                break;

            case bool _:
                if (Convert.ToBoolean(currentValue, CultureInfo.InvariantCulture) != Convert.ToBoolean(lastValue, CultureInfo.InvariantCulture)) 
                    sendCurrentData = true;
                break;

            case string _:
                if (!currentValue.ToString().Equals(lastValue?.ToString(), StringComparison.InvariantCulture))
                    sendCurrentData = true;
                break;

            // these types are not supported by OMF Spec in the 'Supported Formats' table - do they need a case statement here?
            // case byte _: break;  
            // case sbyte _: break;
            // case char _: break;
            // case decimal _: break;
            default:
                // TODO: OMF spec allows type to be array or dictionary - for data that is not simple type we are doing object.Equals() check
                if (currentValue is null)
                {
                    if (lastValue != null)
                        sendCurrentData = true;
                }
                else if (lastValue is null)
                {
                    sendCurrentData = true;
                }
                else
                { 
                    if (!currentValue.Equals(lastValue))
                        sendCurrentData = true;
                }

                break;
        }

        return sendCurrentData;
    }
}
