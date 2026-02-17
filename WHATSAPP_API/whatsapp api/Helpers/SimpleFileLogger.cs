using System.Text;

namespace Whatsapp_API.Helpers
{

    // logger sencillito a archivos .log por día *REVISAR PARA ELIMINAR, SOLO PARA PRUEBAS POR EL MOMENTO
    public static class SimpleFileLogger
    {
        private static readonly object _lock = new();
        // Raíz por defecto: carpeta del ejecutable. Puedes sobreescribirla con ConfigureRoot.
        private static string _root = AppContext.BaseDirectory;
        private const string _folderName = "logs";

        /// Opcional: llama a esto en Program.cs para fijar la raíz (p.ej. ContentRootPath).

        public static void ConfigureRoot(string absolutePath)
        {
            if (!string.IsNullOrWhiteSpace(absolutePath))
                _root = absolutePath;
        }

        public static string LogDirectory => Path.Combine(_root, _folderName);

        private static string PrepareFile(string category)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var file = Path.Combine(LogDirectory, $"{category}-{DateTime.UtcNow:yyyyMMdd}.log");
                if (!File.Exists(file))
                {
                    // crear archivo vacío (no es obligatorio, pero útil para permisos)
                    using var _ = File.Create(file);
                }
                return file;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimpleFileLogger] Error preparando carpeta/archivo: {ex}");
                return string.Empty;
            }
        }

        public static void Log(string category, string title, string text)
        {
            var file = PrepareFile(category);
            if (string.IsNullOrEmpty(file)) return;

            var line = $"[{DateTime.UtcNow:O}] {title} {text}{Environment.NewLine}";
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(file, line, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimpleFileLogger] Error escribiendo log: {ex}");
            }
        }

        // por si ya traes json y no querés formatear aparte
        public static void LogJson(string category, string title, string json)
            => Log(category, title, json);
    }
}
