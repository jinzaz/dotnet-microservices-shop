using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Consul;

namespace MSA.ApiGateway
{
    public class ConsulProxyConfigProvider : Yarp.ReverseProxy.Configuration.IProxyConfigProvider
    {
        private volatile InMemoryConfig _config;
        private readonly Consul.IConsulClient _consul;

        public ConsulProxyConfigProvider(IConsulClient consul)
        {
            _consul = consul;
            _config = new InMemoryConfig(new List<Yarp.ReverseProxy.Configuration.RouteConfig>(), new Dictionary<string, Yarp.ReverseProxy.Configuration.ClusterConfig>());
        }

        public IProxyConfig GetConfig() => _config;

        public async System.Threading.Tasks.Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            // Discover services with prefix 'msa-' (convention) and build clusters
            var services = await _consul.Catalog.Services(cancellationToken);
            var svcNames = services?.Response?.Keys ?? Enumerable.Empty<string>();

            var routes = new List<Yarp.ReverseProxy.Configuration.RouteConfig>();
            var clusters = new Dictionary<string, Yarp.ReverseProxy.Configuration.ClusterConfig>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in svcNames)
            {
                if (!name.StartsWith("msa-", StringComparison.OrdinalIgnoreCase))
                    continue;

                var svcInstances = await _consul.Catalog.Service(name);
                var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int idx = 0;
                foreach (var inst in svcInstances.Response)
                {
                    var addr = inst.ServiceAddress;
                    if (string.IsNullOrEmpty(addr)) addr = inst.Address;
                    var destKey = $"{name}/dest{idx++}";
                    var destAddr = inst.ServicePort > 0 ? $"http://{addr}:{inst.ServicePort}/" : $"http://{addr}/";
                    destinations[destKey] = destAddr;
                }

                if (destinations.Count == 0) continue;

                var clusterId = name;
                var cluster = new Yarp.ReverseProxy.Configuration.ClusterConfig()
                {
                    ClusterId = clusterId,
                    Destinations = destinations.ToDictionary(kv => kv.Key, kv => new Yarp.ReverseProxy.Configuration.DestinationConfig() { Address = kv.Value })
                };

                clusters[clusterId] = cluster;

                // default route: /{serviceName}/{**catch-all}
                var route = new Yarp.ReverseProxy.Configuration.RouteConfig()
                {
                    RouteId = $"route_{clusterId}",
                    ClusterId = clusterId,
                    Match = new Yarp.ReverseProxy.Configuration.RouteMatch
                    {
                        Path = $"/{clusterId}/{{**catch-all}}"
                    }
                };

                routes.Add(route);
            }

            _config = new InMemoryConfig(routes, clusters);
        }

        private class InMemoryConfig : Yarp.ReverseProxy.Configuration.IProxyConfig
        {
            public InMemoryConfig(IReadOnlyList<Yarp.ReverseProxy.Configuration.RouteConfig> routes, IReadOnlyDictionary<string, Yarp.ReverseProxy.Configuration.ClusterConfig> clusters)
            {
                Routes = routes;
                Clusters = clusters;
                ChangeToken = new Microsoft.Extensions.Primitives.CancellationChangeToken(new CancellationToken(false));
            }

            public IReadOnlyList<Yarp.ReverseProxy.Configuration.RouteConfig> Routes { get; }
            public IReadOnlyDictionary<string, Yarp.ReverseProxy.Configuration.ClusterConfig> Clusters { get; }
            public Microsoft.Extensions.Primitives.IChangeToken ChangeToken { get; }
        }
    }
}
