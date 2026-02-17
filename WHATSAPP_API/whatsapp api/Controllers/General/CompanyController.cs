using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Entities.System;
using Whatsapp_API.Models.Request.General;

namespace Whatsapp_API.Controllers.General
{

    // empresas listar, ver una y crear/editar

    [Produces("application/json")]
    [Route("api/general/[controller]")]
    [ApiController]
    public class CompanyController : ControllerBase
    {
        private readonly MyDbContext _db;
        private readonly EmailHelper _correo;

        public CompanyController(MyDbContext db, EmailHelper correo)
        {
            _db = db;
            _correo = correo;
        }

        // todas las empresas
        [HttpGet]
        public async Task<ActionResult> Get()
        {
            try
            {
                var data = await _db.Companies.AsNoTracking().ToListAsync();
                return Ok(new { exitoso = true, data });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex);
                return StatusCode(500, ex.Message);
            }
        }

        // una por id
        [HttpGet("{id:int}")]
        public async Task<ActionResult> Get(int id)
        {
            try
            {
                var e = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
                if (e == null) return NotFound(new { mensaje = "Empresa no encontrada" });
                return Ok(new { exitoso = true, data = e });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id });
                return StatusCode(500, ex.Message);
            }
        }

        // crear o actualizar
        [HttpPost("upsert")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult> Upsert([FromBody] CompanyUpsertRequest req)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                if (req.Id == 0)
                {
                    if (string.IsNullOrWhiteSpace(req.Name))
                        return BadRequest(new { mensaje = "Nombre requerido" });

                    var e = new Company
                    {
                        Name = req.Name!.Trim(),
                        FlowKey = (req.FlowKey ?? "default").Trim()
                    };
                    _db.Companies.Add(e);
                    await _db.SaveChangesAsync();
                    return StatusCode(201, new { exitoso = true, data = e });
                }
                else
                {
                    var e = await _db.Companies.FirstOrDefaultAsync(x => x.Id == req.Id);
                    if (e == null) return NotFound(new { mensaje = "Empresa no encontrada" });

                    if (!string.IsNullOrWhiteSpace(req.Name)) e.Name = req.Name.Trim();
                    if (!string.IsNullOrWhiteSpace(req.FlowKey)) e.FlowKey = req.FlowKey.Trim();

                    await _db.SaveChangesAsync();
                    return Ok(new { exitoso = true, data = e });
                }
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return StatusCode(500, new { exitoso = false, mensaje = "Error al crear/actualizar" });
            }
        }
    }
}
