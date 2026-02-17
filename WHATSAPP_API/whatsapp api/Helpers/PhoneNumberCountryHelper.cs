using System;
using System.Linq;
using PhoneNumbers;

namespace Whatsapp_API.Helpers
{
    public static class PhoneNumberCountryHelper
    {
        private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

        // Devuelve el ISO-2 (CR, US, MX, etc.) o null si no se puede
        public static string? ResolveIso2OrNull(string? rawPhone, string? defaultRegionIso2 = null)
        {
            if (string.IsNullOrWhiteSpace(rawPhone)) return null; // Si no hay número, da null

            // 1) Intenta "parsear" con región por defecto (para números locales sin +)
            if (TryParse(rawPhone, defaultRegionIso2, out var num))
            {
                var region = Util.GetRegionCodeForNumber(num!);
                if (!string.IsNullOrWhiteSpace(region) && !string.Equals(region, "ZZ", StringComparison.OrdinalIgnoreCase))
                    return region.ToUpperInvariant();
            }

            // 2) Intenta "parsear" en formato E.164 forzado (+XXXXXXXX)
            var forced = ForcePlusDigits(rawPhone);
            if (TryParse(forced, null, out num))
            {
                var region = Util.GetRegionCodeForNumber(num!);
                if (!string.IsNullOrWhiteSpace(region) && !string.Equals(region, "ZZ", StringComparison.OrdinalIgnoreCase))
                    return region.ToUpperInvariant();
            }

            return null;
        }

        public static string? NormalizeToE164OrNull(string? rawPhone, string? defaultRegionIso2 = null)
        {
            if (string.IsNullOrWhiteSpace(rawPhone)) return null;
            try
            {
                var number = Util.Parse(rawPhone, string.IsNullOrWhiteSpace(defaultRegionIso2) ? null : defaultRegionIso2.ToUpperInvariant()); // Si el número no tiene +, usa la región por defecto
                if (!Util.IsValidNumber(number)) return null;
                return Util.Format(number, PhoneNumberFormat.E164); // Normaliza al formato +# indicado
            }
            catch
            {
                var cleaned = ForcePlusDigits(rawPhone);
                try
                {
                    var number = Util.Parse(cleaned, null);
                    if (!Util.IsValidNumber(number)) return null;
                    return Util.Format(number, PhoneNumberFormat.E164);
                }
                catch { return null; }
            }
        }

        private static bool TryParse(string raw, string? region, out PhoneNumber? number)
        {
            try
            {
                number = Util.Parse(raw, string.IsNullOrWhiteSpace(region) ? null : region.ToUpperInvariant());
                if (!Util.IsValidNumber(number)) { number = null; return false; }
                return true;
            }
            catch { number = null; return false; } // Si no tiene número válido, da error
        }

        private static string ForcePlusDigits(string s) // Deja solo dígitos y el + inicial si lo tiene
        {
            s = s.Trim();
            if (s.StartsWith("+"))
                return "+" + new string(s.Skip(1).Where(char.IsDigit).ToArray());
            return "+" + new string(s.Where(char.IsDigit).ToArray());
        }
    }
}