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
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Tests.Helper;

public class TestConfigurationEndpoint : IDisposable
{
    #region Private Properties

    private const HttpStatusCode DefaultGetResponseCode = HttpStatusCode.OK;
    private readonly object _requestListLock;
    private readonly HttpListener _httpListener;
    private HttpStatusCode _getResponseCode;
    private HttpStatusCode _setResponseCode;
    private int _getResponseCodeReturnCount;
    private bool _disposed;

    #endregion

    #region Public Constructor

    public TestConfigurationEndpoint(IEnumerable<string> uriStrings)
    {
        ThrowHelper.ThrowIfArgumentNull(uriStrings, nameof(uriStrings));

        _requestListLock = new object();
        _setResponseCode = HttpStatusCode.NoContent;
        _getResponseCode = DefaultGetResponseCode;
        _getResponseCodeReturnCount = 0;

        _httpListener = new HttpListener();

        foreach (var uri in uriStrings)
        {
            _httpListener.Prefixes.Add(uri);
        }

        RequestList = new List<HttpRequestData>();
    }

    #endregion

    private enum HttpOperation
    {
        GetOperation,
        SetResponse,
    }

    #region Public Properties

    public IList<HttpRequestData> RequestList { get; internal set; }

    public string ResponsePayload { get; set; } = "{\"Response\": \"Hello\"}";

    public int PatchOperationCounter { get; set; }

    public int GetOperationCounter { get; set; }

    public int PutOperationCounter { get; set; }

    public int DeleteOperationCounter { get; set; }

    public int PostOperationCounter { get; set; }

    #endregion

    #region Public Methods

    public void SetHttpSetOperationStatusCode(HttpStatusCode setStatusCode)
    {
        _setResponseCode = setStatusCode;
    }

    public void SetHttpGetOperationStatusCode(HttpStatusCode getStatusCode, int returnCount)
    {
        _getResponseCode = getStatusCode;
        _getResponseCodeReturnCount = returnCount;
    }

    public void StartListening()
    {
        _httpListener.Start();

        Task.Run(() =>
            {
                while (_httpListener.IsListening)
                {
                    var context = _httpListener.GetContext();

                    switch (context.Request.HttpMethod)
                    {
                        case "PATCH":
                            {
                                HandleSetRequest(context);
                                PatchOperationCounter++;
                                break;
                            }

                        case "POST":
                            {
                                HandleSetRequest(context);
                                PostOperationCounter++;
                                break;
                            }

                        case "PUT":
                            {
                                HandleSetRequest(context);
                                PutOperationCounter++;
                                break;
                            }

                        case "DELETE":
                            {
                                HandleSetRequest(context);
                                DeleteOperationCounter++;
                                break;
                            }

                        case "GET":
                            {
                                context.Response.StatusCode = (int)GetStatusCode(HttpOperation.GetOperation);
                                GetOperationCounter++;

                                var buffer = Encoding.UTF8.GetBytes(ResponsePayload);
                                context.Response.ContentLength64 = buffer.Length;
                                context.Response.OutputStream.Write(buffer, 0, buffer.Length);

                                context.Response.Close();
                                break;
                            }
                    }
                }
            });
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Protected Methods

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

    #endregion

    #region Private Methods

    private static string GetRequestBody(HttpListenerRequest request)
    {
        string requestBody;
        using (var receiveStream = request.InputStream)
        {
            using var readStream = new StreamReader(receiveStream, request.ContentEncoding);
            requestBody = readStream.ReadToEnd();
        }

        return requestBody;
    }

    private HttpStatusCode GetStatusCode(HttpOperation httpOperation)
    {
        if (httpOperation == HttpOperation.GetOperation)
        {
            if (_getResponseCodeReturnCount > 0)
            {
                _getResponseCodeReturnCount--;
                return _getResponseCode;
            }

            return DefaultGetResponseCode;
        }

        return _setResponseCode;
    }

    private void HandleSetRequest(HttpListenerContext context)
    {
        var body = GetRequestBody(context.Request);

        lock (_requestListLock)
        {
            RequestList.Add(new HttpRequestData
            {
                Body = body,
                Headers = context.Request.Headers,
            });
        }

        context.Response.StatusCode = (int)_setResponseCode;
        context.Response.Close();
    }

    #endregion
}
