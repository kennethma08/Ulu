using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.User
{
    public class ChangePasswordRequest
    {
        public int UserId { get; set; }
        public string NewPassword { get; set; }
    }
}
