// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using SixLabors.ImageSharp.Memory;
using System;
using System.Net.Http;

namespace odh_imageresizer_core
{
    public class Startup
    {
        public Startup(IWebHostEnvironment env, IConfiguration configuration)
        {
            Configuration = configuration;
            CurrentEnvironment = env;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment CurrentEnvironment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // Server side cache with a hard size limit, so cached responses can't grow until the pod is OOM killed
            long cacheSizeLimitMB = long.TryParse(Configuration["OutputCacheSizeLimitMB"], out var mb) ? mb : 512;
            services.AddOutputCache(options =>
            {
                options.SizeLimit = cacheSizeLimitMB * 1024 * 1024;
                // Bigger responses (e.g. proxied documents) are not cached, so they don't evict lots of images
                options.MaximumBodySize = 16 * 1024 * 1024;
                // With locking, parallel requests waiting on a response too big to cache get an empty body
                options.AddPolicy("NoLocking", b => b.SetLocking(false));
            });

            // ImageSharp: keep only a small buffer pool between requests and reject images too big to decode safely
            SixLabors.ImageSharp.Configuration.Default.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
            {
                MaximumPoolSizeMegabytes = 64,
                AllocationLimitMegabytes = 256
            });

            // Every running resize holds a decoded image in memory, so only a few run in parallel
            int maxConcurrentResizes = int.TryParse(Configuration["MaxConcurrentResizes"], out var n) && n > 0 ? n : 4;
            services.AddSingleton(new ResizeLimiter(maxConcurrentResizes));

            //services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Optimal);
            //services.AddResponseCompression(options =>
            //{
            //    options.EnableForHttps = true;
            //    options.Providers.Add<GzipCompressionProvider>();
            //});

            services.AddMvc();

            var retryPolicy = HttpPolicyExtensions.HandleTransientHttpError()
                .Or<TimeoutRejectedException>()
                .RetryAsync();

            var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(10);

            services.AddCors(o => o.AddPolicy("CorsPolicy", builder =>
            {
                builder.AllowAnyOrigin()
                       .AllowAnyMethod()
                       .AllowAnyHeader();
            }));

            //services.AddMemoryCache();
            services.AddResponseCaching();

            services.AddHttpClient("buckets", c =>
                {
                    string bucketurl = Configuration["S3BucketUrl"] ?? Configuration["ODH_IMAGERESIZERCORE_S3"] ?? throw new InvalidProgramException("No S3 Bucket URL provided.");
                    c.BaseAddress = new Uri(bucketurl);
                })
                .AddPolicyHandler(retryPolicy)
                .AddPolicyHandler(timeoutPolicy);

            // Clients of the proxy endpoints, they must not reach internal services
            services.AddHttpClient("AspNetCore.Proxy.HttpProxyClient")
                .ConfigurePrimaryHttpMessageHandler(() => InternalAddressGuard.CreateHandler(allowAutoRedirect: true));
            services.AddHttpClient("customproxy")
                .ConfigurePrimaryHttpMessageHandler(() => InternalAddressGuard.CreateHandler(allowAutoRedirect: false));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Add response caching middleware
            //app.UseResponseCaching();

            //app.UseResponseCompression();            

            app.UseImageHeadersMiddleware(); // Add this BEFORE UseRouting or AFTER UseResponseCaching

            app.UseRouting();

            app.UseCors("CorsPolicy");

            app.UseOutputCache();

            //app.UseResponseCompression();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
