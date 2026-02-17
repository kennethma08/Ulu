using Microsoft.AspNetCore.Http;

namespace Whatsapp_API.Models.Request.General
{
    public class AttachmentUploadForm
    {
        public int MessageId { get; set; }   // campo form: messageId
        public IFormFile File { get; set; }  // campo form: file
    }
}
