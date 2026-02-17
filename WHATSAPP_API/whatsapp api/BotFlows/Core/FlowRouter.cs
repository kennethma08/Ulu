using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Whatsapp_API.Data;

namespace Whatsapp_API.BotFlows.Core
{
    public class FlowRouter : IFlowRouter
    {
        private readonly IEnumerable<IChatFlow> _flows;
        private readonly MyDbContext _db;
        private readonly ILogger<FlowRouter> _log;

        public FlowRouter(IEnumerable<IChatFlow> flows, MyDbContext db, ILogger<FlowRouter> log)
        {
            _flows = flows;
            _db = db;
            _log = log;
        }

        public async Task RouteAsync(FlowInput input)
        {
            var flowKey = await _db.Companies
                .AsNoTracking()
                .Where(e => e.Id == input.CompanyId)
                .Select(e => e.FlowKey)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(flowKey))
            {
                _log.LogWarning("Empresa sin FlowKey. CompanyId={CompanyId}", input.CompanyId);
                return;
            }

            var flow = _flows.FirstOrDefault(f => f.Key.Equals(flowKey, System.StringComparison.OrdinalIgnoreCase));
            if (flow == null)
            {
                _log.LogWarning("Flow no encontrado. CompanyId={CompanyId} FlowKey={FlowKey}", input.CompanyId, flowKey);
                return;
            }

            await flow.HandleAsync(input);
        }
    }
}
