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
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Common.Constants;
using AdapterFramework.Data.Framework.Common.HttpCommunication;
using AdapterFramework.Data.Framework.Common.Security;
using AdapterFramework.Data.Framework.Compression;
using Newtonsoft.Json.Linq;

namespace AdapterFramework.Data.Framework.Tests.Helper;

public class TestEndpoint : IDisposable
{
    public const string ClientId = "abcde";
    public const string ClientSecret = "12345";
    public const string Token = "Token";
    private const string InvalidDataMessage = "Not a valid data message. Do NOT set CacheLastDataValue to true if you are sending invalid messages.";

    private readonly string _openIdTokenUrl;
    private readonly HttpListener _httpListener;
    private readonly IList<HttpRequestData> _requestList;
    private readonly IDictionary<string, string> _containerToLastValue;
    private readonly object _requestListLock;

    private int _openIdRouteGetCounter;
    private HttpStatusCode _responseCode;
    private Dictionary<string, string> _headers;
    private bool _cacheLastDataValue;
    private bool _disposed;
    private Action _messageReceived;

    public TestEndpoint(string uniformRecordLocator)
    {
        _requestListLock = new object();
        _responseCode = HttpStatusCode.OK;

        _httpListener = new HttpListener();

        _httpListener.Prefixes.Add(uniformRecordLocator);

        _openIdTokenUrl = uniformRecordLocator + Token;

        _requestList = new List<HttpRequestData>();
        _containerToLastValue = new Dictionary<string, string>();
    }

    public int OpenIdRouteGetCount => _openIdRouteGetCounter;

    public void SetMessageReceivedAction(Action callback)
    {
        _messageReceived = callback;
    }

    public void CacheLastDataValue(bool cache)
    {
        _cacheLastDataValue = cache;
    }

    public void AddPrefix(string prefix)
    {
        _httpListener.Prefixes.Add(prefix);
    }

    public void StartListening(string clientIdToCompare = ClientId)
    {
        _httpListener.Start();

        Task.Run(() =>
        {
            while (_httpListener.IsListening)
            {
                var context = _httpListener.GetContext();
                context.Response.StatusCode = (int)_responseCode;
                if (_headers != null)
                {
                    foreach (var (key, value) in _headers)
                    {
                        context.Response.AddHeader(key, value);
                    }
                }

                if (context.Request.RawUrl.Contains(Token, StringComparison.InvariantCulture))
                {
                    if (context.Request.HttpMethod == "POST")
                    {
                        var body = GetRequestBody(context.Request, GzipCompressionInHeaders(context.Request));

                        if (body.Contains($"client_id={clientIdToCompare}", StringComparison.InvariantCultureIgnoreCase) &&
                        body.Contains($"client_secret={ClientSecret}", StringComparison.InvariantCultureIgnoreCase))
                        {
                            var accessToken = new AccessToken()
                            {
                                ExpiryTime = 360,
                                TokenString = Token,
                                TokenType = "Type",
                            };

                            var buffer = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(accessToken, typeof(AccessToken)));
                            context.Response.ContentLength64 = buffer.Length;
                            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        }
                        else
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        }

                        context.Response.Close();
                    }

                    continue;
                }

                if (context.Request.HttpMethod == "POST")
                {
                    _messageReceived?.Invoke();
                    var body = GetRequestBody(context.Request, GzipCompressionInHeaders(context.Request));

                    lock (_requestListLock)
                    {
                        _requestList.Add(new HttpRequestData()
                        {
                            Body = body,
                            Headers = context.Request.Headers,
                        });
                    }

                    if (_cacheLastDataValue && context.Request.Headers[EndpointManagerConstants.MessageTypeHeaderKey] == "Data")
                    {
                        SaveLastDataValueForContainers(body);
                    }

                    context.Response.Close();
                }
                else if (context.Request.HttpMethod == "GET")
                {
                    if (context.Request.RawUrl == "/" + AccessTokenManagerConstants.BearAuthConfigLocationWithSlash)
                    {
                        Interlocked.Increment(ref _openIdRouteGetCounter);

                        var openIdResponse = new OpenIdResponse()
                        {
                            TokenEndpoint = _openIdTokenUrl,
                        };

                        var buffer = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(openIdResponse, typeof(OpenIdResponse)));
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.Close();
                        continue;
                    }

                    var data = GetLastValueForData(context.Request.RawUrl);

                    if (data != null)
                    {
                        var buffer = Encoding.UTF8.GetBytes(data);
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }

                    context.Response.Close();
                }
            }
        });
    }

    public void StopListening()
    {
        _httpListener.Stop();
    }

    public void SetHttpResponse(HttpStatusCode code, Dictionary<string, string> headers = null)
    {
        _responseCode = code;
        _headers = headers;
    }

    public bool VerifyMessageReceived(string message, IDictionary<string, string> headers = null)
    {
        HttpRequestData msg;
        lock (_requestListLock)
        {
            msg = _requestList.FirstOrDefault(req => req.Body == $"\"{message}\"" || req.Body == message);
        }

        if (msg == null) return false;

        if (headers == null) return true;

        var tempHeaders = new Dictionary<string, string>(headers);

        foreach (var kvp in tempHeaders)
        {
            if (!string.Equals(msg.Headers[kvp.Key], kvp.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public int NumberOfTimesMessageReceived(string message)
    {
        lock (_requestListLock)
        {
            return _requestList.Count(req => req.Body == $"\"{message}\"" || req.Body == message);
        }
    }

    public void ClearRequests()
    {
        lock (_requestListLock)
        {
            _requestList.Clear();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _httpListener.Close();
        }

        _disposed = true;
    }

    private static string GetRequestBody(HttpListenerRequest request, bool requiresGzipDecompression)
    {
        using var reader = new BinaryReader(request.InputStream);
        byte[] messageBytes = reader.ReadBytes((int)request.ContentLength64);

        if (requiresGzipDecompression)
        {
            messageBytes = new GZipCompressor().Decompress(messageBytes);
        }

        return Encoding.UTF8.GetString(messageBytes);
    }

    private static bool GzipCompressionInHeaders(HttpListenerRequest request)
    {
        if (request.Headers[EndpointManagerConstants.MessageCompressionHeaderKey] == null) return false;

        return string.Equals("gzip",
                   request.Headers[EndpointManagerConstants.MessageCompressionHeaderKey],
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStreamIdFromGetRawUrl(string rawUrl)
    {
        // can be simplified it we assume '/' is a restricted character
        int posFrom = rawUrl.IndexOf("/Streams/", StringComparison.Ordinal) + "/Streams/".Length;
        int posTo = rawUrl.IndexOf("/Data/GetLastValue", StringComparison.Ordinal);

        return rawUrl[posFrom..posTo];
    }

    private string GetLastValueForData(string rawUrl)
    {
        var streamId = GetStreamIdFromGetRawUrl(rawUrl);

        return _containerToLastValue.TryGetValue(streamId, out string value) ? value : null;
    }

    private void SaveLastDataValueForContainers(string body)
    {
        try
        {
            var dataArray = JArray.Parse(body);
            foreach (JObject element in dataArray)
            {
                if (element.ContainsKey("containerid"))
                {
                    // For simplicity, it assumes the last value for the container to have the most recent timestamp
                    _containerToLastValue[element["containerid"].Value<string>()] = element["values"].Last.ToString();
                }
            }
        }
        catch (Exception)
        {
            // happens when method called on a malformed data message
#pragma warning disable CA1303 // Do not pass literals as localized parameters
            Console.WriteLine(InvalidDataMessage);
#pragma warning restore CA1303 // Do not pass literals as localized parameters
        }
    }
}

#pragma warning disable SA1402 // File may only contain a single type
public class HttpRequestData
#pragma warning restore SA1402 // File may only contain a single type
{
    public NameValueCollection Headers { get; internal set; }

    public string Body { get; set; }
}
