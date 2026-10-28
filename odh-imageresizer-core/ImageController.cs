// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using AspNetCore.Proxy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System;
﻿using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace odh_imageresizer_core
{
    [Route("api/[controller]")]
    [ApiController]
    public class ImageController : ControllerBase
    {
        public IConfiguration Configuration { get; }

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ResizeLimiter _resizeLimiter;

        private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

        // Only formats ImageSharp can read are sent unchanged (as before, when every image was decoded).
        // Others like SVG, which can contain scripts, go through decoding and are rejected.
        private static readonly HashSet<string> SupportedContentTypes = SixLabors.ImageSharp.Configuration.Default.ImageFormats
            .SelectMany(f => f.MimeTypes)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public ImageController(IWebHostEnvironment env, IConfiguration configuration, IHttpClientFactory httpClientFactory, ResizeLimiter resizeLimiter)
        {
            Configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _resizeLimiter = resizeLimiter;
        }

        #region ImageResizing

        [OutputCache(Duration = 86400, PolicyName = "NoLocking")]
        [ResponseCache(Duration = 86400)]
        [HttpGet, Route("GetImage")]
        public async Task<IActionResult> GetImage(string imageurl, int? width = null, int? height = null, CancellationToken cancellationToken = default)
        {
            var acquired = false;
            try
            {
                // Nothing to resize, so send the original file without decoding it
                // (if the content type is unknown, it continues below and ImageSharp detects the format)
                if (width == null && height == null)
                {
                    var original = await GetOriginalImage(imageurl, cancellationToken);
                    if (original != null)
                        return original;
                }

                // Limit parallel resizes, every running one holds a decoded image in memory
                await _resizeLimiter.WaitAsync(cancellationToken);
                acquired = true;

                var img = await LoadImage(imageurl, width, height, cancellationToken);
                using var _ = img; // Lazy way to dispose the image resource ;)

                if (width != null || height != null)
                {
                    (int w, int h) = (width ?? 0, height ?? 0);
                    float ratio = (float)img.Width / img.Height;
                    var size = (w > h)
                        ? new Size(w, (int)(w / ratio))
                        : new Size((int)(h * ratio), h);

                    img.Mutate(ctx =>
                    {
                        ctx.Resize(new ResizeOptions
                        {
                            Mode = ResizeMode.Max,
                            Size = size
                        });
                    });
                }
                var imgrawformat = img.Metadata.DecodedImageFormat;
                if (imgrawformat == null)
                    throw new Exception("Imageformat detection failed");

                var stream = await ImageToStream(img, imgrawformat, cancellationToken);

                // Extract filename from URL or use a default
                //string fileName = imageurl;
                //if (string.IsNullOrEmpty(imageurl))
                //    fileName = "image" + imgrawformat.FileExtensions.FirstOrDefault();

                // Convert stream to byte array so Content-Length can be set
                
                //byte[] imageBytes = stream.ToArray();

                //using (var ms = new MemoryStream())
                //{
                //    await stream.CopyToAsync(ms, cancellationToken);
                //    imageBytes = ms.ToArray();
                //}

                // Set headers BEFORE creating the result
                //Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
                //Response.Headers["Content-Length"] = imageBytes.Length.ToString();

                // ETag from the content, so clients can revalidate (304) and it stays the same across restarts
                var hash = SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length));
                var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(hash, 0, 16)}\"");

                return File(stream, imgrawformat.DefaultMimeType, null, etag);
            }
            catch (HttpRequestException ex) when (BucketFile.IsNotFound(ex.StatusCode))
            {
                return NotFound();
            }
            catch(Exception ex)
            {
                return BadRequest(ex.Message + " " + ex.InnerException);
            }
            finally
            {
                if (acquired)
                    _resizeLimiter.Release();
            }
        }

        //Test Method upload to S3 Bucket

        private async Task<MemoryStream> ImageToStream(Image imageIn, IImageFormat imgformat, CancellationToken cancellationToken = default)
        {            
            IImageEncoder encoder = ConfigureImageEncoder(imgformat);

            var ms = new MemoryStream();
            await imageIn.SaveAsync(ms, encoder, cancellationToken);
            ms.Position = 0;
            return ms;
        }

        private static IImageEncoder ConfigureImageEncoder(IImageFormat imgformat)
        {
            var mngr = SixLabors.ImageSharp.Configuration.Default.ImageFormatsManager;
            var encoder = mngr.GetEncoder(imgformat);
            if (encoder is JpegEncoder jpegEncoder)
            {
                //jpegEncoder.Quality = 90;
            }
            else if (encoder is PngEncoder pngEncoder)
            { }
            else if (encoder is GifEncoder gifEncoder)
            { }

            return encoder;
        }

        private async Task<IActionResult?> GetOriginalImage(string imageUrl, CancellationToken cancellationToken)
        {
            using var client = _httpClientFactory.CreateClient("buckets");

            var uri = BucketFile.Resolve(client, imageUrl);
            if (uri == null)
                return BadRequest(BucketFile.NotInBucket);

            // Stream the file instead of downloading it completely into memory first
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return BucketFile.Error(this, response);

            // Use the content type of the bucket, if it is not a supported one the one of the file extension
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == null || !SupportedContentTypes.Contains(contentType))
            {
                if (!ContentTypeProvider.TryGetContentType(imageUrl, out contentType) || !SupportedContentTypes.Contains(contentType))
                {
                    response.Dispose();
                    return null;
                }
            }

            return await BucketFile.Stream(this, response, contentType, cancellationToken);
        }

        private async Task<Image> LoadImage(string imageUrl, int? width, int? height, CancellationToken cancellationToken)
        {
            using var client = _httpClientFactory.CreateClient("buckets");
            var uri = BucketFile.Resolve(client, imageUrl) ?? throw new ArgumentException(BucketFile.NotInBucket);
            using var stream = await client.GetStreamAsync(uri, cancellationToken);

            var options = new DecoderOptions();
            if (width != null || height != null)
            {
                // Scale down while decoding (JPEG decodes directly at a reduced size), so the full
                // resolution image is never held in memory. A 0 dimension keeps the aspect ratio.
                (int w, int h) = (width ?? 0, height ?? 0);
                options = new DecoderOptions
                {
                    TargetSize = (w > h) ? new Size(w, 0) : new Size(0, h),
                    Sampler = KnownResamplers.Bicubic // same as Resize, the decoder default is Box
                };
            }

            return await Image.LoadAsync(options, stream, cancellationToken);
        }

        #endregion

        #region ImageProxying

        //Proxy URL
        //Using nuget package https://github.com/twitchax/aspnetcore.proxy
        [HttpGet, Route("GetImageByUrl")]
        public Task GetImageByUrl(string imageurl)
        {
            return this.HttpProxyAsync($"{imageurl}", ODHProxyController.ProxyOptions());
        }

        #endregion
    }

    public class ResizeLimiter
    {
        private readonly SemaphoreSlim _semaphore;

        public ResizeLimiter(int maxConcurrent)
        {
            _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }

        public Task WaitAsync(CancellationToken cancellationToken) => _semaphore.WaitAsync(cancellationToken);

        public void Release() => _semaphore.Release();
    }
}
