using System.Threading.Tasks;

namespace Whatsapp_API.BotFlows.Core
{
    public interface IFlowRouter
    {
        Task RouteAsync(FlowInput input);
    }
}
