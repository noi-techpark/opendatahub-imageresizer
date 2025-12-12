using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
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

        public async Task InvokeAsync(HttpContext context)
        {
            var originalBodyStream = context.Response.Body;

            using (var memoryStream = new MemoryStream())
            {
                context.Response.Body = memoryStream;

                await _next(context);

                // Check if this is an image response from our GetImage endpoint
                if (context.Request.Path.StartsWithSegments("/api/Image/GetImage")
                    && context.Response.ContentType?.StartsWith("image/") == true)
                {
                    var imageUrl = context.Request.Query["imageurl"].ToString();
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        context.Response.Headers["Content-Disposition"] = $"inline; filename=\"{imageUrl}\"";
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
                context.Response.ContentLength = memoryStream.Length;

                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalBodyStream);

            }
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
