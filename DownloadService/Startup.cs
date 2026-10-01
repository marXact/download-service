using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
            // The API Gateway authenticates the caller against IdentityServer before proxying, so
            // this service only reads the claims out of the token. Validating a second time here
            // costs an introspection roundtrip per request and takes the service down with
            // IdentityServer, so the token is parsed without validation instead. This is only safe
            // as long as the service is unreachable except through the gateway.
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.MapInboundClaims = false;

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateAudience = false,
                        ValidateIssuer = false,
                        ValidateLifetime = false,
                        RequireSignedTokens = false,
                        // On net6.0 the default handler is JwtSecurityTokenHandler, which rejects
                        // anything that is not a JwtSecurityToken (IDX10506).
                        SignatureValidator = (token, _) => new JwtSecurityToken(token),

                        // The IdentityServer handler this replaced used the short claim names.
                        // Without these two, User.Identity.Name and IsInRole fall back to the
                        // WS-Federation URIs and stop matching the "name" / "role" claims the
                        // tokens actually carry.
                        NameClaimType = "name",
                        RoleClaimType = "role",
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
    }
}
