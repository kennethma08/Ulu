using System;
using System.Security.Cryptography;
using System.Text;

namespace Whatsapp_API.Helpers
{
    public static class PasswordHelper
    {
        private const int SaltSize = 16; // 128 bit
        private const int KeySize = 32; // 256 bit
        private const int Iterations = 10000;
        private static readonly HashAlgorithmName _hashAlgorithmName = HashAlgorithmName.SHA256;
        private const byte Version = 0x01;

        // Siempre que necesites guardar una contraseña nueva, usa ESTE método
        // AHORA USA PBKDF2 (HMACSHA256) con Salt
        public static string HashPassword(string? password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;

            using (var algorithm = new Rfc2898DeriveBytes(
                password,
                SaltSize,
                Iterations,
                _hashAlgorithmName))
            {
                var salt = algorithm.Salt;
                var key = algorithm.GetBytes(KeySize);

                var result = new byte[1 + SaltSize + KeySize];
                result[0] = Version;
                Array.Copy(salt, 0, result, 1, SaltSize);
                Array.Copy(key, 0, result, 1 + SaltSize, KeySize);

                return Convert.ToBase64String(result);
            }
        }

        // Verifica soportando:
        // 1) texto plano (legacy - INSEGURO)
        // 2) SHA256 Base64 (legacy - SIN SALT)
        // 3) SHA256 HEX (legacy - SQL)
        // 4) PBKDF2 (Nuevo estándar seguro)
        public static bool VerifyPassword(string? password, string? storedHash)
        {
            if (string.IsNullOrWhiteSpace(storedHash))
                return false;

            var plain = password ?? string.Empty;

            // 1) Contraseña en texto plano (BD aún sin encriptar)
            // ESTO ES MUY INSEGURO, PERO SE MANTIENE POR COMPATIBILIDAD
            if (storedHash == plain)
                return true;

            // Generar hash legacy SHA256 para comparar
            using var sha = SHA256.Create();
            var shaBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(plain));

            // 2) SHA256 en Base64 (igual que HashPassword antiguo)
            var base64 = Convert.ToBase64String(shaBytes);
            if (storedHash == base64)
                return true;

            // 3) SHA256 en HEX (típico de SQL: 64 caracteres 0-9A-F)
            var hex = BitConverter.ToString(shaBytes).Replace("-", string.Empty); // "A9993E36..."
            if (storedHash.Equals(hex, StringComparison.OrdinalIgnoreCase))
                return true;

            // 4) Intento verificar formato nuevo (PBKDF2)
            try
            {
                var decoded = Convert.FromBase64String(storedHash);

                // Verificar longitud mínima (Version + Salt + Hash)
                if (decoded.Length != 1 + SaltSize + KeySize)
                    return false; // No es el formato nuevo

                // Verificar versión
                if (decoded[0] != Version)
                    return false;

                // Extraer Salt
                var salt = new byte[SaltSize];
                Array.Copy(decoded, 1, salt, 0, SaltSize);

                // Extraer Hash almacenado
                var storedKey = new byte[KeySize];
                Array.Copy(decoded, 1 + SaltSize, storedKey, 0, KeySize);

                // Computar Hash con el mismo Salt
                using (var algorithm = new Rfc2898DeriveBytes(
                    plain,
                    salt,
                    Iterations,
                    _hashAlgorithmName))
                {
                    var computedKey = algorithm.GetBytes(KeySize);
                    return CryptographicOperations.FixedTimeEquals(computedKey, storedKey);
                }
            }
            catch
            {
                // Si falla decodificar Base64 o cualquier otra cosa, asumimos que no es válido
                return false;
            }
        }
    }
}
