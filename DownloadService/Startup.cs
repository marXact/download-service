using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using IdentityServer4.AccessTokenValidation;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using UNISharedModels.Request;

namespace DownloadService
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddAuthentication(IdentityServerAuthenticationDefaults.AuthenticationScheme)
                .AddIdentityServerAuthentication(options =>
                {
                    string identityServerUrl = System.Environment.GetEnvironmentVariable("IdentityServerUrl");
                    if (string.IsNullOrEmpty(identityServerUrl))
                    {
                        identityServerUrl = Configuration["IdentityServer:Url"];
                    }
                    // base-address of your identityserver
                    options.Authority = identityServerUrl;

                    string apiName = System.Environment.GetEnvironmentVariable("APIName");
                    if (string.IsNullOrEmpty(apiName))
                    {
                        apiName = Configuration["IdentityServer:APIName"];
                    }
                    // name of the API resource
                    options.ApiName = apiName;

                    string apiSecret = System.Environment.GetEnvironmentVariable("APISecret");
                    if (string.IsNullOrEmpty(apiSecret))
                    {
                        apiSecret = Configuration["IdentityServer:APISecret"];
                    }
                    options.ApiSecret = apiSecret;

                    options.EnableCaching = true;
                    options.CacheDuration = TimeSpan.FromMinutes(10); // that's the default

                    string requireHttpsMetadata = System.Environment.GetEnvironmentVariable("RequireHttpsMetadata");
                    if (string.IsNullOrEmpty(requireHttpsMetadata))
                    {
                        requireHttpsMetadata = Configuration["IdentityServer:RequireHttpsMetadata"];
                    }
                    options.RequireHttpsMetadata = Convert.ToBoolean(requireHttpsMetadata);
                });

            // RabbitMQ
            string rabbitMQHost = Configuration["RabbitMQ:Uri"];
            string rabbitMQUsername = Configuration["RabbitMQ:Username"];
            string rabbitMQPassword = Configuration["RabbitMQ:Password"];

            services.AddMassTransit(x =>
            {

                x.SetKebabCaseEndpointNameFormatter();
                x.UsingRabbitMq((context, settings) =>
                {
                    settings.ConfigureJsonSerializerOptions(settings => { settings.IncludeFields = true; return settings; });

                    settings.Host(rabbitMQHost, connection =>
                    {
                        connection.Username(rabbitMQUsername);
                        connection.Password(rabbitMQPassword);
                    });

                    settings.ConfigureEndpoints(context);
                });
                x.AddRequestClient<RequestRetrieveGeoXact>();
            });

            services.AddAuthorization(options =>
            {
                // Role Policies
                options.AddPolicy("UNICloudAdmin", policy =>
                {
                    policy.RequireClaim("role", "UNICloudAdministrator");
                });
                options.AddPolicy("UNISupport", policy =>
                {
                    policy.RequireClaim("role", "UNISupport");
                });
                options.AddPolicy("Admin", policy =>
                {
                    policy.RequireClaim("role", "Admin");
                });
                options.AddPolicy("Manager", policy =>
                {
                    policy.RequireClaim("role", "Manager");
                });
                options.AddPolicy("User", policy =>
                {
                    policy.RequireClaim("role", "User");
                });
                options.AddPolicy("FreeUser", policy =>
                {
                    policy.RequireClaim("role", "FreeUser");
                });
                options.AddPolicy("Device", policy =>
                {
                    policy.RequireClaim("role", "Device");
                });
                options.AddPolicy("ReadOnly", policy =>
                {
                    policy.RequireClaim("role", "ReadOnly");
                });
                // KlicService Policies
                options.AddPolicy("KlicService", policy =>
                {
                    policy.RequireClaim("scope", "KlicService");
                });
            });

            services.AddHealthChecks();

            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Optimal;
            });

            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<GzipCompressionProvider>();
            });

            services.AddResponseCaching();

            services.AddControllers()
                .AddNewtonsoftJson(options => {
                    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                    options.SerializerSettings.DateFormatString = "yyyy-MM-ddTHH:mm:ssZ";
                    options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Local;
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseCors(builder => builder
               .AllowAnyHeader()
               .AllowAnyMethod()
               .AllowAnyOrigin()
            );

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseResponseCompression();
            app.UseResponseCaching();

            app.Use(async (context, next) =>
            {
                context.Response.Headers.Add("Referrer-Policy", "no-referrer");
                await next.Invoke();
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/hc");
            });
        }
    }
}
