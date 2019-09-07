using System;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Polly.Registry;
using SimmyDemo_WebApi.Chaos;

namespace SimmyDemo_WebApi
{
    public class Startup
    {
        public const string ResiliencePolicy = "ResiliencePolicy";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            // Read the endpoints we expect the service to monitor.
            services.Configure<MonitoringSettings>(Configuration.GetSection("MonitoringEndpoints"));

            // Create (and register with DI) a policy registry containing some policies we want to use.
            services.AddPolicyRegistry(new PolicyRegistry
            {
                { ResiliencePolicy, GetResiliencePolicy() }
            });

            // Register a typed client via HttpClientFactory, set to use the policy we placed in the policy registry.
            services.AddHttpClient<ResilientHttpClient>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                })
                .AddPolicyHandlerFromRegistry(ResiliencePolicy);

            // Add ability for the app to populate ChaosSettings from json file (or any other .NET Core configuration source)
            services.Configure<AppChaosSettings>(Configuration.GetSection("ChaosSettings"));
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            // Only add Simmy chaos injection in development-environment runs (ie prevent chaos-injection ever reaching staging or prod - if that is what you want).
            if (env.IsDevelopment())
            {
                // Wrap every policy in the policy registry in Simmy chaos injectors.
                var registry = app.ApplicationServices.GetRequiredService<IPolicyRegistry<string>>();
                registry?.AddChaosInjectors();
            }

            app.UseHttpsRedirection();
            app.UseMvc();
        }

        private IAsyncPolicy<HttpResponseMessage> GetResiliencePolicy()
        {
            // Define a policy which will form our resilience strategy.  These could be anything.  The settings for them could obviously be drawn from config too.
            var retry = HttpPolicyExtensions.HandleTransientHttpError()
                .RetryAsync(2);

            return retry;
        }
    }
}
