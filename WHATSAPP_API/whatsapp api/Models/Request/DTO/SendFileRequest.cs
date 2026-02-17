using Microsoft.AspNetCore.Mvc;

namespace Whatsapp_API.Models.Request.DTO
{
    public class SendFileRequest
    {
        [FromForm(Name = "file")]
        public IFormFile File { get; set; } = default!;

        [FromForm(Name = "to")]
        public string To { get; set; } = "";

        [FromForm(Name = "caption")]
        public string? Caption { get; set; }
    }

}
