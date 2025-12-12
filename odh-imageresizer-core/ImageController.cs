// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using AspNetCore.CacheOutput;
using AspNetCore.Proxy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System;
﻿using System.IO;
using System.Linq;
using System.Net.Http;
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

        public ImageController(IWebHostEnvironment env, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            Configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        #region ImageResizing

        [CacheOutput(ClientTimeSpan = 86400, ServerTimeSpan = 86400)]
        //[ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any, VaryByQueryKeys = new[] { "imageurl", "width", "height" })]
        [HttpGet, Route("GetImage")]
        public async Task<IActionResult> GetImage(string imageurl, int? width = null, int? height = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var img = await LoadImage(imageurl, cancellationToken);
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

                return File(stream, imgrawformat.DefaultMimeType);
            }
            catch(Exception ex)
            {
                return BadRequest(ex.Message + " " + ex.InnerException);
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

        private async Task<Image> LoadImage(string imageUrl, CancellationToken cancellationToken)
        {
            using var client = _httpClientFactory.CreateClient("buckets");
            using var stream = await client.GetStreamAsync(imageUrl, cancellationToken);           

            return await Image.LoadAsync(stream);  //LoadWithFormatAsync(stream);
        }

        #endregion

        #region ImageProxying

        //Proxy URL
        //Using nuget package https://github.com/twitchax/aspnetcore.proxy
        [HttpGet, Route("GetImageByUrl")]
        public Task GetImageByUrl(string imageurl)
        {
            return this.HttpProxyAsync($"{imageurl}");
        }

        #endregion
    }
}
