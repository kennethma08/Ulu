using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Xml;
using Whatsapp_API.Models.Entities.Security;
using JsonFormatting = Newtonsoft.Json.Formatting;


namespace Whatsapp_API.Helpers
{
    public class EmailHelper
    {
        private readonly IConfiguration _configuration;

        public EmailHelper(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Maneja una excepción enviando un correo de error y lanzando una nueva excepción con un mensaje personalizado.
        /// </summary>
        public T ManejarExcepcion<T>(Exception ex, object data, string message)
        {
            EnviarCorreoError(ex, data);
            throw new Exception(message, ex);
        }

        /// <summary>
        /// Envía un correo para recuperar la contraseña de un usuario.
        /// </summary>
        public void RecuperarContrasenia(User user, string password)
        {
            if (user == null || string.IsNullOrEmpty(user.Email))
                throw new ArgumentException("El usuario o su correo no pueden ser nulos.");

            var message = new MailMessage
            {
                To = { user.Email },
                Subject = "Recuperación de Contraseña - Sistema de administración de animales ",
                Body = $@"
            <html>
                <head>
                    <style>
                        body {{
                            font-family: Arial, sans-serif;
                            background-color: #f1f1f1;
                            margin: 0;
                            padding: 0;
                        }}
                        .email-container {{
                            width: 100%;
                            background-color: #f1f1f1;
                            padding: 20px;
                            text-align: center;
                        }}
                        .email-content {{
                            width: 600px;
                            background-color: #fff;
                            padding: 30px;
                            border-radius: 8px;
                            box-shadow: 0 4px 8px rgba(0, 0, 0, 0.1);
                            margin: 0 auto;
                        }}
                        h1 {{
                            font-size: 24px;
                            color: #6F4F1E;
                            text-align: center;
                        }}
                        p {{
                            font-size: 16px;
                            color: #4E4E4E;
                            line-height: 1.5;
                        }}
                        .strong {{
                            font-weight: bold;
                            color: #6F4F1E;
                        }}
                        .footer {{
                            font-size: 14px;
                            text-align: center;
                            color: #8C8C8C;
                            margin-top: 20px;
                        }}
                        .btn {{
                            display: inline-block;
                            background-color: #6F4F1E;
                            color: white;
                            padding: 10px 20px;
                            text-decoration: none;
                            font-size: 16px;
                            border-radius: 4px;
                            margin-top: 20px;
                            text-align: center;
                        }}
                        .btn:hover {{
                            background-color: #5c3e1f;
                        }}
                    </style>
                </head>
                <body>
                    <div class='email-container'>
                        <table class='email-content' cellpadding='0' cellspacing='0' align='center'>
                            <tr>
                                <td>
                                    <h1>Recuperación de Contraseña</h1>
                                    <p>Estimado/a <span class='strong'>{user.Name}</span>,</p>
                                    <p>Hemos recibido una solicitud para recuperar su contraseña en nuestro sistema.</p>
                                    <p>Su nueva contraseña temporal es: <strong class='strong'>{password}</strong></p>
                                    <p>Por favor, utilícela para acceder a su cuenta. Recuerde cambiarla por una de su preferencia una vez ingrese al sistema.</p>
                                    <p>Si no ha realizado esta solicitud, por favor ignore este mensaje.</p>
                                    <div class='footer'>
                                        <p>Atentamente,<br/>El equipo de soporte de <strong>CNET</strong></p>
                                    </div>
                                </td>
                            </tr>
                        </table>
                    </div>
                </body>
            </html>",
                IsBodyHtml = true,
                From = new MailAddress("kennethmartinezvargas@gmail.com")
            };

            EnviarCorreo(message);
        }

        /// <summary>
        /// Envía un correo con los detalles de un error ocurrido en la aplicación.
        /// </summary>
        public void EnviarCorreoError(Exception ex, object infoAdicional = null)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));

            string MessageBody = ConstruirCuerpoCorreoError(ex, infoAdicional);

            var message = new MailMessage
            {
                To = { ObtenerConfiguracion("CorreoErrores:correoRecibeError") },
                Subject = $"Reporte de Error - {_configuration["CorreoErrores:Aplicacion"]} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                Body = MessageBody,
                IsBodyHtml = true,
                From = new MailAddress("kennethmartinezvargas@gmail.com")
            };

            //EnviarCorreo(mensaje);
        }

        /// <summary>
        /// Configura y envía un correo utilizando SMTP.
        /// </summary>
        private void EnviarCorreo(MailMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            using (var smtpClient = ConfigurarSmtpClient())
            {
                try
                {
                    smtpClient.Send(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al enviar correo: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Configura el cliente SMTP utilizando las configuraciones de la aplicación.
        /// </summary>
        private SmtpClient ConfigurarSmtpClient()
        {
            return new SmtpClient(ObtenerConfiguracion("CorreoErrores:servidorError"), int.Parse(ObtenerConfiguracion("CorreoErrores:puertoError")))
            {
                Credentials = new NetworkCredential(
                    ObtenerConfiguracion("CorreoErrores:mailError"),
                    ObtenerConfiguracion("CorreoErrores:contrasenaError")
                ),
                EnableSsl = bool.Parse(ObtenerConfiguracion("CorreoErrores:SSLError")),
                Timeout = 30000
            };
        }

        /// <summary>
        /// Construye el cuerpo del correo de error en formato HTML.
        /// </summary>
        private string ConstruirCuerpoCorreoError(Exception ex, object infoAdicional)
        {
            string infoExtra = string.Empty;

            if (infoAdicional != null)
            {
                try
                {
                    infoExtra = JsonConvert.SerializeObject(infoAdicional, JsonFormatting.Indented);
                }
                catch (Exception serEx)
                {
                    infoExtra = $"Error al serializar objeto adicional: {serEx.Message}";
                }
            }

            return $@"
                <html>
                  <head>
                    <style>
                      body {{ font-family: Arial, sans-serif; font-size: 14px; color: #333; }}
                      .container {{ padding: 20px; }}
                      h2 {{ color: #ff8c00; }}
                      .error-details, .info-adicional {{ padding: 10px; margin: 15px 0; border-radius: 5px; }}
                      .error-details {{ background: #f8f8f8; border: 1px solid #ddd; }}
                      .info-adicional {{ background: #e8f4fd; border: 1px solid #b6e0fe; }}
                      pre {{ white-space: pre-wrap; word-wrap: break-word; }}
                    </style>
                  </head>
                  <body>
                    <div class='container'>
                      <h2>Reporte de Error en la Aplicación</h2>
                      <p><strong>Fecha:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>
                      <p><strong>Mensaje del Error:</strong> {ex.Message}</p>
                      <p><strong>Tipo de Excepción:</strong> {ex.GetType().FullName}</p>
                      <div class='error-details'>
                        <pre>{ex.StackTrace}</pre>
                      </div>
                      {(string.IsNullOrEmpty(infoExtra) ? "" : $@"
                      <h3>Información adicional:</h3>
                      <div class='info-adicional'>
                        <pre>{infoExtra}</pre>
                      </div>")}
                    </div>
                  </body>
                </html>";
        }

        /// <summary>
        /// Obtiene un valor de configuración y lanza una excepción si es nulo o vacío.
        /// </summary>
        private string ObtenerConfiguracion(string clave)
        {
            string valor = _configuration[clave];
            if (string.IsNullOrEmpty(valor))
                throw new ArgumentNullException(clave, $"La configuración '{clave}' no puede ser nula o vacía.");
            return valor;
        }

        /// <summary>
        /// Envía un correo con los detalles de un error ocurrido en la aplicación (errores JavaScript de la app).
        /// </summary>
        public void EnviarCorreoErrorApp(string mensajeError, string stackTrace, object infoAdicional = null)
        {
            if (string.IsNullOrEmpty(mensajeError)) throw new ArgumentNullException(nameof(mensajeError));

            string MessageBody = ConstruirCuerpoCorreoErrorApp(mensajeError, stackTrace, infoAdicional);

            var message = new MailMessage
            {
                To = { ObtenerConfiguracion("CorreoErrores:correoRecibeErrorApp") },
                Subject = $"Reporte de Error desde la App - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                Body = MessageBody,
                IsBodyHtml = true,
                From = new MailAddress("kennethmartinezvargas@gmail.com")
            };

            EnviarCorreo(message);
        }

        /// <summary>
        /// Construye el cuerpo del correo de error específico para errores de la app en formato HTML.
        /// </summary>
        private string ConstruirCuerpoCorreoErrorApp(string mensajeError, string stackTrace, object infoAdicional)
        {
            string infoExtra = string.Empty;

            if (infoAdicional != null)
            {
                try
                {
                    switch (infoAdicional)
                    {
                        case string s:
                            infoExtra = s;
                            break;
                        case JsonElement je:
                            infoExtra = je.GetRawText();
                            break;
                        case JToken jt:
                            infoExtra = jt.ToString(JsonFormatting.Indented);
                            break;
                        default:
                            infoExtra = JsonConvert.SerializeObject(infoAdicional, JsonFormatting.Indented);
                            break;
                    }

                    if (!string.IsNullOrEmpty(infoExtra) && infoExtra.Length > 1 &&
                        infoExtra[0] == '"' && infoExtra[^1] == '"')
                    {
                        infoExtra = JsonConvert.DeserializeObject<string>(infoExtra);
                    }
                }
                catch (Exception serEx)
                {
                    infoExtra = $"Error al serializar objeto adicional: {serEx.Message}";
                }
            }

            return $@"
                    <html>
                    <head>
                        <style>
                            body {{ font-family: Arial, sans-serif; font-size: 14px; color: #333; }}
                            .container {{ padding: 20px; }}
                            h2 {{ color: #ff8c00; }}
                            .error-details, .info-adicional {{ padding: 10px; margin: 15px 0; border-radius: 5px; }}
                            .error-details {{ background: #f8f8f8; border: 1px solid #ddd; }}
                            .info-adicional {{ background: #e8f4fd; border: 1px solid #b6e0fe; }}
                            pre {{ white-space: pre-wrap; word-wrap: break-word; }}
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <h2>Reporte de Error en la Aplicación - Asociación Jersey</h2>
                            <p><strong>Fecha:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>
                            <p><strong>Mensaje del Error:</strong> {mensajeError}</p>
                            <p><strong>Tipo de Error:</strong> Error JavaScript en la app</p>
                            <div class='error-details'>
                                <pre>{stackTrace}</pre>
                            </div>
                            {(string.IsNullOrEmpty(infoExtra) ? "" : $@"
                            <h3>Información adicional:</h3>
                            <div class='info-adicional'>
                                <pre>{infoExtra}</pre>
                            </div>")}
                        </div>
                    </body>
                    </html>";
        }
    }
}
