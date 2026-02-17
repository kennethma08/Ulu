using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Infrastructure.MultiTenancy;
using Whatsapp_API.Models.Entities.Security;
using Whatsapp_API.Helpers;

namespace Whatsapp_API.Business.Security
{
    public class UserBus
    {
        private readonly MyDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly TenantContext _tenant;

        public UserBus(MyDbContext db, IHttpContextAccessor http, TenantContext tenant)
        {
            _db = db;
            _http = http;
            _tenant = tenant;
        }

        private int CurrentEmpresaId()
        {
            if (_tenant?.CompanyId > 0) return _tenant.CompanyId;

            var http = _http.HttpContext;

            var s = http?.User?.FindFirst("empresa_id")?.Value
                 ?? http?.User?.FindFirst("EmpresaId")?.Value;

            if (string.IsNullOrWhiteSpace(s))
                s = http?.Request?.Headers["X-Empresa-Id"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(s))
                s = http?.Request?.Query["empresa_id"].FirstOrDefault()
                  ?? http?.Request?.Query["EmpresaId"].FirstOrDefault();

            return int.TryParse(s, out var id) ? id : 0;
        }

        public BooleanoDescriptivo<List<User>> List()
        {
            var eid = CurrentEmpresaId();
            var list = _db.Users
                .AsNoTracking()
                .Where(x => x.CompanyId == eid)
                .ToList();

            return new()
            {
                Exitoso = true,
                Data = list,
                StatusCode = 200
            };
        }

        public BooleanoDescriptivo<User> Find(int id)
        {
            var eid = CurrentEmpresaId();
            var u = _db.Users
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == id && x.CompanyId == eid);

            return u == null
                ? new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 }
                : new() { Exitoso = true, Data = u, StatusCode = 200 };
        }

        public DescriptiveBoolean Create(User u)
        {
            var eid = CurrentEmpresaId();

            if (!u.CompanyId.HasValue || u.CompanyId <= 0)
                u.CompanyId = eid;

            // ==== Validar correo único por empresa (crear) ====
            if (!string.IsNullOrWhiteSpace(u.Email))
            {
                var emailNorm = u.Email.Trim().ToLower();
                var companyForCheck = u.CompanyId ?? eid;

                var exists = _db.Users
                    .AsNoTracking()
                    .Any(x =>
                        x.CompanyId == companyForCheck &&
                        x.Email != null &&
                        x.Email.ToLower() == emailNorm);

                if (exists)
                {
                    return new DescriptiveBoolean
                    {
                        Exitoso = false,
                        Mensaje = "Ya existe un usuario con ese correo en esta empresa.",
                        StatusCode = 409
                    };
                }

                u.Email = u.Email.Trim();
            }
            // ==== fin validación correo crear ====

            if (!string.IsNullOrWhiteSpace(u.Pass))
                u.Pass = PasswordHelper.HashPassword(u.Pass);

            _db.Users.Add(u);
            _db.SaveChanges();

            return new()
            {
                Exitoso = true,
                Mensaje = "Creado",
                StatusCode = 201
            };
        }

        public DescriptiveBoolean Update(User u)
        {
            var eid = CurrentEmpresaId();

            var actual = _db.Users
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == u.Id);

            if (actual == null)
                return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            if (eid > 0 && actual.CompanyId != eid)
                return new() { Exitoso = false, Mensaje = "No pertenece a la empresa actual", StatusCode = 403 };

            // ==== Validar correo único por empresa (actualizar) ====
            if (!string.IsNullOrWhiteSpace(u.Email))
            {
                var emailNorm = u.Email.Trim().ToLower();
                var companyForCheck = actual.CompanyId ?? eid;

                var exists = _db.Users
                    .AsNoTracking()
                    .Any(x =>
                        x.Id != u.Id &&
                        x.CompanyId == companyForCheck &&
                        x.Email != null &&
                        x.Email.ToLower() == emailNorm);

                if (exists)
                {
                    return new DescriptiveBoolean
                    {
                        Exitoso = false,
                        Mensaje = "Ya existe un usuario con ese correo en esta empresa.",
                        StatusCode = 409
                    };
                }

                u.Email = u.Email.Trim();
            }
            // ==== fin validación correo actualizar ====

            // Mantener la empresa original
            u.CompanyId = actual.CompanyId;

            _db.Users.Attach(u);
            _db.Entry(u).State = EntityState.Modified;
            _db.SaveChanges();

            return new()
            {
                Exitoso = true,
                Mensaje = "Actualizado",
                StatusCode = 200
            };
        }

        public DescriptiveBoolean Delete(int idUsuario)
        {
            var eid = CurrentEmpresaId();
            var u = _db.Users
                .FirstOrDefault(x => x.Id == idUsuario && x.CompanyId == eid);

            if (u == null)
                return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

            _db.Users.Remove(u);
            _db.SaveChanges();

            return new()
            {
                Exitoso = true,
                Mensaje = "Eliminado",
                StatusCode = 200
            };
        }

        public DescriptiveBoolean SetExpoToken(int idUsuario, string expoToken)
        {
            var eid = CurrentEmpresaId();
            var u = _db.Users
                .FirstOrDefault(x => x.Id == idUsuario && x.CompanyId == eid);

            if (u == null)
                return new() { Exitoso = false, Mensaje = "Usuario no encontrado", StatusCode = 404 };

            // u.ExpoToken = expoToken;
            _db.SaveChanges();

            return new()
            {
                Exitoso = true,
                Mensaje = "ExpoToken asignado",
                StatusCode = 200
            };
        }

        public DescriptiveBoolean ClearExpoToken(int idUsuario)
        {
            var eid = CurrentEmpresaId();
            var u = _db.Users
                .FirstOrDefault(x => x.Id == idUsuario && x.CompanyId == eid);

            if (u == null)
                return new() { Exitoso = false, Mensaje = "Usuario no encontrado", StatusCode = 404 };

            // u.ExpoToken = null;
            _db.SaveChanges();

            return new()
            {
                Exitoso = true,
                Mensaje = "ExpoToken eliminado",
                StatusCode = 200
            };
        }

        public DescriptiveBoolean ObtenerDatosEmpresaPorUsuario(int idUsuario)
        {
            var eid = CurrentEmpresaId();
            var u = _db.Users
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == idUsuario && x.CompanyId == eid);

            if (u == null)
                return new() { Exitoso = false, Mensaje = "Usuario no encontrado", StatusCode = 404 };

            var obj = new { company = u.Company, company_id = u.CompanyId };

            return new BooleanoDescriptivo<object>
            {
                Exitoso = true,
                Data = obj,
                StatusCode = 200
            };
        }

        public DescriptiveBoolean UpdateNombre(int idUsuario, string nuevoNombre)
        {
            var eid = CurrentEmpresaId();
            var u = _db.Users
                .FirstOrDefault(x => x.Id == idUsuario && x.CompanyId == eid);

            if (u == null)
                return new() { Exitoso = false, Mensaje = "Usuario no encontrado", StatusCode = 404 };

            u.Name = nuevoNombre;
            _db.SaveChanges();

            return new()
            {
                Exitoso = true,
                Mensaje = "Nombre actualizado",
                StatusCode = 200
            };
        }

        // ====== NUEVO: actualización solo de contraseña ======
        public DescriptiveBoolean UpdatePassword(int idUsuario, string newPasswordHash)
        {
            var eid = CurrentEmpresaId();
            var u = _db.Users
                .FirstOrDefault(x => x.Id == idUsuario && x.CompanyId == eid);

            if (u == null)
            {
                return new DescriptiveBoolean
                {
                    Exitoso = false,
                    Mensaje = "Usuario no encontrado",
                    StatusCode = 404
                };
            }

            u.Pass = newPasswordHash;
            _db.SaveChanges();

            return new DescriptiveBoolean
            {
                Exitoso = true,
                Mensaje = "Contraseña actualizada",
                StatusCode = 200
            };
        }
    }
}
