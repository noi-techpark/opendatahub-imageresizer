// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

﻿using Microsoft.AspNetCore.Mvc;
using AspNetCore.Proxy;
using AspNetCore.Proxy.Options;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using System.Net.Http.Headers;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace odh_imageresizer_core
{
    [Route("api")]
    [ApiController]
    public class ODHProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public ODHProxyController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet, Route("ODHProxy/{*url}")]
        public Task GetODHProxy(string url)
        {
            try
            {
                var parameter = "?";

                foreach (var paramdict in HttpContext.Request.Query)
                {
                    parameter = parameter + paramdict.Key + "=" + paramdict.Value;
                }

                var fullurl = url + parameter;

                Console.WriteLine("Url to proxy: " + fullurl);

                return this.HttpProxyAsync(fullurl, ProxyOptions());
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        [OutputCache(Duration = 14400, PolicyName = "NoLocking")]
        [HttpGet, Route("ODHProxyCached/{*url}")]
        public Task GetODHProxyCached(string url)
        {
            try
            {
                var parameter = "?";

                foreach (var paramdict in HttpContext.Request.Query)
                {
                    parameter = parameter + paramdict.Key + "=" + paramdict.Value;
                }

                var fullurl = url + parameter;

                // Cached on the server only, clients have to revalidate
                return this.HttpProxyAsync(fullurl, WithClientCache(0));
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        [HttpGet, Route("ODHProxyCachedonClient/{*url}")]
        public Task GetODHProxyCachedonClient(string url)
        {
            try
            {
                var parameter = "?";

                foreach (var paramdict in HttpContext.Request.Query)
                {
                    parameter = parameter + paramdict.Key + "=" + paramdict.Value;
                }

                var fullurl = url + parameter;

                return this.HttpProxyAsync(fullurl, WithClientCache(14400));
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        // The proxy copies the upstream headers, so the client cache header is set on the upstream response
        private static HttpProxyOptions WithClientCache(int seconds) =>
            ProxyOptionsBuilder()
                .WithAfterReceive((context, response) =>
                {
                    response.Headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromSeconds(seconds) };
                    return Task.CompletedTask;
                })
                .Build();

        public static HttpProxyOptions ProxyOptions() => ProxyOptionsBuilder().Build();

        // Answers failures with a short message, the default of the proxy library sends the whole stack trace
        private static IHttpProxyOptionsBuilder ProxyOptionsBuilder() =>
            HttpProxyOptionsBuilder.Instance
                .WithHandleFailure(async (context, ex) =>
                {
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    await context.Response.WriteAsync("Request could not be proxied. " + ex.Message);
                });


        [OutputCache(Duration = 14400, PolicyName = "NoLocking")]
        [ResponseCache(Duration = 14400)]
        [HttpGet, Route("ODHProxyCustomCached/{contenttype}/{*url}")]
        public async Task<IActionResult> GetODHProxyCustomCached(string contenttype, string url)
        {
            var parameter = "?";

            foreach (var paramdict in HttpContext.Request.Query)
            {
                parameter = parameter + paramdict.Key + "=" + paramdict.Value;
            }

            var fullurl = url + parameter;

            var _client = _httpClientFactory.CreateClient("customproxy");
            
            var request = HttpContext.CreateProxyHttpRequest(new Uri(fullurl));
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            await HttpContext.CopyProxyHttpResponse(response, contenttype);
            // The response is already written, so the result must not touch it anymore
            return new EmptyResult();
        }

        [ResponseCache(Duration = 14400)]
        [HttpGet, Route("ODHProxyCustomCachedonClient/{contenttype}/{*url}")]
        public async Task<IActionResult> GetODHProxyCustomCachedonClient(string contenttype, string url)
        {
            var parameter = "?";

            foreach (var paramdict in HttpContext.Request.Query)
            {
                parameter = parameter + paramdict.Key + "=" + paramdict.Value;
            }

            var fullurl = url + parameter;

            var _client = _httpClientFactory.CreateClient("customproxy");

            var request = HttpContext.CreateProxyHttpRequest(new Uri(fullurl));
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            await HttpContext.CopyProxyHttpResponse(response, contenttype);
            // The response is already written, so the result must not touch it anymore
            return new EmptyResult();
        }

        [OutputCache(Duration = 14400, PolicyName = "NoLocking")]
        [ResponseCache(Duration = 0)]
        [HttpGet, Route("ODHProxyCustomCachedonServer/{contenttype}/{*url}")]
        public async Task<IActionResult> GetODHProxyCustomCachedonServer(string contenttype, string url)
        {
            var parameter = "?";

            foreach (var paramdict in HttpContext.Request.Query)
            {
                parameter = parameter + paramdict.Key + "=" + paramdict.Value;
            }

            var fullurl = url + parameter;

            var _client = _httpClientFactory.CreateClient("customproxy");

            var request = HttpContext.CreateProxyHttpRequest(new Uri(fullurl));
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            await HttpContext.CopyProxyHttpResponse(response, contenttype);
            // The response is already written, so the result must not touch it anymore
            return new EmptyResult();
        }
    }


    public static class MyHttpContextExtensions
    {
        public static HttpRequestMessage CreateProxyHttpRequest(this HttpContext context, Uri uri)
        {
            var request = context.Request;

            var requestMessage = new HttpRequestMessage();
            var requestMethod = request.Method;
            if (!HttpMethods.IsGet(requestMethod) &&
                !HttpMethods.IsHead(requestMethod) &&
                !HttpMethods.IsDelete(requestMethod) &&
                !HttpMethods.IsTrace(requestMethod))
            {
                var streamContent = new StreamContent(request.Body);
                requestMessage.Content = streamContent;
            }

            // Copy the request headers
            foreach (var header in request.Headers)
            {
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
                {
                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            //Remove the Accept-Encoding Header
            requestMessage.Headers.Remove("Accept-Encoding");

            requestMessage.Headers.Host = uri.Authority;
            requestMessage.RequestUri = uri;
            requestMessage.Method = new HttpMethod(request.Method);

            return requestMessage;
        }

        public static async Task CopyProxyHttpResponse(this HttpContext context, HttpResponseMessage responseMessage, string? usecustomcontenttype = "")
        {
            if (responseMessage == null)
            {
                throw new ArgumentNullException(nameof(responseMessage));
            }

            var response = context.Response;

            // Keep our own Cache-Control (set by [ResponseCache]) instead of the upstream one
            var ownCacheControl = response.Headers.CacheControl;

            response.StatusCode = (int)responseMessage.StatusCode;

            
            foreach (var header in responseMessage.Headers)
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in responseMessage.Content.Headers)
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }

            // SendAsync removes chunking from the response. This removes the header so it doesn't expect a chunked response.
            response.Headers.Remove("Transfer-Encoding");
            response.Headers.Remove("Connection");
            response.Headers.Remove("Cache-Control");
            response.Headers.Remove("Pragma");
            response.Headers.Remove("Expires");
            response.Headers.Remove("Vary");
            response.Headers.Remove("set-cookie");
            response.Headers.Remove("CF-Cache-Status");
            response.Headers.Remove("Expect-CT");
            response.Headers.Remove("Server");
            response.Headers.Remove("CF-RAY");
            response.Headers.Remove("Date");
            response.Headers.Remove("Content-Encoding");

            if (!string.IsNullOrEmpty(ownCacheControl))
                response.Headers.CacheControl = ownCacheControl;


            //https://localhost:44373/api/ODHProxyCustomCached/https://www.dolomitisuperski.com/file/?uuidLift=01e04d73-8ab9-44b8-9b37-d693d4ddf49f

            var contenttypetouse = GetCorrectContentType(usecustomcontenttype);

            //"application/vnd.google-earth.kml+xml"
            if (!string.IsNullOrEmpty(contenttypetouse) && contenttypetouse != "nochange")
            {
                response.Headers.Remove("content-type");
                response.Headers.Add("content-type", contenttypetouse);
            }                

            using (var responseStream = await responseMessage.Content.ReadAsStreamAsync())
            {
                await responseStream.CopyToAsync(response.Body, 4096, context.RequestAborted);
            }
        }


        private static string GetCorrectContentType(string contenttype) =>        
            contenttype switch
            {
                "kml" => "application/vnd.google-earth.kml+xml",
                _ => "nochange"
            };
        

    }
}
