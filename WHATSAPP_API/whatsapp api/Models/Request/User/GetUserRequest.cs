using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.User
{
    public class GetUserRequest
    {
        /// <summary>
        /// Nombre de usuario para iniciar sesión.
        /// </summary>
        [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "El nombre de usuario debe tener entre 3 y 50 caracteres.")]
        public string Name { get; set; } = string.Empty;
    }
}
