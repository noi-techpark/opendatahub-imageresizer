// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

﻿using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Gif;
using AspNetCore.Proxy;

namespace odh_imageresizer_core
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileController : ControllerBase
    {
        public IConfiguration Configuration { get; }

        private readonly IHttpClientFactory _httpClientFactory;

        public FileController(IWebHostEnvironment env, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            Configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        #region Documents

        [OutputCache(Duration = 100, PolicyName = "NoLocking")]
        [ResponseCache(Duration = 0)]
        [HttpGet, Route("GetFile/{filename}")]
        public async Task<IActionResult> GetFile(string filename, CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient("buckets");

                var uri = BucketFile.Resolve(client, filename);
                if (uri == null)
                    return BadRequest(BucketFile.NotInBucket);

                // Stream the file instead of downloading it completely into memory first
                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return BucketFile.Error(this, response);

                var mimeType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

                return await BucketFile.Stream(this, response, mimeType, cancellationToken);
            }
            catch(Exception ex)
            {
                return BadRequest(ex.Message + " " + ex.InnerException);
            }
        }

        #endregion
    }
}
