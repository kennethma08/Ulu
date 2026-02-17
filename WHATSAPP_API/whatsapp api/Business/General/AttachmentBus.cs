using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;
using Whatsapp_API.Infrastructure.MultiTenancy;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Models.Helpers;

namespace Whatsapp_API.Business.General
{
    public class AttachmentBus
    {
        private readonly MyDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly TenantContext _tenant;

        public AttachmentBus(MyDbContext db, IHttpContextAccessor http, TenantContext tenant)
        {
            _db = db;
            _http = http;
            _tenant = tenant;
        }

        private int EmpresaIdActual()
        {
            var items = _http?.HttpContext?.Items;
            if (items != null)
            {
                if (items.TryGetValue("COMPANY_ID", out var vc))
                {
                    if (vc is int vi && vi > 0) return vi;
                    if (vc is string vs && int.TryParse(vs, out var vis) && vis > 0) return vis;
                }
                if (items.TryGetValue("EMPRESA_ID", out var ve))
                {
                    if (ve is int vei && vei > 0) return vei;
                    if (ve is string ves && int.TryParse(ves, out var vesi) && vesi > 0) return vesi;
                }
            }

            if (_tenant?.CompanyId > 0) return _tenant.CompanyId;

            var http = _http?.HttpContext;

            string s =
                   http?.User?.FindFirst("company_id")?.Value
                ?? http?.User?.FindFirst("CompanyId")?.Value
                ?? http?.User?.FindFirst("empresa_id")?.Value
                ?? http?.User?.FindFirst("EmpresaId")?.Value;

            if (string.IsNullOrWhiteSpace(s))
                s = http?.Request?.Headers["X-Company-Id"].FirstOrDefault()
                  ?? http?.Request?.Headers["X-Company"].FirstOrDefault()
                  ?? http?.Request?.Headers["X-Empresa-Id"].FirstOrDefault()
                  ?? http?.Request?.Headers["X-Empresa"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(s))
                s = http?.Request?.Query["company_id"].FirstOrDefault()
                  ?? http?.Request?.Query["CompanyId"].FirstOrDefault()
                  ?? http?.Request?.Query["empresa_id"].FirstOrDefault()
                  ?? http?.Request?.Query["EmpresaId"].FirstOrDefault();

            return int.TryParse(s, out var id) ? id : 0;
        }

        public BooleanoDescriptivo<List<Attachment>> List()
        {
            var eid = EmpresaIdActual();
            var list = _db.Attachments
                .AsNoTracking()
                .Where(x => x.CompanyId == eid)
                .ToList();

            return new() { Exitoso = true, Data = list, StatusCode = 200 };
        }

        public BooleanoDescriptivo<Attachment> Find(int id)
        {
            var eid = EmpresaIdActual();
            var a = _db.Attachments
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == id && x.CompanyId == eid);

            return a == null
                ? new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 }
                : new() { Exitoso = true, Data = a, StatusCode = 200 };
        }

        public DescriptiveBoolean Create(Attachment a)
        {
            var eid = EmpresaIdActual();

            var msgOk = _db.Messages
                .AsNoTracking()
                .Any(m => m.Id == a.MessageId && m.CompanyId == eid);
            if (!msgOk) return new() { Exitoso = false, Mensaje = "Mensaje inválido", StatusCode = 400 };

            a.CompanyId = eid;

            _db.Attachments.Add(a);
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Creado", StatusCode = 201 };
        }

        public DescriptiveBoolean Update(Attachment a)
        {
            var eid = EmpresaIdActual();
            var exists = _db.Attachments
                .AsNoTracking()
                .Any(x => x.Id == a.Id && x.CompanyId == eid);

            if (!exists) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            if (a.MessageId != 0)
            {
                var msgOk = _db.Messages
                    .AsNoTracking()
                    .Any(m => m.Id == a.MessageId && m.CompanyId == eid);
                if (!msgOk) return new() { Exitoso = false, Mensaje = "Mensaje inválido", StatusCode = 400 };
            }

            a.CompanyId = eid;
            _db.Attachments.Attach(a);
            _db.Entry(a).State = EntityState.Modified;
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Actualizado", StatusCode = 200 };
        }

        public DescriptiveBoolean Delete(int id)
        {
            var eid = EmpresaIdActual();
            var a = _db.Attachments.FirstOrDefault(x => x.Id == id && x.CompanyId == eid);
            if (a == null) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            _db.Attachments.Remove(a);
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Eliminado", StatusCode = 200 };
        }

        // ===== NUEVO: adjuntos por conversación =====
        public BooleanoDescriptivo<List<Attachment>> ListByConversation(int conversationId)
        {
            var eid = EmpresaIdActual();

            var list = (
                from a in _db.Attachments.AsNoTracking()
                join m in _db.Messages.AsNoTracking()
                    on a.MessageId equals m.Id
                where a.CompanyId == eid
                   && m.CompanyId == eid
                   && m.ConversationId == conversationId
                select a
            ).ToList();

            return new()
            {
                Exitoso = true,
                Data = list,
                StatusCode = 200
            };
        }
    }
}
