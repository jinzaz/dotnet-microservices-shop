using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    // Marker/optional richer Saga interface. Leave minimal to allow alternative implementations.
    public interface ISaga
    {
        string Id { get; }

        Task StartAsync(IIntegrationEvent @event);
    }
}
