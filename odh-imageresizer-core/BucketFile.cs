// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace odh_imageresizer_core
{
    public static class BucketFile
    {
        // Resolves the path against the bucket URL. Returns null if it points somewhere else,
        // e.g. an absolute URL to another host, which must not be fetched from the server (SSRF).
        public static Uri? Resolve(HttpClient client, string path)
        {
            var bucket = client.BaseAddress!;
            if (!Uri.TryCreate(bucket, path, out var uri))
                return null;

            return uri.Scheme == bucket.Scheme && uri.Authority == bucket.Authority && uri.AbsolutePath.StartsWith(bucket.AbsolutePath)
                ? uri
                : null;
        }

        public const string NotInBucket = "Only files of the bucket can be requested";

        // S3 answers 403 instead of 404 for missing files, because listing the bucket is not allowed
        public static bool IsNotFound(HttpStatusCode? statusCode) =>
            statusCode == HttpStatusCode.NotFound || statusCode == HttpStatusCode.Forbidden;

        public static IActionResult Error(ControllerBase controller, HttpResponseMessage response)
        {
            var statusCode = response.StatusCode;
            response.Dispose();

            return IsNotFound(statusCode)
                ? controller.NotFound()
                : controller.StatusCode((int)statusCode);
        }

        // Streams the file with its length, ETag and Last-Modified, so clients can revalidate it (304)
        public static async Task<IActionResult> Stream(ControllerBase controller, HttpResponseMessage response, string contentType, CancellationToken cancellationToken)
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // The upstream stream is not seekable, so pass on its length (otherwise the response is chunked)
            controller.Response.ContentLength = response.Content.Headers.ContentLength;

            var etag = response.Headers.ETag;
            return new FileStreamResult(stream, contentType)
            {
                EntityTag = etag != null ? new EntityTagHeaderValue(etag.Tag, etag.IsWeak) : null,
                LastModified = response.Content.Headers.LastModified
            };
        }
    }
}
