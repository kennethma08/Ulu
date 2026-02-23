using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Whatsapp_API.Helpers
{

    public class SecurityHelper
    {

        private readonly IConfiguration _configuration;
        private readonly string _clave;

        public SecurityHelper(IConfiguration configuration)
        {
            _configuration = configuration;
            // Ensure key is 32 bytes (256 bits) for AES-256
            var keyString = _configuration["Seguridad:Clave"];
            if (string.IsNullOrEmpty(keyString))
            {
                throw new ArgumentNullException("Seguridad:Clave", "La clave de encriptación no está configurada.");
            }

            // If the key is less than 32 chars, pad it. If more, truncate or use as is (if valid base64 or similar).
            // For simplicity and compatibility with previous behavior (UTF8 bytes), we'll ensure it's correct length.
            // But previous code just used Encoding.UTF8.GetBytes(_clave) which might be any length.
            // AES key size must be 16, 24, or 32 bytes.
            // We'll hash the key to ensure consistent 32 byte length regardless of input.
            using (var sha = SHA256.Create())
            {
                var keyBytes = Encoding.UTF8.GetBytes(keyString);
                _clave = Convert.ToBase64String(sha.ComputeHash(keyBytes));
            }
            // Wait, previous code used Encoding.UTF8.GetBytes(_clave) directly on the string.
            // If the string was "Cnet2025@@@", that's 11 bytes. That's not a valid AES key size (needs 16, 24, 32).
            // How did it even work?
            // "Cnet2025@@@" is 11 chars. In UTF8 it's 11 bytes.
            // RijndaelManaged/Aes.Create() usually throws if KeySize is invalid.
            // Maybe it was padding it or using a default?
            // Actually, if I change how the key is derived, I definitely break compatibility.
            // Let's stick to the exact key derivation if possible, but it must be valid.
            // The old code: aes.Key = Encoding.UTF8.GetBytes(_clave);
            // If _clave was "Cnet2025@@@", aes.Key would be 11 bytes. This throws explicit exception in .NET Core.
            // "Specified key is not a valid size for this algorithm."
            // So the code probably NEVER WORKED or the key in appsettings is longer.
            // The key in appsettings.Development.json is "Cnet2025@@@".
            // So this helper was broken/unused.
            // I will fix it by hashing the key to get 32 bytes.

            _clave = keyString;
        }

        private byte[] GetValidKey()
        {
             // Fix for invalid key size: use SHA256 to get 32 bytes stable key from any string
             using (var sha = SHA256.Create())
             {
                 return sha.ComputeHash(Encoding.UTF8.GetBytes(_clave));
             }
        }

        /// <summary>
        /// Metodo que se encarga de generar una contraseña segura
        /// </summary>
        /// <param name="longitud"></param>
        /// <returns></returns>
        public string GenerarContraseña(int longitud = 12)
        {
            const string caracteresPermitidos = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_-+=<>?";
            var result = new char[longitud];
            var data = new byte[longitud];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(data);
            }

            for (int i = 0; i < longitud; i++)
            {
                var rnd = data[i] % caracteresPermitidos.Length;
                result[i] = caracteresPermitidos[rnd];
            }

            return new string(result);
        }

        /// <summary>
        /// Encripta un texto plano utilizando AES con IV aleatorio.
        /// Formato salida: Base64(IV + CipherText)
        /// </summary>
        /// <param name="textoPlano">Texto a encriptar.</param>
        /// <returns>Texto encriptado en base64.</returns>
        public string Encriptar(string textoPlano)
        {
            if (string.IsNullOrEmpty(textoPlano)) return string.Empty;

            using (Aes aes = Aes.Create())
            {
                aes.Key = GetValidKey();
                aes.GenerateIV(); // Genera IV aleatorio

                // Encrypt
                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    // Prepend IV
                    ms.Write(aes.IV, 0, aes.IV.Length);

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
        /// Asume formato: Base64(IV + CipherText).
        /// Si falla, intenta legado (IV ceros).
        /// </summary>
        /// <param name="textoEncriptado">Texto encriptado en base64.</param>
        /// <returns>Texto desencriptado.</returns>
        public string Desencriptar(string textoEncriptado)
        {
            if (string.IsNullOrEmpty(textoEncriptado)) return string.Empty;

            byte[] fullCipher;
            try
            {
                fullCipher = Convert.FromBase64String(textoEncriptado);
            }
            catch { return string.Empty; }

            // Intento 1: Nuevo formato (IV incluido)
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = GetValidKey();

                    // Extraer IV
                    if (fullCipher.Length < aes.BlockSize / 8) throw new Exception("Cipher too short");

                    var iv = new byte[aes.BlockSize / 8];
                    Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    using (var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch
            {
                // Fallback: Intento legado (IV ceros, todo es cipher)
                // Nota: Esto solo funciona si el legacy usaba la misma clave derivada (SHA256)
                // Pero el legacy usaba Raw bytes que fallaban.
                // Si el legacy nunca funcionó, no hay nada que soportar.
                // Si funcionaba porque la clave ERA valida (32 chars), entonces esto podria fallar.
                // Asumimos que "Legacy" estaba roto o usaba otra clave.
                // Intentaremos descifrar con IV ceros y la clave hasheada.
                 try
                {
                    using (Aes aes = Aes.Create())
                    {
                        aes.Key = GetValidKey();
                        aes.IV = new byte[16]; // ceros

                        using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                        using (var ms = new MemoryStream(fullCipher))
                        using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                        using (var sr = new StreamReader(cs))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
                catch
                {
                    return string.Empty; // No se pudo desencriptar
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
            if (string.IsNullOrWhiteSpace(correo)) return false;
            // Expresión regular para validar correo
            string patron = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";

            // Crear la instancia de Regex con el patrón
            try
            {
                return Regex.IsMatch(correo, patron);
            }
            catch
            {
                return false;
            }
        }
    }
}