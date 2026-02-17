using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using Whatsapp_API.Business.General;
using Whatsapp_API.Business.Whatsapp;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Models.Request.General;

namespace Whatsapp_API.Controllers.General
{
    [Produces("application/json")]
    [Route("api/general/[controller]")]
    [ApiController]
    public class AttachmentController : ControllerBase
    {
        private readonly AttachmentBus _bus;
        private readonly EmailHelper _correo;
        private readonly WhatsappMediaOnDemandService _whatsappMedia;

        public AttachmentController(
            AttachmentBus bus,
            EmailHelper correo,
            WhatsappMediaOnDemandService whatsappMedia)
        {
            _bus = bus;
            _correo = correo;
            _whatsappMedia = whatsappMedia;
        }

        // =========================================================
        // LISTA GENERAL (por empresa)
        // =========================================================
        [HttpGet]
        public ActionResult Get()
        {
            try
            {
                return _bus.List().StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex);
                return StatusCode(500, ex.Message);
            }
        }

        // =========================================================
        // OBTENER POR ID
        // =========================================================
        [HttpGet("{id:int}")]
        public ActionResult Get(int id)
        {
            try
            {
                return _bus.Find(id).StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id });
                return StatusCode(500, ex.Message);
            }
        }

        // =========================================================
        // LISTAR ADJUNTOS POR CONVERSACIÓN
        // GET api/general/attachment/by-conversation/123
        // =========================================================
        [HttpGet("by-conversation/{conversationId:int}")]
        public ActionResult GetByConversation(int conversationId)
        {
            try
            {
                return _bus.ListByConversation(conversationId).StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { conversationId });
                return StatusCode(500, ex.Message);
            }
        }

        // =========================================================
        // DEVOLVER BINARIO (para <audio>, descargas, etc.)
        // GET api/general/attachment/5/content
        // =========================================================
        [HttpGet("{id:int}/content")]
        public async Task<ActionResult> DownloadContent(int id)
        {
            try
            {
                var found = _bus.Find(id);
                if (!found.Exitoso || found.Data == null)
                    return NotFound(new { mensaje = "Adjunto no encontrado" });

                var a = found.Data;

                // 1) Si ya hay binario en BD (modo antiguo), se usa tal cual
                if (a.Data != null && a.Data.Length > 0)
                {
                    var mimeDb = string.IsNullOrWhiteSpace(a.MimeType)
                        ? "application/octet-stream"
                        : a.MimeType!;

                    var fileNameDb = string.IsNullOrWhiteSpace(a.FileName)
                        ? $"file_{a.Id}"
                        : a.FileName!;

                    return File(a.Data, mimeDb, fileNameDb);
                }

                // 2) Si no hay binario y el adjunto viene de WhatsApp, se baja on-demand
                var isWhatsapp =
                    !string.IsNullOrWhiteSpace(a.StorageProvider) &&
                    a.StorageProvider.Equals("whatsapp", StringComparison.OrdinalIgnoreCase);

                if (isWhatsapp && !string.IsNullOrWhiteSpace(a.WhatsappMediaId))
                {
                    var companyId = a.CompanyId; // viene de tu tabla attachments

                    var (bytes, mimeType, error) =
                        await _whatsappMedia.DownloadMediaAsync(companyId, a.WhatsappMediaId);

                    if (bytes == null || bytes.Length == 0)
                    {
                        return NotFound(new
                        {
                            mensaje = error ?? "No se pudo descargar el archivo desde WhatsApp (puede que haya expirado en Meta)."
                        });
                    }

                    var finalMime = !string.IsNullOrWhiteSpace(mimeType)
                        ? mimeType!
                        : (string.IsNullOrWhiteSpace(a.MimeType) ? "application/octet-stream" : a.MimeType!);

                    var finalFileName = string.IsNullOrWhiteSpace(a.FileName)
                        ? $"audio_{a.WhatsappMediaId}.ogg"
                        : a.FileName!;

                    // *** MODO ON DEMAND ***
                    // NO se guarda en BD, solo se devuelve el archivo cada vez que se pida.
                    // Si quisieras cachear, aquí podrías actualizar a.Data y guardar.
                    return File(bytes, finalMime, finalFileName);
                }

                // 3) No hay Data y no es WhatsApp (o no tiene mediaId) → no hay contenido
                return NotFound(new
                {
                    mensaje = "Adjunto sin contenido (no se guardó el binario en BD y no es un adjunto WhatsApp descargable)."
                });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id });
                return StatusCode(500, ex.Message);
            }
        }

        // =========================================================
        // UPSERT (JSON con Data_Base64 opcional)
        // =========================================================
        [HttpPost("upsert")]
        [Consumes("application/json")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(DescriptiveBoolean), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DescriptiveBoolean), StatusCodes.Status201Created)]
        public ActionResult Upsert([FromBody] AttachmentUpsertRequest req)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                byte[]? data = null;
                if (!string.IsNullOrEmpty(req.Data_Base64))
                {
                    try
                    {
                        data = Convert.FromBase64String(req.Data_Base64);
                    }
                    catch (Exception convEx)
                    {
                        return BadRequest(new
                        {
                            mensaje = "Data_Base64 inválido",
                            detalle = convEx.Message
                        });
                    }
                }

                if (req.Id == 0)
                {
                    var a = new Attachment
                    {
                        MessageId = req.Message_Id,
                        FileName = req.File_Name,
                        MimeType = req.Mime_Type,
                        Data = data,
                        UploadedAt = req.Uploaded_At ?? DateTime.UtcNow
                    };

                    var r = _bus.Create(a);
                    return r.StatusCodeDescriptivo();
                }
                else
                {
                    var encontrado = _bus.Find(req.Id);
                    if (!encontrado.Exitoso || encontrado.Data == null)
                        return NotFound(new { mensaje = "Adjunto no encontrado" });

                    var a = encontrado.Data;
                    a.MessageId = req.Message_Id != 0 ? req.Message_Id : a.MessageId;
                    a.FileName = req.File_Name ?? a.FileName;
                    a.MimeType = req.Mime_Type ?? a.MimeType;
                    a.Data = data ?? a.Data;
                    a.UploadedAt = req.Uploaded_At ?? a.UploadedAt;

                    var r = _bus.Update(a);
                    return r.StatusCodeDescriptivo();
                }
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return new DescriptiveBoolean
                {
                    Exitoso = false,
                    Mensaje = "Error al crear/actualizar",
                    StatusCode = 500
                }.StatusCodeDescriptivo();
            }
        }

        // =========================================================
        // UPLOAD MULTIPART (desde formulario / archivo)
        // =========================================================
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public ActionResult Upload([FromForm] AttachmentUploadForm form)
        {
            try
            {
                if (form.File == null || form.File.Length == 0)
                    return BadRequest(new { mensaje = "Archivo vacío" });

                using var ms = new System.IO.MemoryStream();
                form.File.CopyTo(ms);
                var bytes = ms.ToArray();

                var a = new Attachment
                {
                    MessageId = form.MessageId,
                    FileName = form.File.FileName,
                    MimeType = form.File.ContentType,
                    Data = bytes,
                    UploadedAt = DateTime.UtcNow
                };

                var r = _bus.Create(a);
                return r.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { form?.MessageId, file = form?.File?.FileName });
                return new DescriptiveBoolean
                {
                    Exitoso = false,
                    Mensaje = "Error en upload",
                    StatusCode = 500
                }.StatusCodeDescriptivo();
            }
        }

        // =========================================================
        // ELIMINAR
        // =========================================================
        [HttpGet("Delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                return _bus.Delete(id).StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id });
                return new DescriptiveBoolean
                {
                    Exitoso = false,
                    Mensaje = "Error al eliminar",
                    StatusCode = 500
                }.StatusCodeDescriptivo();
            }
        }
    }
}
