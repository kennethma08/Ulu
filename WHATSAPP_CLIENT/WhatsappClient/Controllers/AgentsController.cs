using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsappClient.Models;
using WhatsappClient.Services;

namespace WhatsappClient.Controllers
{
    [Authorize]
    public class AgentsController : Controller
    {
        private readonly ApiService _api;

        public AgentsController(ApiService api)
        {
            _api = api;
        }

        // Vista principal de analíticas por agente
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var convs = await _api.ObtenerConversacionesAsync() ?? new List<ConversationSessionDto>();
            var agentes = await _api.GetAgentesAsync() ?? new List<UserDto>();

            var hoyUtc = DateTime.UtcNow.Date;
            var closedTodayByUser = new Dictionary<int, int>();
            var lastMinByUser = new Dictionary<int, int>();
            var onlineByUser = new Dictionary<int, bool>();

            foreach (var u in agentes)
            {
                var closedToday = convs.Count(c =>
                    c.ClosedByUserId.HasValue &&
                    c.ClosedByUserId.Value == u.Id &&
                    c.EndedAt.HasValue &&
                    c.EndedAt.Value.ToUniversalTime().Date == hoyUtc
                );

                var last = u.LastActivity;
                var lastEffective = last ?? DateTime.UtcNow;
                var mins = (int)Math.Max(0, (DateTime.UtcNow - lastEffective).TotalMinutes);

                closedTodayByUser[u.Id] = closedToday;
                lastMinByUser[u.Id] = mins;
                onlineByUser[u.Id] = u.IsOnline;
            }

            var kpiOpen = convs.Count(c =>
                !string.IsNullOrWhiteSpace(c.Status) &&
                c.Status.Equals("open", StringComparison.OrdinalIgnoreCase));

            var kpiPromCarga = agentes.Count > 0 ? (kpiOpen / (double)agentes.Count) : 0d;
            var kpiActivos = onlineByUser.Values.Count(v => v);
            var kpiCierresHoy = closedTodayByUser.Values.Sum();

            ViewBag.KpiActivos = kpiActivos;
            ViewBag.KpiOpen = kpiOpen;
            ViewBag.KpiPromCarga = kpiPromCarga;
            ViewBag.KpiCierresHoy = kpiCierresHoy;
            ViewBag.ClosedTodayByUser = closedTodayByUser;
            ViewBag.LastMinByUser = lastMinByUser;
            ViewBag.OnlineByUser = onlineByUser;

            // Vista de analíticas por agente
            return View("~/Views/Agent/Index.cshtml", agentes);
        }

        // Endpoint JSON: conversaciones cerradas por agente (con rango de fechas opcional)
        [HttpGet]
        public async Task<IActionResult> ClosedByAgent(int id, DateTime? from = null, DateTime? to = null)
        {
            try
            {
                if (id <= 0)
                    return Json(new { items = Array.Empty<object>(), total = 0 });

                var conversations = await _api.ObtenerConversacionesAsync() ?? new List<ConversationSessionDto>();

                var query = conversations
                    .Where(c => c.ClosedByUserId.HasValue
                             && c.ClosedByUserId.Value == id
                             && c.EndedAt.HasValue);

                if (from.HasValue)
                {
                    var f = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
                    query = query.Where(c => c.EndedAt!.Value.ToUniversalTime() >= f);
                }

                if (to.HasValue)
                {
                    var t = DateTime.SpecifyKind(to.Value, DateTimeKind.Utc);
                    query = query.Where(c => c.EndedAt!.Value.ToUniversalTime() <= t);
                }

                var contacts = await _api.ObtenerContactosAsync() ?? new List<ContactDto>();
                var phoneByContact = contacts.ToDictionary(k => k.Id, v => v.PhoneNumber ?? "");

                var cerradas = query
                    .OrderByDescending(c => c.EndedAt)
                    .Select(c =>
                    {
                        int cid = Convert.ToInt32(c.ContactId);
                        string? phone = phoneByContact.TryGetValue(cid, out var ph) ? ph : null;

                        return new
                        {
                            id = c.Id,
                            contactId = cid,
                            contactPhone = phone,
                            startedAt = c.StartedAt,
                            endedAt = c.EndedAt
                        };
                    })
                    .ToList();

                return Json(new { items = cerradas, total = cerradas.Count });
            }
            catch (Exception ex)
            {
                return Json(new { items = Array.Empty<object>(), total = 0, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ActualizarNombre([FromBody] UpdateNombreReq req)
        {
            if (req == null || req.Id <= 0 || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest("Parámetros inválidos.");

            var ok = await _api.UpdateNombreAgenteAsync(req.Id, req.Name.Trim());
            if (!ok) return StatusCode(500, "No se pudo actualizar el nombre.");
            return Ok(new { mensaje = "Actualizado" });
        }

        public record UpdateNombreReq(int Id, string Name);
    }
}
