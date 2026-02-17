using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;
using Whatsapp_API.Infrastructure.MultiTenancy;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Models.Helpers;

namespace Whatsapp_API.Business.General
{
    public class ConversationBus
    {
        private readonly MyDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly TenantContext _tenant;

        public ConversationBus(MyDbContext db, IHttpContextAccessor http, TenantContext tenant)
        {
            _db = db;
            _http = http;
            _tenant = tenant;
        }

        private int EmpresaIdActual()
        {
            try
            {
                var items = _http?.HttpContext?.Items;

                // Items: aceptar ambos (tu webhook usa COMPANY_ID)
                if (items != null)
                {
                    if (items.TryGetValue("COMPANY_ID", out var vCompany))
                    {
                        if (vCompany is int vi && vi > 0) return vi;
                        if (vCompany is string vs && int.TryParse(vs, out var vis) && vis > 0) return vis;
                    }

                    if (items.TryGetValue("EMPRESA_ID", out var vEmp))
                    {
                        if (vEmp is int vi2 && vi2 > 0) return vi2;
                        if (vEmp is string vs2 && int.TryParse(vs2, out var vis2) && vis2 > 0) return vis2;
                    }
                }

                // Tenant
                if (_tenant?.CompanyId > 0) return _tenant.CompanyId;

                var http = _http?.HttpContext;

                // Claims
                string s =
                    http?.User?.FindFirst("empresa_id")?.Value ??
                    http?.User?.FindFirst("company_id")?.Value ??
                    http?.User?.FindFirst("empresaId")?.Value ??
                    http?.User?.FindFirst("companyId")?.Value ??
                    "";

                // Headers
                if (string.IsNullOrWhiteSpace(s))
                    s = http?.Request?.Headers["X-Company-Id"].FirstOrDefault()
                      ?? http?.Request?.Headers["X-Company"].FirstOrDefault()
                      ?? http?.Request?.Headers["X-Empresa-Id"].FirstOrDefault()
                      ?? http?.Request?.Headers["X-Empresa"].FirstOrDefault();

                // Query
                if (string.IsNullOrWhiteSpace(s))
                    s = http?.Request?.Query["company_id"].FirstOrDefault()
                      ?? http?.Request?.Query["CompanyId"].FirstOrDefault()
                      ?? http?.Request?.Query["empresa_id"].FirstOrDefault()
                      ?? http?.Request?.Query["EmpresaId"].FirstOrDefault();

                return int.TryParse(s, out var id) ? id : 0;
            }
            catch
            {
                return 0;
            }
        }

        private (int userId, int profileId, bool isAdmin) GetMe()
        {
            var user = _http?.HttpContext?.User;

            string? sId =
                user?.FindFirst("id")?.Value ??
                user?.FindFirst("user_id")?.Value ??
                user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                user?.FindFirst("sub")?.Value;

            int.TryParse(sId, out var userId);

            string? sProfile =
                user?.FindFirst("idProfile")?.Value ??
                user?.FindFirst("profile_id")?.Value ??
                user?.FindFirst("IdProfile")?.Value ??
                user?.FindFirst("ProfileId")?.Value;

            int.TryParse(sProfile, out var profileId);

            var role =
                user?.FindFirst("role")?.Value ??
                user?.FindFirst(ClaimTypes.Role)?.Value ??
                "";

            // si no viene profileId en el token, igual se puede decidir por role
            bool isAdmin =
                profileId == 2 || profileId == 3 ||
                role.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("superadmin", StringComparison.OrdinalIgnoreCase);

            return (userId, profileId, isAdmin);
        }

        public BooleanoDescriptivo<List<Conversation>> List()
        {
            var eid = EmpresaIdActual();
            var data = _db.Conversations
                .AsNoTracking()
                .Where(x => x.CompanyId == eid)
                .OrderByDescending(x => x.LastActivityAt ?? x.StartedAt)
                .ToList();

            return new() { Exitoso = true, Data = data, StatusCode = 200 };
        }

        public BooleanoDescriptivo<Conversation> Find(int id)
        {
            var eid = EmpresaIdActual();
            var c = _db.Conversations
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == id && x.CompanyId == eid);

            return c == null
                ? new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 }
                : new() { Exitoso = true, Data = c, StatusCode = 200 };
        }

        public BooleanoDescriptivo<Conversation> Create(Conversation c)
        {
            var eid = EmpresaIdActual();

            var contOk = _db.Contacts
                .AsNoTracking()
                .Any(x => x.Id == c.ContactId && x.CompanyId == eid);

            if (!contOk)
                return new() { Exitoso = false, Mensaje = "Contacto inválido", StatusCode = 400 };

            c.CompanyId = eid;

            _db.Conversations.Add(c);
            _db.SaveChanges();

            return new() { Exitoso = true, Mensaje = "Creado", StatusCode = 201, Data = c };
        }

        public BooleanoDescriptivo<Conversation> Update(Conversation c)
        {
            var eid = EmpresaIdActual();

            var dbC = _db.Conversations.FirstOrDefault(x => x.Id == c.Id && x.CompanyId == eid);
            if (dbC == null)
                return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            // no permitir reabrir si ya está cerrada
            if (!string.IsNullOrWhiteSpace(c.Status) &&
                string.Equals(dbC.Status ?? "", "closed", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Status, "open", StringComparison.OrdinalIgnoreCase))
                return new() { Exitoso = false, Mensaje = "No se permite reabrir una conversación cerrada.", StatusCode = 409 };

            // básicos
            if (c.StartedAt != default) dbC.StartedAt = c.StartedAt;
            dbC.LastActivityAt = c.LastActivityAt ?? dbC.LastActivityAt;
            dbC.EndedAt = c.EndedAt ?? dbC.EndedAt;
            dbC.Status = string.IsNullOrWhiteSpace(c.Status) ? dbC.Status : c.Status.Trim();

            // métricas
            dbC.GreetingSent = c.GreetingSent;
            dbC.TotalMessages = c.TotalMessages;
            dbC.AiMessages = c.AiMessages;
            dbC.FirstResponseTime = c.FirstResponseTime ?? dbC.FirstResponseTime;
            dbC.Rating = c.Rating ?? dbC.Rating;
            dbC.ClosedByUserId = c.ClosedByUserId ?? dbC.ClosedByUserId;

            // hold (tu versión anterior NO lo guardaba)
            dbC.IsOnHold = c.IsOnHold;
            dbC.OnHoldReason = c.OnHoldReason;
            dbC.OnHoldAt = c.OnHoldAt;
            dbC.OnHoldByUserId = c.OnHoldByUserId;

            // agent requested
            dbC.AgentRequestedAt = c.AgentRequestedAt ?? dbC.AgentRequestedAt;

            // NO tocar Assigned* aquí para no borrar asignaciones por accidente
            _db.SaveChanges();

            return new() { Exitoso = true, Mensaje = "Actualizado", StatusCode = 200, Data = dbC };
        }

        public DescriptiveBoolean Delete(int id)
        {
            var eid = EmpresaIdActual();
            var c = _db.Conversations.FirstOrDefault(x => x.Id == id && x.CompanyId == eid);

            if (c == null)
                return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            _db.Conversations.Remove(c);
            _db.SaveChanges();

            return new() { Exitoso = true, Mensaje = "Eliminado", StatusCode = 200 };
        }

        public BooleanoDescriptivo<Conversation> FindOpenByContactStrict(int contactId, bool freshOnly)
        {
            var eid = EmpresaIdActual();

            var q = _db.Conversations
                .AsNoTracking()
                .Where(x => x.CompanyId == eid && x.ContactId == contactId && x.Status == "open")
                .OrderByDescending(x => x.LastActivityAt ?? x.StartedAt);

            var c = q.FirstOrDefault();
            if (c == null) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            if (freshOnly)
            {
                var last = c.LastActivityAt ?? c.StartedAt;
                if (last <= DateTime.UtcNow.AddHours(-23))
                    return new() { Exitoso = false, Mensaje = "No encontrado (stale)", StatusCode = 404 };
            }

            return new() { Exitoso = true, Data = c, StatusCode = 200 };
        }

        public BooleanoDescriptivo<Conversation> FindOpenByContact(int contactId)
        {
            var eid = EmpresaIdActual();

            var c = _db.Conversations
                .AsNoTracking()
                .Where(x => x.CompanyId == eid && x.ContactId == contactId && x.Status == "open")
                .OrderByDescending(x => x.LastActivityAt ?? x.StartedAt)
                .FirstOrDefault();

            return c == null
                ? new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 }
                : new() { Exitoso = true, Data = c, StatusCode = 200 };
        }

        public BooleanoDescriptivo<Conversation> EnsureOpenForIncoming(int contactId)
        {
            var eid = EmpresaIdActual();

            var open = _db.Conversations
                .AsNoTracking()
                .Where(x => x.CompanyId == eid && x.ContactId == contactId && x.Status == "open")
                .OrderByDescending(x => x.LastActivityAt ?? x.StartedAt)
                .FirstOrDefault();

            if (open != null)
                return new() { Exitoso = true, Data = open, StatusCode = 200, Mensaje = "Open existente" };

            var now = DateTime.UtcNow;
            var c = new Conversation
            {
                ContactId = contactId,
                StartedAt = now,
                LastActivityAt = now,
                Status = "open",
                GreetingSent = false,
                TotalMessages = 0,
                AiMessages = 0,
                IsOnHold = false
            };

            var r = Create(c);
            if (!r.Exitoso || r.Data == null)
                return new() { Exitoso = false, Mensaje = r.Mensaje ?? "No se pudo crear conversación", StatusCode = r.StatusCode };

            return new() { Exitoso = true, Data = r.Data, StatusCode = 201, Mensaje = "Creada nueva conversación" };
        }

        public BooleanoDescriptivo<Conversation> MarkAgentRequested(int conversationId)
        {
            var eid = EmpresaIdActual();
            var dbC = _db.Conversations.FirstOrDefault(x => x.Id == conversationId && x.CompanyId == eid);
            if (dbC == null) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            if (!dbC.AgentRequestedAt.HasValue)
                dbC.AgentRequestedAt = DateTime.UtcNow;

            _db.SaveChanges();
            return new() { Exitoso = true, Data = dbC, StatusCode = 200 };
        }

        public BooleanoDescriptivo<List<object>> ListPanel()
        {
            var eid = EmpresaIdActual();
            var (meId, _, isAdmin) = GetMe();

            var q = _db.Conversations
                .AsNoTracking()
                .Where(x => x.CompanyId == eid && x.AgentRequestedAt != null);

            if (!isAdmin)
                q = q.Where(x => x.AssignedUserId == null || x.AssignedUserId == meId);

            var projected = q
                .OrderByDescending(x => x.LastActivityAt ?? x.StartedAt)
                .Select(x => new
                {
                    x.Id,
                    x.ContactId,
                    x.Status,
                    x.StartedAt,
                    x.LastActivityAt,
                    x.EndedAt,
                    x.AgentRequestedAt,
                    x.AssignedUserId,
                    x.AssignedAt,
                    x.AssignedByUserId
                })
                .ToList();

            var data = projected.Select(x => (object)x).ToList();
            return new() { Exitoso = true, Data = data, StatusCode = 200 };
        }

        public DescriptiveBoolean Assign(int conversationId, int? toUserId)
        {
            var eid = EmpresaIdActual();
            var (meId, _, isAdmin) = GetMe();

            if (meId <= 0) return new() { Exitoso = false, Mensaje = "Usuario inválido", StatusCode = 401 };

            var dbC = _db.Conversations.FirstOrDefault(x => x.Id == conversationId && x.CompanyId == eid);
            if (dbC == null) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            if (!dbC.AgentRequestedAt.HasValue)
                return new() { Exitoso = false, Mensaje = "La conversación no tiene solicitud de agente.", StatusCode = 409 };

            if (!isAdmin)
            {
                if (!dbC.AssignedUserId.HasValue)
                {
                    if (!toUserId.HasValue || toUserId.Value != meId)
                        return new() { Exitoso = false, Mensaje = "Solo podés tomar la conversación para vos.", StatusCode = 403 };
                }
                else
                {
                    if (dbC.AssignedUserId.Value != meId)
                        return new() { Exitoso = false, Mensaje = "La conversación ya está asignada a otro agente.", StatusCode = 403 };
                }
            }

            if (toUserId.HasValue)
            {
                var exists = _db.Users.AsNoTracking().Any(u => u.Id == toUserId.Value);
                if (!exists) return new() { Exitoso = false, Mensaje = "Usuario destino no existe.", StatusCode = 400 };
            }

            dbC.AssignedUserId = toUserId;
            dbC.AssignedAt = DateTime.UtcNow;
            dbC.AssignedByUserId = meId;
            dbC.LastActivityAt = DateTime.UtcNow;

            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "OK", StatusCode = 200 };
        }

        public DescriptiveBoolean Release(int conversationId)
        {
            return Assign(conversationId, null);
        }
    }
}
