using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.General
{
    public class SendTemplateRequest
    {
        public string? To_Phone { get; set; }
        public int? Contact_Id { get; set; }
        public int? Agent_Id { get; set; }
        public int? Conversation_Id { get; set; }
        public bool Create_If_Not_Exists { get; set; } = true;

        /// <summary>Nombre del template en Meta.</summary>
        [Required]
        public string Template_Name { get; set; } = "";

        /// <summary>Código de idioma (ej: es, es_MX, en_US).</summary>
        [Required]
        public string Language { get; set; } = "es";

        /// <summary>Variables del body (opcional).</summary>
        public List<string>? Body_Vars { get; set; }

        /// <summary>Si true, registra el mensaje en la base.</summary>
        public bool Log { get; set; } = true;

        //Header location
        public double? Header_Location_Latitude { get; set; }
        public double? Header_Location_Longitude { get; set; }
        public string? Header_Location_Name { get; set; }
        public string? Header_Location_Address { get; set; }
    }
}
