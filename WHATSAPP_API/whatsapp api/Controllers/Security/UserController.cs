using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Whatsapp_API.Business.Security;
using Whatsapp_API.Business.VAMMP;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Request.DTO;
using Whatsapp_API.Models.Request.Security;
using Whatsapp_API.Models.Request.User;
using Whatsapp_API.Models.Entities.Security;

namespace Whatsapp_API.Controllers.Security
{
    [Produces("application/json")]
    [Route("api/seguridad/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class UserController : ControllerBase
    {
        private readonly UserBus _usuarioBus;
        private readonly ProfileBus _perfilBus;
        private readonly UserVAMMPBus _usuarioVampBus;
        private readonly EmailHelper _correoHelper;
        private readonly IConfiguration _cfg;

        public UserController(
            UserBus usuarioBus,
            EmailHelper correoHelper,
            UserVAMMPBus usuarioVAMMPBus,
            ProfileBus perfilBus,
            IConfiguration cfg)
        {
            _usuarioBus = usuarioBus;
            _correoHelper = correoHelper;
            _usuarioVampBus = usuarioVAMMPBus;
            _perfilBus = perfilBus;
            _cfg = cfg;
        }

        [HttpGet("{id:int}")]
        public ActionResult GetUsuario(int id)
        {
            try { return _usuarioBus.Find(id).StatusCodeDescriptivo(); }
            catch (Exception ex) { _correoHelper.EnviarCorreoError(ex, new { id }); return StatusCode(500, ex.Message); }
        }

        [HttpGet]
        public ActionResult Get()
        {
            try { return _usuarioBus.List().StatusCodeDescriptivo(); }
            catch (Exception ex) { _correoHelper.EnviarCorreoError(ex); return StatusCode(500, ex.Message); }
        }

        [HttpGet("by-perfil/{perfilNombre}")]
        public ActionResult GetByPerfilNombre(string perfilNombre)
        {
            try
            {
                var perfil = _perfilBus.FindByNombre(perfilNombre);
                if (perfil == null)
                    return NotFound(new { mensaje = $"Perfil '{perfilNombre}' no encontrado." });

                var all = _usuarioBus.List();
                if (!all.Exitoso || all.Data == null) return all.StatusCodeDescriptivo();

                var list = all.Data.Where(u => u.IdProfile == perfil.Id).ToList();

                return new BooleanoDescriptivo<List<User>>
                {
                    Exitoso = true,
                    Data = list,
                    StatusCode = 200
                }.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correoHelper.EnviarCorreoError(ex, new { perfilNombre });
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("by-perfil-id/{idPerfil:int}")]
        public ActionResult GetByPerfilId(int idPerfil)
        {
            try
            {
                var all = _usuarioBus.List();
                if (!all.Exitoso || all.Data == null) return all.StatusCodeDescriptivo();

                var list = all.Data.Where(u => u.IdProfile == idPerfil).ToList();

                return new BooleanoDescriptivo<List<User>>
                {
                    Exitoso = true,
                    Data = list,
                    StatusCode = 200
                }.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correoHelper.EnviarCorreoError(ex, new { idPerfil });
                return StatusCode(500, ex.Message);
            }
        }

        [AllowAnonymous]
        [HttpPost("upsert")]
        [Consumes("application/json")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(DescriptiveBoolean), StatusCodes.Status200OK)]
        public ActionResult Post(
            [FromBody] UserCreateUpdateRequest req,
            [FromQuery(Name = "empresa_id")] int? empresaIdQuery = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                var isAuth = User?.Identity?.IsAuthenticated == true;

                int companyId = 0;
                if (isAuth)
                {
                    companyId = GetEmpresaIdFromTokenOrHeader();
                }
                else
                {
                    if (empresaIdQuery.HasValue && empresaIdQuery.Value > 0)
                        companyId = empresaIdQuery.Value;
                    else
                        companyId = GetEmpresaIdFromHeaderOnly();

                    if (companyId <= 0)
                        return BadRequest(new { mensaje = "empresa_id no presente (query empresa_id o header X-Empresa-Id)." });
                }

                // ===== CREAR USUARIO =====
                if (req.Id == 0)
                {
                    var nuevo = new User
                    {
                        Name = req.Name,
                        Email = req.Email,
                        // IMPORTANTE: no hasheamos aquí; lo hace UserBus.Create
                        Pass = string.IsNullOrWhiteSpace(req.Pass) ? null : req.Pass,
                        Phone = req.Phone,
                        Status = req.Status ?? false,
                        IdProfile = req.IdProfile,
                        Company = req.Company,
                        CompanyId = companyId,
                        ContactId = req.ContactId,
                        IsOnline = req.IsOnline ?? false,
                        ConversationCount = req.ConversationCount ?? 0
                    };

                    var respCreate = _usuarioBus.Create(nuevo);
                    return respCreate.StatusCodeDescriptivo();
                }
                // ===== ACTUALIZAR USUARIO =====
                else
                {
                    if (!isAuth)
                        return Unauthorized(new { mensaje = "Actualización requiere autenticación (JWT)." });

                    var encontrado = _usuarioBus.Find(req.Id);
                    if (!encontrado.Exitoso || encontrado.Data == null)
                        return NotFound(new { mensaje = "Usuario no encontrado" });

                    var u = encontrado.Data;
                    u.Name = req.Name ?? u.Name;
                    u.Email = req.Email ?? u.Email;

                    // Si viene contraseña en el upsert, aquí sí la hasheamos una vez
                    if (!string.IsNullOrWhiteSpace(req.Pass))
                        u.Pass = PasswordHelper.HashPassword(req.Pass);

                    u.Phone = req.Phone ?? u.Phone;
                    u.Status = req.Status ?? u.Status;
                    u.IdProfile = req.IdProfile ?? u.IdProfile;
                    u.Company = req.Company ?? u.Company;
                    u.ContactId = req.ContactId ?? u.ContactId;
                    u.IsOnline = req.IsOnline ?? u.IsOnline;
                    u.ConversationCount = req.ConversationCount ?? u.ConversationCount;

                    var respUpdate = _usuarioBus.Update(u);
                    return respUpdate.StatusCodeDescriptivo();
                }
            }
            catch (Exception ex)
            {
                _correoHelper.EnviarCorreoError(ex, req);
                return new DescriptiveBoolean { Exitoso = false, Mensaje = "Error al crear/actualizar", StatusCode = 500 }
                    .StatusCodeDescriptivo();
            }
        }

        [HttpPost("{idUsuario:int}/set-perfil")]
        public ActionResult SetPerfil(int idUsuario, [FromBody] UserSetPerfilRequest req)
        {
            try
            {
                var encontrado = _usuarioBus.Find(idUsuario);
                if (!encontrado.Exitoso || encontrado.Data == null)
                    return NotFound(new { mensaje = "Usuario no encontrado" });

                var perfil = _perfilBus.Find(req.IdProfile);
                if (!perfil.Exitoso || perfil.Data == null)
                    return NotFound(new { mensaje = "Perfil no encontrado" });

                var u = encontrado.Data;
                u.IdProfile = req.IdProfile;

                var r = _usuarioBus.Update(u);
                return r.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correoHelper.EnviarCorreoError(ex, new { idUsuario, req });
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("Delete/{idUsuario:int}")]
        public ActionResult Delete(int idUsuario)
        {
            try { return _usuarioBus.Delete(idUsuario).StatusCodeDescriptivo(); }
            catch (Exception ex)
            {
                _correoHelper.EnviarCorreoError(ex, new { idUsuario });
                return new DescriptiveBoolean { Exitoso = false, Mensaje = "Error al eliminar", StatusCode = 500 }
                    .StatusCodeDescriptivo();
            }
        }

        [HttpGet("Empresa/{idUsuario:int}")]
        public ActionResult ObtenerDatosEmpresaDeUsuario(int idUsuario)
        {
            try { return _usuarioBus.ObtenerDatosEmpresaPorUsuario(idUsuario).StatusCodeDescriptivo(); }
            catch (Exception ex) { _correoHelper.EnviarCorreoError(ex, new { idUsuario }); return StatusCode(500, ex.Message); }
        }

        [HttpPost("SetExpoToken")]
        public ActionResult SetExpoToken([FromBody] SetExpoTokenRequest request)
        {
            try { return _usuarioBus.SetExpoToken(request.IdUser, request.ExpoToken).StatusCodeDescriptivo(); }
            catch (Exception ex) { _correoHelper.EnviarCorreoError(ex, request); return StatusCode(500, ex.Message); }
        }

        [HttpDelete("ClearExpoToken/{idUsuario:int}")]
        public ActionResult ClearExpoToken(int idUsuario)
        {
            try { return _usuarioBus.ClearExpoToken(idUsuario).StatusCodeDescriptivo(); }
            catch (Exception ex) { _correoHelper.EnviarCorreoError(ex, new { idUsuario }); return StatusCode(500, ex.Message); }
        }

        [HttpGet("ConsultaUsuarioVammp/{correo}")]
        public async Task<ActionResult> ConsultaUsuarioVammp([FromQuery] string token, string correo)
        {
            try
            {
                var respuesta = await _usuarioVampBus.ConsultaUsuarioVammp(token, correo);
                return respuesta.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correoHelper.EnviarCorreoError(ex, new { correo });
                return StatusCode(500, ex.Message);
            }
        }

        private int GetEmpresaIdFromTokenOrHeader()
        {
            var s = User.FindFirst("empresa_id")?.Value
                 ?? User.FindFirst("EmpresaId")?.Value
                 ?? Request.Headers["X-Empresa-Id"].FirstOrDefault();

            return int.TryParse(s, out var id) ? id : 0;
        }

        private int GetEmpresaIdFromHeaderOnly()
        {
            var s = Request.Headers["X-Empresa-Id"].FirstOrDefault();
            return int.TryParse(s, out var id) ? id : 0;
        }

        public class UpdateNombreRequest { public string Nombre { get; set; } = ""; }

        [HttpPatch("{idUsuario:int}/nombre")]
        public ActionResult UpdateNombre(int idUsuario, [FromBody] UpdateNombreRequest req)
        {
            try
            {
                if (idUsuario <= 0 || string.IsNullOrWhiteSpace(req?.Nombre))
                    return BadRequest(new { mensaje = "Parámetros inválidos." });

                var r = _usuarioBus.UpdateNombre(idUsuario, req.Nombre.Trim());
                return r.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correoHelper.EnviarCorreoError(ex, new { idUsuario, req });
                return StatusCode(500, ex.Message);
            }
        }

        public class ChangePasswordRequest
        {
            public string CurrentPassword { get; set; } = "";
            public string NewPassword { get; set; } = "";
        }

        [HttpPost("{idUsuario:int}/change-password")]
        public ActionResult ChangePassword(int idUsuario, [FromBody] ChangePasswordRequest req)
        {
            try
            {
                if (idUsuario <= 0 ||
                    string.IsNullOrWhiteSpace(req?.CurrentPassword) ||
                    string.IsNullOrWhiteSpace(req.NewPassword))
                    return BadRequest(new { mensaje = "Parámetros inválidos." });

                var encontrado = _usuarioBus.Find(idUsuario);
                if (!encontrado.Exitoso || encontrado.Data == null)
                    return NotFound(new { mensaje = "Usuario no encontrado" });

                var u = encontrado.Data;

                // Verificamos la contraseña actual
                if (!PasswordHelper.VerifyPassword(req.CurrentPassword, u.Pass))
                {
                    return Unauthorized(new { mensaje = "La contraseña actual no es correcta." });
                }

                // Hash de la nueva contraseña
                var newHash = PasswordHelper.HashPassword(req.NewPassword);

                // Usamos un método específico para actualizar SOLO el password
                var resp = _usuarioBus.UpdatePassword(idUsuario, newHash);
                return resp.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correoHelper.EnviarCorreoError(ex, new { idUsuario, req });
                return StatusCode(500, ex.Message);
            }
        }
    }
}
