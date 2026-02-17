using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.Authentication
{
    public class LoginRequest
    {
        /// <summary>
        /// Nombre de usuario para iniciar sesión.
        /// </summary>
        [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "El nombre de usuario debe tener entre 3 y 50 caracteres.")]
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// Contraseña del usuario.
        /// </summary>
        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        public string Password { get; set; } = string.Empty;
        public bool LoginApp { get; set; } = false;

    }
}