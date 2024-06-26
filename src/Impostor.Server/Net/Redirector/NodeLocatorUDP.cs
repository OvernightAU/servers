using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Impostor.Server.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Impostor.Server.Net.Redirector
{
    public class NodeLocatorUdp : INodeLocator, IDisposable
    {
        private readonly ILogger<NodeLocatorUdp> _logger;
        private readonly bool _isMaster;
        private readonly IPEndPoint _server;
        private readonly UdpClient _client;
        private readonly ConcurrentDictionary<string, AvailableNode> _availableNodes;

        public NodeLocatorUdp(ILogger<NodeLocatorUdp> logger, IOptions<ServerRedirectorConfig> config)
        {
            _logger = logger;

            throw new NotImplementedException();
        }

        public void Update(IPEndPoint ip, string gameCode)
        {
            _logger.LogDebug("Received update {0} -> {1}", gameCode, ip);

            _availableNodes.AddOrUpdate(
                gameCode,
                s => new AvailableNode
                {
                    Endpoint = ip,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
                (s, node) =>
                {
                    node.Endpoint = ip;
                    node.LastUpdated = DateTimeOffset.UtcNow;

                    return node;
                });

            foreach (var (key, value) in _availableNodes)
            {
                if (value.Expired)
                {
                    _availableNodes.TryRemove(key, out _);
                }
            }
        }

        public ValueTask<IPEndPoint> FindAsync(string gameCode)
        {
            if (!_isMaster)
            {
                return ValueTask.FromResult(default(IPEndPoint));
            }

            if (_availableNodes.TryGetValue(gameCode, out var node))
            {
                if (node.Expired)
                {
                    _availableNodes.TryRemove(gameCode, out _);
                    return ValueTask.FromResult(default(IPEndPoint));
                }

                return ValueTask.FromResult(node.Endpoint);
            }

            return ValueTask.FromResult(default(IPEndPoint));
        }

        public ValueTask RemoveAsync(string gameCode)
        {
            if (!_isMaster)
            {
                return ValueTask.CompletedTask;
            }

            _availableNodes.TryRemove(gameCode, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask SaveAsync(string gameCode, IPEndPoint endPoint)
        {
            var data = Encoding.UTF8.GetBytes($"{gameCode},{endPoint}");
            _client.Send(data, data.Length, _server);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            _client?.Dispose();
        }

        private class AvailableNode
        {
            public IPEndPoint Endpoint { get; set; }

            public DateTimeOffset LastUpdated { get; set; }

            public bool Expired => LastUpdated < DateTimeOffset.UtcNow.AddHours(-1);
        }
    }
}
