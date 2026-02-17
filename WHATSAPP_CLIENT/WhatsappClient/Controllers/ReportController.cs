using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using WhatsappClient.Models;
using WhatsappClient.Services;

namespace WhatsappClient.Controllers
{
    public class ReportController : Controller
    {
        private readonly ApiService _api;

        public ReportController(ApiService api)
        {
            _api = api;
        }

        // Helper simple: hora actual de Costa Rica (UTC-6)
        private static DateTime GetCostaRicaNow()
        {
            return DateTime.UtcNow.AddHours(-6);
        }

        // /Report -> redirige a /Report/General
        public ActionResult Index()
        {
            return RedirectToAction("General");
        }

        // ================== VISTA: Analíticas generales ==================

        public ActionResult General()
        {
            var todayCr = GetCostaRicaNow().Date;
            var from = todayCr.AddDays(-29);
            var to = todayCr;

            ViewBag.From = from.ToString("yyyy-MM-dd");
            ViewBag.To = to.ToString("yyyy-MM-dd");

            // Vista general: Views/Reports/Index.cshtml
            return View("~/Views/Reports/Index.cshtml");
        }

        // ================== VISTA: Analíticas por agente ==================

        public async Task<ActionResult> Agents()
        {
            var todayCr = GetCostaRicaNow().Date;
            var from = todayCr.AddDays(-29);
            var to = todayCr;

            ViewBag.From = from.ToString("yyyy-MM-dd");
            ViewBag.To = to.ToString("yyyy-MM-dd");

            // Trae agentes desde la API; si viene null, usamos lista vacía
            var agentes = await _api.GetAgentesAsync() ?? new List<UserDto>();

            // Vista de analíticas por agente: Views/Reports/Agents.cshtml
            return View("~/Views/Reports/Agents.cshtml", agentes);
        }

        // ==== Records de respuesta ====

        public record SeriesPoint(string label, int value);
        public record SeriesResponse(string granularity, List<SeriesPoint> points);
        public record CountItem(string name, int count);

        public record KpisResponse(
            int totalMessages,
            int closedConversations,
            int newClients,
            int openConversations,
            double avgMessagesPerConversation,
            int activeClients,
            int activeCountries
        );

        public record CountryCount(string code, string? name, int count);

        // ==== Helpers genéricos ====

        private static DateTime ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return GetCostaRicaNow().Date;

            if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return GetCostaRicaNow().Date;

            // Lo tratamos como fecha "tal cual", sin zona; para filtros de rango no importa el Kind
            return dt.Date;
        }

        private static DateTime StartOfIsoWeek(DateTime d)
        {
            var day = (int)d.DayOfWeek;
            if (day == 0) day = 7;
            var monday = d.Date.AddDays(1 - day);
            return monday;
        }

        private static string NormalizeGroup(string? g)
        {
            g ??= "day";
            g = g.Trim().ToLowerInvariant();
            return g is "day" or "week" or "month" ? g : "day";
        }

        private static string LabelFor(DateTime d, string group) =>
            group switch
            {
                "day" => d.ToString("yyyy-MM-dd"),
                "week" => StartOfIsoWeek(d).ToString("yyyy-MM-dd"),
                "month" => d.ToString("yyyy-MM"),
                _ => d.ToString("yyyy-MM-dd")
            };

        private static object? Prop(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (p != null) return p.GetValue(obj);
                var f = t.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (f != null) return f.GetValue(obj);
            }
            return null;
        }

        private static DateTime FlexDate(object obj, params string[] names)
        {
            var v = Prop(obj, names);
            if (v == null) return DateTime.MinValue;

            if (v is DateTime dt)
                return dt;

            if (v is DateTimeOffset dto)
                return dto.DateTime;

            if (v is long epoch)
                return DateTimeOffset.FromUnixTimeSeconds(epoch).DateTime;

            if (v is string s && DateTime.TryParse(s, out var parsed))
                return parsed;

            return DateTime.MinValue;
        }

        private static int? FlexInt(object obj, params string[] names)
        {
            var v = Prop(obj, names);
            if (v == null) return null;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is string s && int.TryParse(s, out var si)) return si;
            return null;
        }

        private static bool FlexBool(object obj, params string[] names)
        {
            var v = Prop(obj, names);
            if (v == null) return false;
            if (v is bool b) return b;
            if (v is int i) return i != 0;
            if (v is string s && bool.TryParse(s, out var sb)) return sb;
            return false;
        }

        private static string GetStatus(object conv)
        {
            var stObj = Prop(conv, "Status", "status", "Estado", "estado");
            var st = stObj?.ToString();
            return string.IsNullOrWhiteSpace(st) ? "open" : st.Trim();
        }

        // ================== SERIES: Mensajes por fecha ==================

        [HttpGet]
        public async Task<IActionResult> Series(string from, string to, string? groupBy = "day")
        {
            var f = ParseDate(from).Date;
            var t = ParseDate(to).Date;
            var g = NormalizeGroup(groupBy);

            var mensajes = await _api.ObtenerMensajesAsync();
            var dateNames = new[] { "Fecha", "CreatedAt", "SentAt", "sent_at", "Date", "Timestamp", "CreatedOn" };

            var points = mensajes
                .Select(m => FlexDate(m, dateNames))
                .Where(d => d != DateTime.MinValue && d.Date >= f && d.Date <= t)
                .GroupBy(d => LabelFor(d.Date, g))
                .Select(gp => new SeriesPoint(gp.Key, gp.Count()))
                .OrderBy(p => p.label)
                .ToList();

            return Ok(new SeriesResponse(g, points));
        }

        // ================== TOP CLIENTES POR MENSAJES ==================

        [HttpGet]
        public async Task<IActionResult> TopClients(string from, string to, int take = 10)
        {
            var f = ParseDate(from).Date;
            var t = ParseDate(to).Date;

            var mensajes = await _api.ObtenerMensajesAsync();
            var contactos = await _api.ObtenerContactosAsync();

            var dateNames = new[] { "Fecha", "CreatedAt", "SentAt", "sent_at", "Date", "Timestamp" };
            var contactIdNames = new[] { "ContactId", "contact_id", "ContactoId", "ClienteId", "CustomerId", "ToContactId" };

            var data = mensajes
                .Select(m => new
                {
                    dt = FlexDate(m, dateNames),
                    contactId = FlexInt(m, contactIdNames) ?? -1
                })
                .Where(x => x.dt != DateTime.MinValue && x.dt.Date >= f && x.dt.Date <= t)
                .GroupBy(x => x.contactId)
                .Select(gp =>
                {
                    var cid = gp.Key;
                    var name = cid <= 0
                        ? "Sin cliente"
                        : contactos.FirstOrDefault(c => c.Id == cid)?.Name ?? $"Cliente #{cid}";

                    return new CountItem(name, gp.Count());
                })
                .OrderByDescending(x => x.count)
                .Take(Math.Max(1, take))
                .ToList();

            return Ok(data);
        }

        // ================== PAÍSES (para gráfico de países) ==================

        [HttpGet]
        public async Task<IActionResult> Countries(string from, string to)
        {
            var f = ParseDate(from).Date;
            var t = ParseDate(to).Date;

            var mensajes = await _api.ObtenerMensajesAsync();
            var contactos = await _api.ObtenerContactosAsync();

            var dateNames = new[] { "Fecha", "CreatedAt", "SentAt", "sent_at", "Date", "Timestamp" };
            var contactIdNames = new[] { "ContactId", "contact_id", "ContactoId", "ClienteId", "CustomerId", "ToContactId" };

            var msgsRange = mensajes
                .Select(m => new
                {
                    dt = FlexDate(m, dateNames),
                    cid = FlexInt(m, contactIdNames) ?? -1
                })
                .Where(x =>
                    x.dt != DateTime.MinValue &&
                    x.dt.Date >= f && x.dt.Date <= t &&
                    x.cid > 0)
                .ToList();

            var activeContactIds = new HashSet<int>(msgsRange.Select(x => x.cid));

            var all = contactos
                .Where(c => activeContactIds.Contains(c.Id))
                .GroupBy(c =>
                {
                    var code = string.IsNullOrWhiteSpace(c.Country)
                        ? "??"
                        : c.Country!.Trim().ToUpperInvariant();
                    return code;
                })
                .Select(g => new CountryCount(
                    g.Key,
                    g.Key,          // nombre = código (CR, MX, etc.)
                    g.Count()
                ))
                .OrderByDescending(x => x.count)
                .ToList();

            // Top 4 + "Otros"
            List<CountryCount> result;
            if (all.Count <= 5)
            {
                result = all;
            }
            else
            {
                var top4 = all.Take(4).ToList();
                var othersCount = all.Skip(4).Sum(x => x.count);
                top4.Add(new CountryCount("OTHERS", "Otros", othersCount));
                result = top4;
            }

            return Ok(result);
        }

        // ================== KPIs GENERALES ==================

        [HttpGet]
        public async Task<IActionResult> Kpis(string from, string to)
        {
            var f = ParseDate(from).Date;
            var t = ParseDate(to).Date;

            var mensajes = await _api.ObtenerMensajesAsync();
            var convs = await _api.ObtenerConversacionesAsync();
            var contactos = await _api.ObtenerContactosAsync();

            var msgDateNames = new[] { "Fecha", "CreatedAt", "SentAt", "sent_at", "Date", "Timestamp" };
            var contactIdNames = new[] { "ContactId", "contact_id", "ContactoId", "ClienteId", "CustomerId" };
            var convIdNames = new[] { "ConversationId", "conversation_id", "ConversationSessionId", "IdConversation", "ChatId", "SessionId" };

            // Mensajes en el rango
            var msgsRange = mensajes
                .Select(m => new
                {
                    dt = FlexDate(m, msgDateNames),
                    cid = FlexInt(m, contactIdNames) ?? -1,
                    convId = FlexInt(m, convIdNames) ?? -1
                })
                .Where(x => x.dt != DateTime.MinValue &&
                            x.dt.Date >= f && x.dt.Date <= t)
                .ToList();

            int totalMessages = msgsRange.Count;

            var perConv = msgsRange
                .Where(x => x.convId > 0)
                .GroupBy(x => x.convId)
                .Select(g => g.Count())
                .ToList();

            double avgMessagesPerConversation = perConv.Count == 0
                ? 0d
                : perConv.Average();

            // Conversaciones que "tocan" el rango de fechas
            var convActiveInRange = convs
                .Select(c =>
                {
                    var start = FlexDate(c, "started_at", "StartedAt", "CreatedAt", "created_at", "Fecha", "Date");
                    var end = FlexDate(c, "ended_at", "EndedAt", "last_activity_at", "LastActivityAt");

                    if (start == DateTime.MinValue && end != DateTime.MinValue)
                        start = end;
                    if (end == DateTime.MinValue && start != DateTime.MinValue)
                        end = start;

                    if (start == DateTime.MinValue)
                        start = f;
                    if (end == DateTime.MinValue)
                        end = DateTime.MaxValue.Date;

                    return new
                    {
                        conv = c,
                        start = start.Date,
                        end = end.Date
                    };
                })
                .Where(x => x.start <= t && x.end >= f)
                .ToList();

            int closedConversations = convActiveInRange.Count(x =>
            {
                var st = GetStatus(x.conv);
                return string.Equals(st, "closed", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(st, "cerrado", StringComparison.OrdinalIgnoreCase);
            });

            int openConversations = convActiveInRange.Count(x =>
            {
                var st = GetStatus(x.conv);
                return !string.Equals(st, "closed", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(st, "cerrado", StringComparison.OrdinalIgnoreCase);
            });

            // Clientes activos (que tuvieron mensajes en el rango)
            var activeClientIds = msgsRange
                .Where(x => x.cid > 0)
                .Select(x => x.cid)
                .Distinct()
                .ToList();

            int activeClients = activeClientIds.Count;

            var activeSet = new HashSet<int>(activeClientIds);
            int activeCountries = contactos
                .Where(c => activeSet.Contains(c.Id))
                .Select(c =>
                {
                    var code = string.IsNullOrWhiteSpace(c.Country)
                        ? "??"
                        : c.Country!.Trim().ToUpperInvariant();
                    return code;
                })
                .Distinct()
                .Count();

            // Clientes nuevos en el rango (primer mensaje en la fecha)
            var firstByClient = msgsRange
                .Where(x => x.cid > 0)
                .GroupBy(x => x.cid)
                .Select(g => new
                {
                    cid = g.Key,
                    first = g.Min(v => v.dt.Date)
                });

            int newClients = firstByClient.Count(x => x.first >= f && x.first <= t);

            var result = new KpisResponse(
                totalMessages,
                closedConversations,
                newClients,
                openConversations,
                avgMessagesPerConversation,
                activeClients,
                activeCountries
            );

            return Ok(result);
        }

        // ================== KPIs POR AGENTE ==================

        /// <summary>
        /// KPIs por agente: cantidad de conversaciones cerradas y duración promedio (minutos)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> KpisByAgent(int id, string from, string to)
        {
            if (id <= 0)
                return Json(new { success = false, closedCount = 0, avgDurationMinutes = 0 });

            var f = ParseDate(from).Date;
            var t = ParseDate(to).Date;

            var convs = await _api.ObtenerConversacionesAsync();

            // Nombres posibles en la entidad Conversation
            var closedByNames = new[] { "ClosedByUserId", "closed_by_user_id", "UsuarioCierreId", "ClosedBy", "AgentId" };
            var startedNames = new[] { "StartedAt", "started_at", "FechaInicio", "created_at", "CreatedAt" };
            var endedNames = new[] { "EndedAt", "ended_at", "FechaFin", "ClosedAt", "Closed_At" };
            var statusNames = new[] { "Status", "status", "Estado", "estado" };

            var closedList = convs
                .Select(c =>
                {
                    var closedBy = FlexInt(c, closedByNames) ?? 0;
                    var started = FlexDate(c, startedNames);
                    var ended = FlexDate(c, endedNames);
                    var status = Prop(c, statusNames)?.ToString() ?? "open";

                    return new
                    {
                        Conv = c,
                        closedBy,
                        started,
                        ended,
                        status
                    };
                })
                .Where(x =>
                    x.closedBy == id &&
                    !string.IsNullOrWhiteSpace(x.status) &&
                    x.status.Trim().Equals("closed", StringComparison.OrdinalIgnoreCase) &&
                    x.ended != DateTime.MinValue &&
                    x.ended.Date >= f && x.ended.Date <= t
                )
                .ToList();

            var closedCount = closedList.Count;

            double avgMinutes = 0;
            if (closedCount > 0)
            {
                var durations = closedList
                    .Where(x => x.started != DateTime.MinValue && x.ended != DateTime.MinValue)
                    .Select(x =>
                    {
                        var diff = x.ended - x.started;
                        return Math.Max(0, diff.TotalMinutes);
                    })
                    .ToList();

                if (durations.Count > 0)
                    avgMinutes = durations.Average();
            }

            return Json(new
            {
                success = true,
                closedCount,
                avgDurationMinutes = Math.Round(avgMinutes, 1)
            });
        }

        // ================== LISTA DE CONVERSACIONES CERRADAS POR AGENTE ==================

        /// <summary>
        /// Devuelve las conversaciones cerradas por un agente, con teléfono, inicio, fin y duración (min)
        /// para llenar la tabla "Conversaciones cerradas por agente".
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ClosedByAgent(int id, string from, string to)
        {
            if (id <= 0)
                return Json(new { items = Array.Empty<object>() });

            var f = ParseDate(from).Date;
            var t = ParseDate(to).Date;

            var convs = await _api.ObtenerConversacionesAsync();
            var contactos = await _api.ObtenerContactosAsync();

            var closedByNames = new[] { "ClosedByUserId", "closed_by_user_id", "UsuarioCierreId", "ClosedBy", "AgentId" };
            var startedNames = new[] { "StartedAt", "started_at", "FechaInicio", "created_at", "CreatedAt" };
            var endedNames = new[] { "EndedAt", "ended_at", "FechaFin", "ClosedAt", "Closed_At" };
            var statusNames = new[] { "Status", "status", "Estado", "estado" };
            var contactIdNames = new[] { "ContactId", "contact_id", "ClienteId", "IdContacto" };

            var closedList = convs
                .Select(c =>
                {
                    var closedBy = FlexInt(c, closedByNames) ?? 0;
                    var started = FlexDate(c, startedNames);
                    var ended = FlexDate(c, endedNames);
                    var status = Prop(c, statusNames)?.ToString() ?? "open";
                    var contactId = FlexInt(c, contactIdNames) ?? 0;
                    var idConv = FlexInt(c, "Id", "id", "ConversationId", "conversation_id") ?? 0;

                    return new
                    {
                        id = idConv,
                        closedBy,
                        started,
                        ended,
                        status,
                        contactId
                    };
                })
                .Where(x =>
                    x.closedBy == id &&
                    !string.IsNullOrWhiteSpace(x.status) &&
                    x.status.Trim().Equals("closed", StringComparison.OrdinalIgnoreCase) &&
                    x.ended != DateTime.MinValue &&
                    x.ended.Date >= f && x.ended.Date <= t
                )
                .ToList();

            // usar PhoneNumber en lugar de Phone
            var phoneByContact = contactos.ToDictionary(
                c => c.Id,
                c => c.PhoneNumber ?? string.Empty
            );

            var items = closedList
                .Select(x =>
                {
                    double? mins = null;
                    if (x.started != DateTime.MinValue && x.ended != DateTime.MinValue)
                    {
                        var diff = x.ended - x.started;
                        mins = Math.Max(0, diff.TotalMinutes);
                    }

                    phoneByContact.TryGetValue(x.contactId, out var phone);

                    return new
                    {
                        id = x.id,
                        contactPhone = phone,
                        startedAt = x.started == DateTime.MinValue ? (DateTime?)null : x.started,
                        endedAt = x.ended == DateTime.MinValue ? (DateTime?)null : x.ended,
                        durationMinutes = mins
                    };
                })
                .OrderByDescending(x => x.endedAt ?? x.startedAt)
                .ToList();

            return Json(new { items });
        }

        // ================== ESTADO DEL AGENTE (online / offline) ==================

        /// <summary>
        /// Devuelve:
        ///  - lastMinutes: minutos desde la última actividad (para lógica)
        ///  - status: "En línea / Desconectado"
        ///  - lastActivityLocal: fecha/hora de última actividad (hora Costa Rica)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> AgentStatus(int id)
        {
            if (id <= 0)
                return Json(new { success = false, lastMinutes = (int?)null, status = "N/D", lastActivityLocal = (string?)null });

            var user = await _api.GetUsuarioByIdAsync(id);
            if (user == null)
                return Json(new { success = false, lastMinutes = (int?)null, status = "N/D", lastActivityLocal = (string?)null });

            int? lastMinutes = null;
            string? lastActivityLocal = null;

            // Empezamos con lo que diga la API
            bool isOnline = user.IsOnline;

            // Usamos LastActivity si existe; si no, caemos a LastLogin como referencia
            DateTime? lastRef = user.LastActivity ?? user.LastLogin;

            if (lastRef.HasValue)
            {
                // En BD ahora se guarda ya en hora de Costa Rica
                var lastCr = lastRef.Value;
                var nowCr = GetCostaRicaNow();

                lastActivityLocal = lastCr.ToString("yyyy-MM-dd HH:mm:ss");

                var diff = nowCr - lastCr;

                if (diff.TotalMinutes >= 0)
                {
                    lastMinutes = (int)Math.Round(diff.TotalMinutes);

                    // Regla 1: si tuvo actividad en los últimos 5 min -> online
                    if (lastMinutes.Value <= 5)
                    {
                        isOnline = true;
                    }
                    else
                    {
                        // Regla 2: si no está marcado online pero se logueó/hizo algo en los últimos 30 min,
                        // lo consideramos online "blando"
                        if (!isOnline && lastMinutes.Value <= 30)
                            isOnline = true;
                    }
                }
            }

            var statusText = isOnline ? "En línea" : "Desconectado";

            return Json(new { success = true, lastMinutes, status = statusText, lastActivityLocal });
        }

        // ================== Scaffold (por si algo lo usa) ==================

        public ActionResult Details(int id) => View();
        public ActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try { return RedirectToAction(nameof(Index)); }
            catch { return View(); }
        }

        public ActionResult Edit(int id) => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try { return RedirectToAction(nameof(Index)); }
            catch { return View(); }
        }

        public ActionResult Delete(int id) => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try { return RedirectToAction(nameof(Index)); }
            catch { return View(); }
        }
    }
}
