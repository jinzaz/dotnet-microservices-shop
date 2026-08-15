using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    public interface IEventBus
    {
        Task PublishAsync(IIntegrationEvent @event);
    }
}
