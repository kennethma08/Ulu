using System;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Request.General;
using Whatsapp_API.Infrastructure.MultiTenancy;
using Whatsapp_API.Models.Entities.System;

namespace Whatsapp_API.Business.Integrations
{

    // maneja la config de integración de whatsapp por empresa

    public class IntegrationBus
    {
        private readonly MyDbContext _db;
        private readonly TenantContext _tenant;

        public IntegrationBus(MyDbContext db, TenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        // trae la integración activa de la empresa (SOLO la más reciente), con el token enmascarado para no exponerlo y por seguridad

        public BooleanoDescriptivo<IntegrationViewResponse> GetActive(string provider = "whatsapp_cloud")
        {
            var eid = _tenant.CompanyId;

            var it = _db.Integrations.AsNoTracking()
                .Where(x => x.CompanyId == eid)
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .FirstOrDefault(x => x.Provider == provider && x.IsActive);

            if (it == null)
                return new() { Exitoso = false, Mensaje = "No configurado", StatusCode = 404 };

            // solo se va mostrar los ultms 4 del token, lo demás con asteriscos


            string masked = "";
            if (it.AccessTokenEnc != null && it.AccessTokenEnc.Length > 0)
            {
                var token = Encoding.UTF8.GetString(it.AccessTokenEnc);
                masked = token.Length <= 4 ? "****" : new string('*', token.Length - 4) + token[^4..];
            }

            return new()
            {
                Exitoso = true,
                StatusCode = 200,
                Data = new IntegrationViewResponse
                {
                    Id = it.Id,
                    Provider = it.Provider,
                    PhoneNumberId = it.PhoneNumberId,
                    WabaId = it.WabaId,
                    ApiBaseUrl = it.ApiBaseUrl,
                    ApiVersion = it.ApiVersion,
                    IsActive = it.IsActive,
                    AccessTokenMasked = masked,
                    HasVerifyToken = !string.IsNullOrEmpty(it.VerifyTokenHash),
                    UpdatedAt = it.UpdatedAt
                }
            };
        }

        public DescriptiveBoolean Upsert(IntegrationUpsertRequest req)
        {
            var eid = _tenant.CompanyId;
            var now = DateTime.UtcNow;
            Integration it;

            if (req.Id == 0)
            {
                it = new Integration
                {
                    CompanyId = eid,
                    Provider = req.Provider ?? "whatsapp_cloud",
                    PhoneNumberId = req.Phone_Number_Id,
                    WabaId = req.Waba_Id,
                    ApiBaseUrl = req.Api_Base_Url ?? "https://graph.facebook.com",
                    ApiVersion = req.Api_Version ?? "v20.0",
                    IsActive = req.Is_Active ?? true,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                if (!string.IsNullOrEmpty(req.Access_Token))
                    it.AccessTokenEnc = Encoding.UTF8.GetBytes(req.Access_Token);

                if (!string.IsNullOrEmpty(req.Verify_Token))
                    it.VerifyTokenHash = req.Verify_Token;

                _db.Integrations.Add(it);
            }
            else
            {
                it = _db.Integrations
                    .FirstOrDefault(x => x.Id == req.Id && x.CompanyId == eid);

                if (it == null)
                    return new() { Exitoso = false, Mensaje = "No encontrado", StatusCode = 404 };

                it.Provider = req.Provider ?? it.Provider;
                it.PhoneNumberId = req.Phone_Number_Id ?? it.PhoneNumberId;
                it.WabaId = req.Waba_Id ?? it.WabaId;
                it.ApiBaseUrl = req.Api_Base_Url ?? it.ApiBaseUrl;
                it.ApiVersion = req.Api_Version ?? it.ApiVersion;
                if (req.Is_Active.HasValue) it.IsActive = req.Is_Active.Value;

                if (!string.IsNullOrEmpty(req.Access_Token))
                    it.AccessTokenEnc = Encoding.UTF8.GetBytes(req.Access_Token);

                if (!string.IsNullOrEmpty(req.Verify_Token))
                    it.VerifyTokenHash = req.Verify_Token;

                it.UpdatedAt = now;
                _db.Integrations.Update(it);
            }

            _db.SaveChanges();
            return new() { Exitoso = true, Mensaje = "OK", StatusCode = req.Id == 0 ? 201 : 200 };
        }

        // datos listos para armar requests al graph (token y urls); si no hay activo, devolvemos error simple

        public (bool ok, string token, string baseUrl, string version, string phoneId, string? err) GetDecryptedForSend()
        {
            var r = GetActive();
            if (!r.Exitoso || r.Data == null) return (false, "", "", "", "", "No hay integración activa");

            var eid = _tenant.CompanyId;

            // confirmamos contra bd por si cambió algo entre el get y el uso

            var it = _db.Integrations
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == r.Data.Id && x.CompanyId == eid);

            if (it?.AccessTokenEnc == null || it.AccessTokenEnc.Length == 0)
                return (false, "", "", "", "", "Token no configurado");

            var token = Encoding.UTF8.GetString(it.AccessTokenEnc);

            return (true, token, it.ApiBaseUrl, it.ApiVersion, it.PhoneNumberId, null);
        }

        // devuelve el verify token en crudo para la verificación del webhook (GET de meta)
        public string? GetVerifyTokenRaw()
        {
            var eid = _tenant.CompanyId;
            var it = _db.Integrations
                .AsNoTracking()
                .Where(x => x.CompanyId == eid && x.Provider == "whatsapp_cloud" && x.IsActive)
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .FirstOrDefault();
            return it?.VerifyTokenHash;
        }

    }
}
