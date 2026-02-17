using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Infrastructure.MultiTenancy;
using Whatsapp_API.Models.Entities.System;

namespace Whatsapp_API.Business.Integrations
{
    public class WhatsappTemplateBus
    {
        private readonly MyDbContext _db;
        private readonly TenantContext _tenant;

        public WhatsappTemplateBus(MyDbContext db, TenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public BooleanoDescriptivo<List<WhatsappTemplate>> List()
            => new()
            {
                Exitoso = true,
                Data = _db.WhatsappTemplates
                          .AsNoTracking()
                          .OrderBy(x => x.Name)
                          .ToList(),
                StatusCode = 200
            };

        public BooleanoDescriptivo<WhatsappTemplate> Find(int id)
        {
            var t = _db.WhatsappTemplates
                       .AsNoTracking()
                       .FirstOrDefault(x => x.Id == id);

            return t == null
                ? new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 }
                : new() { Exitoso = true, Data = t, StatusCode = 200 };
        }

        public WhatsappTemplate? FindByNameLang(string name, string lang)
            => _db.WhatsappTemplates
                  .AsNoTracking()
                  .FirstOrDefault(x =>
                      x.Name.ToLower() == name.ToLower() &&
                      x.Language.ToLower() == lang.ToLower());

        // >>> NUEVO: usado por ZaifuFlow
        public WhatsappTemplate? FindActiveByEmpresaAndName(int companyId, string name)
        {
            return _db.WhatsappTemplates
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && x.IsActive
                            && x.Name.ToLower() == name.ToLower())
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .FirstOrDefault();
        }

        public DescriptiveBoolean Upsert(WhatsappTemplate t)
        {
            if (t.Id == 0)
            {
                t.CompanyId = _tenant.CompanyId;
                _db.WhatsappTemplates.Add(t);
                _db.SaveChanges();
                return new() { Exitoso = true, Mensaje = "Creado", StatusCode = 201 };
            }
            else
            {
                var cur = _db.WhatsappTemplates.FirstOrDefault(x => x.Id == t.Id);
                if (cur == null)
                    return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

                cur.Name = t.Name;
                cur.Language = t.Language;
                cur.TemplateIdentifier = t.TemplateIdentifier;
                cur.BodyParamCount = t.BodyParamCount;
                cur.IsActive = t.IsActive;
                cur.UpdatedAt = DateTime.UtcNow;
                _db.SaveChanges();
                return new() { Exitoso = true, Mensaje = "Actualizado", StatusCode = 200 };
            }
        }

        public DescriptiveBoolean Delete(int id)
        {
            var cur = _db.WhatsappTemplates.FirstOrDefault(x => x.Id == id);
            if (cur == null) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };
            _db.WhatsappTemplates.Remove(cur);
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Eliminado", StatusCode = 200 };
        }
    }
}
