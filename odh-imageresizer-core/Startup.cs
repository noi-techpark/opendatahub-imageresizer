// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using AspNetCore.CacheOutput;
using AspNetCore.CacheOutput.InMemory;
using AspNetCore.CacheOutput.InMemory.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
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
            services.AddInMemoryCacheOutput();

            //services.AddInMemoryCacheOutput(config =>
            //{
            //    config.ExcludeHeadersFromCacheKey.Add("Content-Length");
            //    config.ExcludeHeadersFromCacheKey.Add("Content-Disposition");
            //});

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
                    string bucketurl = Configuration["S3BucketUrl"] ?? throw new InvalidProgramException("No S3 Bucket URL provided.");
                    c.BaseAddress = new Uri(bucketurl);
                })
                .AddPolicyHandler(retryPolicy)
                .AddPolicyHandler(timeoutPolicy);
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

            //app.UseCacheOutput();

            //app.UseResponseCompression();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
