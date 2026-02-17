using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.User
{
    public class RecoverPasswordRequest
    {
        /// <summary>
        /// Correo electrónico del usuario para recuperar la contraseña.
        /// </summary>
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "El correo de usuario debe tener entre 3 y 50 caracteres.")]
        [EmailAddress(ErrorMessage = "El correo no tiene un formato valido")]
        public string? email { get; set; }
    }
}
