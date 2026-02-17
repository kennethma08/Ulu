using Microsoft.AspNetCore.Mvc;

namespace Whatsapp_API.Models.Helpers
{

    // atajos para devolver la misma respuesta con su status
    public static class DescriptiveBooleanExtensions
    {
        public static ActionResult StatusCodeDescriptivo(this DescriptiveBoolean r)
            => new ObjectResult(r) { StatusCode = r.StatusCode };

        public static ActionResult StatusCodeDescriptivo<T>(this DescriptiveBoolean<T> r)
            => new ObjectResult(r) { StatusCode = r.StatusCode };
    }
}
