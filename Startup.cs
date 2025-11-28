using System;
using System.IO;
using System.Net;
using System.Reflection;
using AspNetCoreRateLimit;
using AspNetCoreRateLimit.Redis;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Prometheus;
using StackExchange.Redis;

namespace dev
{
    public class Startup
    {
        private IConfiguration Configuration;
        public Startup(IConfiguration conf)
        {
            Configuration = conf;
        }
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions();
            var redisCon = Configuration["REDIS_HOST"] ?? Configuration["redisCon"] ?? "localhost:6379";
            services.AddControllers().AddNewtonsoftJson();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Version = "v1",
                    Title = "Skylytics - Hypixel Skyblock Auction Tracker API",
                    Description = "Personal Hypixel Skyblock auction house and bazaar tracker API. Track auctions, bazaar prices, find flips, and analyze market trends.",
                    Contact = new OpenApiContact
                    {
                        Name = "Skylytics API",
                    },
                    License = new OpenApiLicense
                    {
                        Name = "Use under AGPLv3",
                        Url = new Uri("https://github.com/Coflnet/HypixelSkyblock/blob/master/LICENSE"),
                    }
                });

                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                    c.IncludeXmlComments(xmlPath);
            });
            services.AddSwaggerGenNewtonsoftSupport();
            
            // Redis cache - optional, gracefully handle if not available
            try
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisCon;
                    options.InstanceName = "Skylytics";
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis cache not available: {ex.Message}");
                services.AddDistributedMemoryCache();
            }
            
            services.AddResponseCaching();

            services.AddDbContext<HypixelContext>();
            services.AddSingleton<AuctionService>(AuctionService.Instance);
            
            // Register ItemDetails service
            var itemsBaseUrl = Configuration["ITEMS_BASE_URL"] ?? "http://localhost:5014";
            services.AddSingleton<IItemsApi>(sp => new ItemsApi(itemsBaseUrl));
            services.AddSingleton<ItemDetails>(sp => new ItemDetails(sp.GetRequiredService<IItemsApi>()));

            // Redis connection - optional
            try
            {
                var redisOptions = ConfigurationOptions.Parse(redisCon);
                redisOptions.AbortOnConnectFail = false;
                services.AddSingleton<IConnectionMultiplexer>(provider => ConnectionMultiplexer.Connect(redisOptions));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis connection not available: {ex.Message}");
            }

            // Rate limiting - disabled for personal use (no payment required)
            // You can re-enable this if you want to add rate limiting for your own use
            var enableRateLimiting = Configuration.GetValue<bool>("EnableRateLimiting", false);
            if (enableRateLimiting)
            {
                services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));
                services.Configure<IpRateLimitPolicies>(Configuration.GetSection("IpRateLimitPolicies"));
                services.AddRedisRateLimiting();
                services.AddSingleton<IIpPolicyStore, DistributedCacheIpPolicyStore>();
                services.AddSingleton<IRateLimitCounterStore, DistributedCacheRateLimitCounterStore>();
                services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
            }

            services.AddLogging(configure =>
            {
                configure.AddConsole();
            });

            // Add CORS for frontend access
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
            });
        }


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Skylytics API V1");
                c.RoutePrefix = "api";
                c.DocumentTitle = "Skylytics - Hypixel Skyblock Tracker API";
            });

            app.UseRouting();
            app.UseCors();

            app.UseResponseCaching();
            
            // Only use rate limiting if enabled
            var enableRateLimiting = Configuration.GetValue<bool>("EnableRateLimiting", false);
            if (enableRateLimiting)
            {
                app.UseIpRateLimiting();
            }

            app.Use(async (context, next) =>
            {
                context.Response.GetTypedHeaders().CacheControl =
                    new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
                    {
                        Public = true,
                        MaxAge = TimeSpan.FromSeconds(10)
                    };
                context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.Vary] =
                    new string[] { "Accept-Encoding" };

                await next();
            });

            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.ContentType = "application/json";

                    var exceptionHandlerPathFeature =
                        context.Features.Get<IExceptionHandlerPathFeature>();

                    if (exceptionHandlerPathFeature?.Error is CoflnetException ex)
                    {
                        await context.Response.WriteAsync(
                                        JsonConvert.SerializeObject(new { ex.Slug, ex.Message }));
                    }
                    else
                    {
                        await context.Response.WriteAsync(
                                        JsonConvert.SerializeObject(new { Slug = "internal_error", Message = "An unexpected internal error occurred. Please check that your request is valid." }));
                    }
                });
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonConvert.SerializeObject(new 
                    { 
                        message = "Welcome to Skylytics API - Hypixel Skyblock Auction Tracker",
                        version = Coflnet.Sky.Core.Program.Version,
                        docs = "/api",
                        endpoints = new 
                        {
                            auctions = "/api/auctions",
                            bazaar = "/api/bazaar",
                            items = "/api/items",
                            players = "/api/players",
                            prices = "/api/prices",
                            flipper = "/api/flipper",
                            search = "/api/search",
                            status = "/api/status"
                        }
                    }));
                });
                endpoints.MapMetrics();
                endpoints.MapControllers();
            });
        }
    }
}
