using System.Threading.Tasks;

namespace Whatsapp_API.BotFlows.Core
{
    public interface IChatFlow
    {
        string Key { get; }
        Task HandleAsync(FlowInput input);
    }
}
