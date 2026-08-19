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
namespace AdapterFramework.Data.DataModel;

/// <summary>
/// A class holding all the token constants.
/// </summary>
public static class Tokens
{
    #region Meta-data Tokens

    public const string Type = "type";
    public const string Format = "format";
    public const string Items = "items";
    public const string AdditionalProperties = "additionalProperties";
    public const string Id = "id";
    public const string Version = "version";
    public const string Name = "name";
    public const string Description = "description";
    public const string Classification = "classification";
    public const string Properties = "properties";
    public const string IsIndex = "isindex";
    public const string Tags = "tags";
    public const string Metadata = "metadata";
    public const string Indexes = "indexes";
    public const string IsName = "isname";
    public const string Uom = "uom";
    public const string Enum = "enum";
    public const string Extrapolation = "extrapolation";
    public const string Interpolation = "interpolation";
    public const string Minimum = "minimum";
    public const string Maximum = "maximum";
    public const string GZip = "gzip";
    public const string Json = "json";
    public const string DataSource = "datasource";
    public const string Quality = "quality";
    public const string QualitySchema = "qualityschema";
    public const string Value = "value";
    public const string IsQuality = "isquality";
    public const string BaseTypeId = "basetypeid";
    public const string StartTime = "starttime";
    public const string EndTime = "endtime";

    #endregion

    #region Classification Tokens

    public const string Static = "static";
    public const string Dynamic = "dynamic";
    public const string StreamingData = "streamingdata";
    public const string Event = "event";
    public const string Entity = "entity";

    #endregion

    #region Link and Container Tokens

    public const string TypeId = "typeid";
    public const string RefTypeId = "reftypeid";
    public const string ContainerId = "containerid";
    public const string TypeVersion = "typeversion";
    public const string Index = "index";
    public const string Source = "source";
    public const string Target = "target";
    public const string Root = "_ROOT";
    public const string Link = "__Link";
    public const string Values = "values";
    public const string PropertyOverrides = "propertyoverrides";
    public const string LinkProperty = "property";
    public const string LinkType = "type";
    public const string LinkLabel = "label";
    public const string LinkCollection = "collection";
    public const string ExtendedPropertiesDefinition = "extendedpropertiesdefinition";
    public const string TypesCollection = "types";
    public const string StreamsCollection = "streamingdata";
    public const string EventsCollection = "events";
    public const string EntitiesCollection = "entities";

    #endregion

    #region Property Type Tokens

    public const string ArrayToken = "array";
    public const string BooleanToken = "boolean";
    public const string IntegerToken = "integer";
    public const string NullToken = "null";
    public const string NumberToken = "number";
    public const string ObjectToken = "object";
    public const string StringToken = "string";

    #endregion

    #region Format Tokens

    public const string Int64Token = "int64";
    public const string Int32Token = "int32";
    public const string Int16Token = "int16";
    public const string UInt64Token = "uint64";
    public const string UInt32Token = "uint32";
    public const string UInt16Token = "uint16";
    public const string Float64Token = "float64";
    public const string Float32Token = "float32";
    public const string Float16Token = "float16";
    public const string DictionaryToken = "dictionary";
    public const string DateTimeToken = "date-time";

    #endregion
}
