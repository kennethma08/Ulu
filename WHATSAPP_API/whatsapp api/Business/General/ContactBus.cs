using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Helpers;
using Whatsapp_API.Infrastructure.MultiTenancy;

namespace Whatsapp_API.Business.General
{
    public class ContactBus
    {
        private readonly MyDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly TenantContext _tenant;

        public ContactBus(MyDbContext db, IHttpContextAccessor http, TenantContext tenant)
        {
            _db = db;
            _http = http;
            _tenant = tenant;
        }

        private int EmpresaIdActual()
        {
            // 1) Dentro del request (puesto por middleware/controlador)
            var items = _http?.HttpContext?.Items;
            if (items != null && items.TryGetValue("EMPRESA_ID", out var vObj))
            {
                if (vObj is int vi && vi > 0) return vi;
                if (vObj is string vs && int.TryParse(vs, out var vis) && vis > 0) return vis;
            }

            // 2) Contexto de tenant
            if (_tenant?.CompanyId > 0) return _tenant.CompanyId;

            // 3) Claims
            var http = _http?.HttpContext;
            string s = http?.User?.FindFirst("empresa_id")?.Value
                    ?? http?.User?.FindFirst("EmpresaId")?.Value;

            // 4) Headers
            if (string.IsNullOrWhiteSpace(s))
                s = http?.Request?.Headers["X-Empresa-Id"].FirstOrDefault()
                  ?? http?.Request?.Headers["X-Empresa"].FirstOrDefault();

            // 5) Querystring
            if (string.IsNullOrWhiteSpace(s))
                s = http?.Request?.Query["empresa_id"].FirstOrDefault()
                  ?? http?.Request?.Query["EmpresaId"].FirstOrDefault();

            return int.TryParse(s, out var id) ? id : 0;
        }

        public BooleanoDescriptivo<List<Contact>> List()
        {
            var eid = EmpresaIdActual();
            var list = _db.Contacts
                .AsNoTracking()
                .Where(x => x.CompanyId == eid)
                .ToList();

            return new() { Exitoso = true, Data = list, StatusCode = 200 };
        }

        public BooleanoDescriptivo<Contact> Find(int id)
        {
            var eid = EmpresaIdActual();
            var c = _db.Contacts
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == id && x.CompanyId == eid);

            return c == null
                ? new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 }
                : new() { Exitoso = true, Data = c, StatusCode = 200 };
        }

        public BooleanoDescriptivo<Contact> FindByPhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return new() { Exitoso = false, Mensaje = "Teléfono vacío", StatusCode = 400 };

            var eid = EmpresaIdActual();
            var c = _db.Contacts
                .AsNoTracking()
                .FirstOrDefault(x => x.CompanyId == eid && x.PhoneNumber == phone);

            return c == null
                ? new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 }
                : new() { Exitoso = true, Data = c, StatusCode = 200 };
        }

        public DescriptiveBoolean Create(Contact c)
        {
            var eid = EmpresaIdActual();
            c.CompanyId = eid;

            if (!string.IsNullOrWhiteSpace(c.PhoneNumber))
            {
                c.Country = PhoneNumberCountryHelper.ResolveIso2OrNull(c.PhoneNumber) ?? c.Country;
            }

            _db.Contacts.Add(c);
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Creado", StatusCode = 201 };
        }

        public DescriptiveBoolean Update(Contact c)
        {
            var eid = EmpresaIdActual();
            var exists = _db.Contacts
                .AsNoTracking()
                .Any(x => x.Id == c.Id && x.CompanyId == eid);

            if (!exists) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            c.CompanyId = eid;

            if (!string.IsNullOrWhiteSpace(c.PhoneNumber))
            {
                var iso2 = PhoneNumberCountryHelper.ResolveIso2OrNull(c.PhoneNumber);
                if (!string.IsNullOrWhiteSpace(iso2)) c.Country = iso2;
            }

            _db.Contacts.Attach(c);
            _db.Entry(c).State = EntityState.Modified;
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Actualizado", StatusCode = 200 };
        }

        public DescriptiveBoolean Delete(int id)
        {
            var eid = EmpresaIdActual();
            var c = _db.Contacts.FirstOrDefault(x => x.Id == id && x.CompanyId == eid);
            if (c == null) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            _db.Contacts.Remove(c);
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Eliminado", StatusCode = 200 };
        }
    }
}
