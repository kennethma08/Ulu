using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Infrastructure.MultiTenancy;

namespace Whatsapp_API.Business.General
{
    public class MessageBus
    {
        private readonly MyDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly TenantContext _tenant;

        public MessageBus(MyDbContext db, IHttpContextAccessor http, TenantContext tenant)
        {
            _db = db;
            _http = http;
            _tenant = tenant;
        }

        private int EmpresaIdActual()
        {
            var items = _http?.HttpContext?.Items;
            if (items != null && items.TryGetValue("COMPANY_ID", out var vObj))
            {
                if (vObj is int vi && vi > 0) return vi;
                if (vObj is string vs && int.TryParse(vs, out var vis) && vis > 0) return vis;
            }

            if (_tenant?.CompanyId > 0) return _tenant.CompanyId;

            var http = _http?.HttpContext;
            string s = http?.User?.FindFirst("company_id")?.Value
                    ?? http?.User?.FindFirst("CompanyId")?.Value;

            if (string.IsNullOrWhiteSpace(s))
                s = http?.Request?.Headers["X-Company-Id"].FirstOrDefault()
                  ?? http?.Request?.Headers["X-Company"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(s))
                s = http?.Request?.Query["company_id"].FirstOrDefault()
                  ?? http?.Request?.Query["CompanyId"].FirstOrDefault();

            return int.TryParse(s, out var id) ? id : 0;
        }

        public BooleanoDescriptivo<List<Message>> List()
        {
            var eid = EmpresaIdActual();
            var list = _db.Messages
                .AsNoTracking()
                .Where(x => x.CompanyId == eid)
                .OrderByDescending(x => x.SentAt)
                .ToList();

            return new() { Exitoso = true, Data = list, StatusCode = 200 };
        }

        public BooleanoDescriptivo<Message> Find(int id)
        {
            var eid = EmpresaIdActual();
            var m = _db.Messages
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == id && x.CompanyId == eid);

            return m == null
                ? new() { Exitoso = false, Message = "No encontrado", StatusCode = 404 }
                : new() { Exitoso = true, Data = m, StatusCode = 200 };
        }

        public DescriptiveBoolean Create(Message m)
        {
            var eid = EmpresaIdActual();

            var convOk = _db.Conversations
                .AsNoTracking()
                .Any(c => c.Id == m.ConversationId && c.CompanyId == eid);
            if (!convOk) return new() { Exitoso = false, Mensaje = "Conversación inválida", StatusCode = 400 };

            var contOk = _db.Contacts
                .AsNoTracking()
                .Any(c => c.Id == m.ContactId && c.CompanyId == eid);
            if (!contOk) return new() { Exitoso = false, Mensaje = "Contacto inválido", StatusCode = 400 };

            m.CompanyId = eid;

            using var tx = _db.Database.BeginTransaction();
            try
            {
                _db.Messages.Add(m);
                _db.SaveChanges();

                var isAi =
                    (m.Sender != null) &&
                    (m.Sender.Equals("agent", System.StringComparison.OrdinalIgnoreCase) ||
                     m.Sender.Equals("ai", System.StringComparison.OrdinalIgnoreCase))
                    ? 1 : 0;

                _db.Database.ExecuteSqlRaw(@"
UPDATE conversations
SET total_messages   = total_messages + 1,
    ai_messages      = ai_messages + {0},
    last_activity_at = CASE 
        WHEN last_activity_at IS NULL OR last_activity_at < {1} THEN {1}
        ELSE last_activity_at
    END
WHERE id = {2} AND company_id = {3};",
                    isAi,
                    m.SentAt,
                    m.ConversationId,
                    eid
                );

                tx.Commit();
                return new() { Exitoso = true, Mensaje = "Creado", StatusCode = 201 };
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public DescriptiveBoolean Update(Message m)
        {
            var eid = EmpresaIdActual();
            var exists = _db.Messages
                .AsNoTracking()
                .Any(x => x.Id == m.Id && x.CompanyId == eid);

            if (!exists) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            if (m.ConversationId != 0)
            {
                var convOk = _db.Conversations
                    .AsNoTracking()
                    .Any(c => c.Id == m.ConversationId && c.CompanyId == eid);
                if (!convOk) return new() { Exitoso = false, Mensaje = "Conversación inválida", StatusCode = 400 };
            }

            if (m.ContactId != 0)
            {
                var contOk = _db.Contacts
                    .AsNoTracking()
                    .Any(c => c.Id == m.ContactId && c.CompanyId == eid);
                if (!contOk) return new() { Exitoso = false, Mensaje = "Contacto inválido", StatusCode = 400 };
            }

            m.CompanyId = eid;
            _db.Messages.Attach(m);
            _db.Entry(m).State = EntityState.Modified;
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Actualizado", StatusCode = 200 };
        }

        public DescriptiveBoolean Delete(int id)
        {
            var eid = EmpresaIdActual();
            var m = _db.Messages.FirstOrDefault(x => x.Id == id && x.CompanyId == eid);
            if (m == null) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            _db.Messages.Remove(m);
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Eliminado", StatusCode = 200 };
        }
    }
}
