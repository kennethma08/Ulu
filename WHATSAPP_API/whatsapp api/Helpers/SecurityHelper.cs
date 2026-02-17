using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Whatsapp_API.Helpers
{

    public class SecurityHelper
    {

        private readonly IConfiguration _configuration;
        private string _clave;
        public SecurityHelper(IConfiguration configuration)
        {
            _configuration = configuration;
            _clave = _configuration["Seguridad:Clave"].ToString(); // Obtener la clave de configuración

        }
        /// <summary>
        /// Metodo que se encarga de generar una contraseña
        /// </summary>
        /// <param name="longitud"></param>
        /// <returns></returns>
        public string GenerarContraseña(int longitud = 12)
        {
            var random = new Random();
            const string caracteresPermitidos = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_-+=<>?";

            char[] contrasenia = new char[longitud];
            for (int i = 0; i < longitud; i++)
            {
                contrasenia[i] = caracteresPermitidos[random.Next(caracteresPermitidos.Length)];
            }

            return new string(contrasenia);
        }

        /// <summary>
        /// Encripta un texto plano utilizando AES.
        /// </summary>
        /// <param name="textoPlano">Texto a encriptar.</param>
        /// <returns>Texto encriptado en base64.</returns>
        public string Encriptar(string textoPlano)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(_clave);
                aes.IV = new byte[16]; // Vector de inicialización (16 bytes de ceros).

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs))
                    {
                        sw.Write(textoPlano);
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        /// <summary>
        /// Desencripta un texto encriptado en base64 utilizando AES.
        /// </summary>
        /// <param name="textoEncriptado">Texto encriptado en base64.</param>
        /// <returns>Texto desencriptado.</returns>
        public string Desencriptar(string textoEncriptado)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(_clave);
                aes.IV = new byte[16]; // Vector de inicialización (16 bytes de ceros).

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream(Convert.FromBase64String(textoEncriptado)))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs))
                {
                    return sr.ReadToEnd();
                }
            }
        }
        /// <summary>
        /// Metodo que se encarga de validar si el correo es correcto
        /// </summary>
        /// <param name="correo">Indica el correo del usuario</param>
        /// <returns></returns>
        public bool ValidarCorreo(string correo)
        {
            // Expresión regular para validar correo
            string patron = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";

            // Crear la instancia de Regex con el patrón
            Regex regex = new Regex(patron);

            // Validar si el correo cumple con el patrón
            return regex.IsMatch(correo);
        }
    }
}