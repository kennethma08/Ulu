using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsappClient.Models;
using WhatsappClient.Services;

namespace WhatsappClient.Controllers
{
    [Authorize]
    public class ContactController : Controller
    {
        private readonly ApiService _apiService;

        public ContactController(ApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task<IActionResult> Index()
        {
            var contactos = await _apiService.ObtenerContactosAsync();
            return View(contactos);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> ActualizarNombre([FromForm] int id, [FromForm] string nombre)
        {
            if (id <= 0 || string.IsNullOrWhiteSpace(nombre))
                return BadRequest("Parámetros inválidos.");

            var ok = await _apiService.UpdateNombreContactoAsync(id, nombre.Trim());
            if (!ok) return StatusCode(500, "No se pudo actualizar el nombre.");
            return Ok(new { mensaje = "Actualizado" });
        }
    }
}
