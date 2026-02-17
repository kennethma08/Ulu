using System;
using System.Security.Cryptography;
using System.Text;

namespace Whatsapp_API.Helpers
{
    public static class PasswordHelper
    {
        // Siempre que necesites guardar una contraseña nueva, usa ESTE método
        public static string HashPassword(string? password)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? string.Empty));
            // Formato estándar: Base64 del SHA256
            return Convert.ToBase64String(bytes);
        }

        // Verifica soportando:
        // 1) texto plano (legacy)
        // 2) SHA256 Base64 (C#)
        // 3) SHA256 HEX (SQL HASHBYTES)
        public static bool VerifyPassword(string? password, string? storedHash)
        {
            if (string.IsNullOrWhiteSpace(storedHash))
                return false;

            var plain = password ?? string.Empty;

            // 1) Contraseña en texto plano (BD aún sin encriptar)
            if (storedHash == plain)
                return true;

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(plain));

            // 2) SHA256 en Base64 (igual que HashPassword)
            var base64 = Convert.ToBase64String(bytes);
            if (storedHash == base64)
                return true;

            // 3) SHA256 en HEX (típico de SQL: 64 caracteres 0-9A-F)
            var hex = BitConverter.ToString(bytes).Replace("-", string.Empty); // "A9993E36..."
            if (storedHash.Equals(hex, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
