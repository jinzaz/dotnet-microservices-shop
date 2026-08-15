using System;
using System.Net.Http;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Timeout;

namespace MSA.ApiGateway
{
    // Apply Polly policies (timeout + circuit breaker) to HttpClient handlers created by IHttpClientFactory
    public class PollyHttpMessageHandlerBuilderFilter : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        {
            return builder =>
            {
                next(builder);

                // Add a delegating handler that applies Polly policies
                // Configure a combined policy: timeout + circuit breaker
                var policy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10), TimeoutStrategy.Pessimistic)
                    .WrapAsync(Policy<HttpResponseMessage>
                        .Handle<Exception>()
                        .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30)));

                builder.AdditionalHandlers.Add(new PolicyHttpMessageHandler(policy));
            };
        }
    }

    internal class PolicyHttpMessageHandler : DelegatingHandler
    {
        private readonly IAsyncPolicy<HttpResponseMessage> _policy;

        public PolicyHttpMessageHandler(IAsyncPolicy<HttpResponseMessage> policy)
        {
            _policy = policy;
            InnerHandler = new HttpClientHandler();
        }

        protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            return await _policy.ExecuteAsync(ct => base.SendAsync(request, ct), cancellationToken);
        }
    }
}
