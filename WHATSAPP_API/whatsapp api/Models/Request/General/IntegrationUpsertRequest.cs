using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.General
{
    public class IntegrationUpsertRequest
    {
        public int Id { get; set; } 

        [Required, MaxLength(50)]
        public string Provider { get; set; } = "whatsapp_cloud";

        [Required]
        public string Phone_Number_Id { get; set; } = "";

        public string? Waba_Id { get; set; }


        public string? Access_Token { get; set; }
        public string? Verify_Token { get; set; }

        public string? Api_Base_Url { get; set; } 
        public string? Api_Version { get; set; } 
        public bool? Is_Active { get; set; }
    }
}
