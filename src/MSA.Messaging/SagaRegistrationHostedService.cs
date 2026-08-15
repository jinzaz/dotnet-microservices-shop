using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    // Hosted service that registers all ISagaHandler instances with the SagaCoordinator at startup.
    public class SagaRegistrationHostedService : IHostedService
    {
        private readonly IServiceProvider _provider;

        public SagaRegistrationHostedService(IServiceProvider provider)
        {
            _provider = provider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Resolve coordinator and handlers from the provider
            var coordinator = (SagaCoordinator?)_provider.GetService(typeof(SagaCoordinator));
            var handlers = (IEnumerable<ISagaHandler>?)_provider.GetService(typeof(IEnumerable<ISagaHandler>));

            if (coordinator != null && handlers != null)
            {
                foreach (var h in handlers)
                {
                    coordinator.RegisterHandler(h);
                }
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
