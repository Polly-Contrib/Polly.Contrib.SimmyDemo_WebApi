using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;

namespace SimmyDemo_WebApi
{
    public class ResilientHttpClient
    {
        private readonly HttpClient client;

        public ResilientHttpClient(HttpClient client)
        {
            this.client = client;
        }

        public async Task<MonitoringResults> GetStatus(MonitoringSettings settings, Context context)
        {
            MonitoringResults results = new MonitoringResults{Results = new List<EndpointResult>()};

            // In a real app, would use a Task.WhenAll() fanout pattern.
            foreach (var endpoint in settings.Endpoints)
            {
                var response = await GetAsyncUsingContext(endpoint, context);
                results.Results.Add(new EndpointResult(){Url = endpoint, Value = (int)response.StatusCode});
            }

            return results;
        }

        public async Task<MonitoringResults> GetResponseReadTimeMs(MonitoringSettings settings, Context context)
        {
            MonitoringResults results = new MonitoringResults { Results = new List<EndpointResult>() };

            // In a real app, would use a Task.WhenAll() fanout pattern.
            foreach (var endpoint in settings.Endpoints)
            {
                var watch = Stopwatch.StartNew();

                var response = await GetAsyncUsingContext(endpoint, context);

                // Returning the response read time should throw if - after all resilience attempts - we can't read and time a valid response.
                response.EnsureSuccessStatusCode();

                var contentActuallyOfNoInterest = await (response.Content?.ReadAsStringAsync()??Task.FromResult(String.Empty));

                results.Results.Add(new EndpointResult() { Url = endpoint, Value = watch.ElapsedMilliseconds });
            }

            return results;
        }

        private async Task<HttpResponseMessage> GetAsyncUsingContext(string url, Context context)
        {
            // This will include configured Polly resilience policies; and Simmy chaos policies in dev environments.
            // - Polly resilience policies were configured in StartUp
            // - A call to .AddChaosInjectors() added chaos policies to all policies in the registry, during startup, for dev environments.

            // We attach the Polly context to the HttpRequestMessage using an extension method provided by HttpClientFactory.
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, url);
            message.SetPolicyExecutionContext(context);

            // Make the request using the client configured by HttpClientFactory, which embeds the Polly and Simmy policies.
            return await client.SendAsync(message);
        }
    }
}
