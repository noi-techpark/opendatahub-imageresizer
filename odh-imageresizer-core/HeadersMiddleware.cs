// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace odh_imageresizer_core
{
    public class HeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public HeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            // Headers are added when the response starts, so the body is streamed and not buffered in memory
            // (Content-Length is set by the file result / output cache / proxied upstream response)
            context.Response.OnStarting(() =>
            {
                // Errors must not be cached by clients (e.g. a missing image that is uploaded later)
                if (context.Response.StatusCode >= 400)
                    context.Response.Headers.Remove("Cache-Control");

                // Check if this is an image response from our GetImage endpoint
                if (context.Request.Path.StartsWithSegments("/api/Image/GetImage")
                    && context.Response.ContentType?.StartsWith("image/") == true)
                {
                    var imageUrl = context.Request.Query["imageurl"].ToString();
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        context.Response.Headers["Content-Disposition"] = ContentDisposition(imageUrl);
                    }
                }
                else if (context.Request.Path.StartsWithSegments("/api/File/GetFile"))
                {
                    //To check which Content-Diposition on which file type
                    //var filename = context.Request.Query["filename"].ToString();
                    //if (!string.IsNullOrEmpty(filename))
                    //{
                    //    context.Response.Headers["Content-Disposition"] = $"inline; filename=\"{filename}\"";
                    //}
                }

                return Task.CompletedTask;
            });

            return _next(context);
        }

        // Header values must be ASCII, so names with e.g. umlauts are sent encoded as filename* (RFC 6266),
        // with an ASCII fallback for old clients
        private static string ContentDisposition(string fileName)
        {
            if (fileName.All(c => c >= 0x20 && c < 0x7F))
                return $"inline; filename=\"{fileName}\"";

            var fallback = new string(fileName.Select(c => c >= 0x20 && c < 0x7F ? c : '_').ToArray());
            return $"inline; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
        }
    }

    public static class HeadersMiddlewareExtensions
    {
        public static IApplicationBuilder UseImageHeadersMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<HeadersMiddleware>();
        }
    }
}
