// Bussiness/Seguridad/PerfilBus.cs
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Entities.Security;
using Whatsapp_API.Models.Helpers;

namespace Whatsapp_API.Business.Security
{
    public class ProfileBus
    {
        private readonly MyDbContext _db;
        private readonly IHttpContextAccessor _http;

        public ProfileBus(MyDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        private int CurrentEmpresaId()
        {
            var s = _http.HttpContext?.User?.FindFirst("company_id")?.Value
                    ?? _http.HttpContext?.User?.FindFirst("CompanyId")?.Value;
            return int.TryParse(s, out var id) ? id : 0;
        }

        public BooleanoDescriptivo<List<Profile>> List()
        {
            var eid = CurrentEmpresaId();
            var list = _db.Profiles.AsNoTracking()
                .Where(p => p.CompanyId == eid)
                .OrderBy(p => p.Id)
                .ToList();

            return new() { Exitoso = true, Data = list, StatusCode = 200 };
        }

        public BooleanoDescriptivo<Profile> Find(int id)
        {
            var eid = CurrentEmpresaId();
            var p = _db.Profiles.AsNoTracking().FirstOrDefault(x => x.Id == id && x.CompanyId == eid);

            return p == null
                ? new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 }
                : new() { Exitoso = true, Data = p, StatusCode = 200 };
        }

        public Profile? FindByNombre(string nombre)
        {
            var eid = CurrentEmpresaId();
            if (string.IsNullOrWhiteSpace(nombre)) return null;

            return _db.Profiles.AsNoTracking()
                .FirstOrDefault(p =>
                    p.CompanyId == eid &&
                    p.Name != null &&
                    p.Name.Trim().ToLower() == nombre.Trim().ToLower());
        }

        public bool ExistsNombre(string nombre, int exceptId = 0)
        {
            var eid = CurrentEmpresaId();
            if (string.IsNullOrWhiteSpace(nombre)) return false;

            var q = _db.Profiles.AsNoTracking()
                .Where(p => p.CompanyId == eid && p.Name != null && p.Name.ToLower() == nombre.ToLower());

            if (exceptId > 0) q = q.Where(p => p.Id != exceptId);
            return q.Any();
        }

        public DescriptiveBoolean Create(Profile p)
        {
            var eid = CurrentEmpresaId();
            p.CompanyId = eid;

            _db.Profiles.Add(p);
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Creado", StatusCode = 201 };
        }

        public DescriptiveBoolean Update(Profile p)
        {
            var eid = CurrentEmpresaId();
            var exists = _db.Profiles.AsNoTracking().Any(x => x.Id == p.Id && x.CompanyId == eid);
            if (!exists) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            if (string.IsNullOrWhiteSpace(p.Name))
                return new() { Exitoso = false, Mensaje = "Nombre requerido", StatusCode = 400 };

            if (ExistsNombre(p.Name!, p.Id))
                return new() { Exitoso = false, Mensaje = "Ya existe un perfil con ese nombre", StatusCode = 409 };

            p.CompanyId = eid;
            _db.Profiles.Attach(p);
            _db.Entry(p).State = EntityState.Modified;
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Actualizado", StatusCode = 200 };
        }

        public DescriptiveBoolean Delete(int id)
        {
            var eid = CurrentEmpresaId();
            var p = _db.Profiles.FirstOrDefault(x => x.Id == id && x.CompanyId == eid);
            if (p == null) return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            _db.Profiles.Remove(p);
            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "Eliminado", StatusCode = 200 };
        }

        public BooleanoDescriptivo<List<Profile>> EnsureDefaults()
        {
            var eid = CurrentEmpresaId();
            var creados = new List<Profile>();
            var objetivos = new[] { "Agente", "Admin", "SuperAdmin" };

            foreach (var nombre in objetivos)
            {
                var exists = _db.Profiles.Any(p => p.CompanyId == eid && p.Name != null &&
                                                   p.Name.Trim().ToLower() == nombre.ToLower());
                if (!exists)
                {
                    var p = new Profile { Name = nombre, CompanyId = eid };
                    _db.Profiles.Add(p);
                    creados.Add(p);
                }
            }

            if (creados.Count > 0) _db.SaveChanges();

            var list = _db.Profiles.AsNoTracking()
                .Where(p => p.CompanyId == eid)
                .OrderBy(p => p.Id)
                .ToList();

            return new() { Exitoso = true, Data = list, StatusCode = 200 };
        }
    }
}
