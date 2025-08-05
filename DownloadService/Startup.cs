using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using AuthContracts.Requests;
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
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using UniAuthHelper.Helpers;
using UniAuthHelper.Services;
using UniAuthHelper.Services.Interfaces;
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
            // Disable the OpenIddict default claim type mapping for JWT tokens
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
            
            // OpenIddict
            string identityServerUrl = Environment.GetEnvironmentVariable("IdentityServerUrl");
            string apiName = Environment.GetEnvironmentVariable("APIName");
            string requireHttpsMetadata = Environment.GetEnvironmentVariable("RequireHttpsMetadata");
            if (
                string.IsNullOrEmpty(identityServerUrl) ||
                string.IsNullOrEmpty(apiName) ||
                string.IsNullOrEmpty(requireHttpsMetadata)
            ) {
                identityServerUrl = GetConfig("IdentityServer:Url");
                apiName = GetConfig("IdentityServer:APIName");
                requireHttpsMetadata = GetConfig("IdentityServer:RequireHttpsMetadata");
            }

            services.AddAuthentication().AddJwtBearer(options =>
            {
                options.Authority = identityServerUrl;
                options.RequireHttpsMetadata = Convert.ToBoolean(requireHttpsMetadata);

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = true,
                    ValidAudience = apiName,

                    ValidateIssuer = true,
                    ValidIssuer = identityServerUrl,

                    ValidateLifetime = true
                };
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
                    settings.UseSystemTextJsonSerializer(settings =>
                    {
                        settings.IncludeFields = true;
                        return settings;
                    });

                    settings.Host(rabbitMQHost, connection =>
                    {
                        connection.Username(rabbitMQUsername);
                        connection.Password(rabbitMQPassword);
                    });

                    settings.ConfigureEndpoints(context);
                });
                x.AddRequestClient<RequestRetrieveGeoXact>();
                
                //UNI AUTH HELPER
                x.AddRequestClient<RequestUserPermissions>();
                x.AddRequestClient<RequestAllPermissions>();
            });

            services.AddMassTransitHostedService();

            services.AddAuthorization();

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
            
            // Uni-Auth Integration Setup
            services.AddScoped<IUniAuthPermissionService, UniAuthPermissionService>();
            
            var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST");
            var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT");
            var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");

            string redisConnectionString = $"{redisHost}:{redisPort}";

            if (!string.IsNullOrEmpty(redisPassword)) {
                redisConnectionString += $",password={redisPassword}";
            }
            
            if (string.IsNullOrEmpty(redisHost) || string.IsNullOrEmpty(redisPort)) {
                redisConnectionString = GetConfig("Redis:ConnectionString");
            }
            
            services.AddUniAuthHelper(redisConnectionString);

            services.AddHostedService<UniAuthPolicyInitializer>();
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
               .SetIsOriginAllowed((host) => true)
               .AllowCredentials()
               .WithExposedHeaders("X-Pagination")
            );

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/hc");
            });
        }
        
        private string GetConfig(string key)
        {
            return Configuration[key] ?? throw new ArgumentNullException(key, "environment variable null");
        }
    }
}
