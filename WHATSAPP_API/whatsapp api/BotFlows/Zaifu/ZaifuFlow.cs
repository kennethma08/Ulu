using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Whatsapp_API.BotFlows.Core;
using Whatsapp_API.Business.General; // ConversacionBus
using Whatsapp_API.Business.Integrations; // <-- Para TemplateHeaderLocation

namespace Whatsapp_API.BotFlows.Zaifu
{
    public class ZaifuFlow : IChatFlow
    {
        // IMPORTANTE: ESTA KEY ES EL ATRIBUTO FlowKey DE LA EMPRESA EN LA BASE DE DATOS
        public string Key => "zaifu";

        private readonly WhatsappSender _sender;
        private readonly WhatsappTemplateBus _tplBus;
        private readonly ConversationBus _convBus;

        public ZaifuFlow(WhatsappSender sender, WhatsappTemplateBus tplBus, ConversationBus convBus)
        {
            _sender = sender;
            _tplBus = tplBus;
            _convBus = convBus;
        }

        private enum Stage { Menu, ServicesMenu, AwaitYesNo }
        private class ConvState
        {
            public Stage Stage { get; set; } = Stage.Menu;
            public string? CurrentService { get; set; }
        }
        private static readonly ConcurrentDictionary<int, ConvState> _state = new();

        public async Task HandleAsync(FlowInput input)
        {
            var raw = (input.MessageText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return;

            var up = Normalize(raw);
            var st = _state.GetOrAdd(input.ConversationId, _ => new ConvState());

            // === SILENCIO DURANTE HANDOFF HUMANO ===
            var handoffActive = IsAgentHandoffActive(input.ConversationId);
            var isExplicit = IsExplicitCommand(up, st.Stage);

            if (handoffActive && !isExplicit)
            {
                // Mantenerse en silencio para evitar "no entendí" cuando contesta un agente.
                return;
            }

            if (input.JustCreated)
            {
                st.Stage = Stage.Menu;
                st.CurrentService = null;
                await SendTpl(input, "bienvenido");
                return;
            }

            if (up == "MENU" || up == "MENU ")
            {
                st.Stage = Stage.Menu;
                st.CurrentService = null;
                await SendTpl(input, "bienvenido");
                return;
            }

            switch (st.Stage)
            {
                case Stage.Menu:
                    {
                        if (up.Contains("HORARIO") || up.Contains("UBICACION"))
                        {
                            // Enviar plantilla de ubicación con header location
                            await SendUbicacionTpl(input);
                            return;
                        }
                        if (up.Contains("SERVICIO"))
                        {
                            st.Stage = Stage.ServicesMenu;
                            await SendTpl(input, "servicios");
                            return;
                        }
                        if (up.Contains("AGENTE") || up.Contains("ASESOR"))
                        {
                            _convBus.MarkAgentRequested(input.ConversationId);

                            await _sender.SendTextAsync(input.PhoneE164, "Perfecto. Un momento, un agente se comunicará con usted en breve.");
                            st.Stage = Stage.Menu;
                            st.CurrentService = null;
                            return;
                        }

                        var opt = MapToOption(up);
                        if (opt == "1")
                        {
                            await SendUbicacionTpl(input);
                            return;
                        }
                        if (opt == "2")
                        {
                            st.Stage = Stage.ServicesMenu;
                            await SendTpl(input, "servicios");
                            return;
                        }
                        if (opt == "3")
                        {
                            _convBus.MarkAgentRequested(input.ConversationId);

                            await _sender.SendTextAsync(input.PhoneE164, "Perfecto. Un momento, un agente se comunicará con usted en breve.");
                            st.Stage = Stage.Menu;
                            st.CurrentService = null;
                            return;
                        }

                        if (!handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, "Disculpe, no logré entender su respuesta. Por favor elija una opción del menú.");
                            await SendTpl(input, "bienvenido");
                        }
                        return;
                    }

                case Stage.ServicesMenu:
                    {
                        var service = MapToService(up);
                        if (service != null)
                        {
                            st.Stage = Stage.AwaitYesNo;
                            st.CurrentService = service;

                            await SendServicePitch(input.PhoneE164, service);
                            await _sender.SendTextAsync(input.PhoneE164, "¿Desea que le contactemos? Responda *sí* o *no*.");
                            return;
                        }

                        if (!handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, "Por favor seleccione una de las opciones de servicios o escriba *MENÚ* para volver.");
                            await SendTpl(input, "servicios");
                        }
                        return;
                    }

                case Stage.AwaitYesNo:
                    {
                        if (IsYes(up))
                        {
                            _convBus.MarkAgentRequested(input.ConversationId);

                            await _sender.SendTextAsync(input.PhoneE164, "Perfecto. Un momento, un agente se comunicará con usted en breve.");
                            st.Stage = Stage.Menu;
                            st.CurrentService = null;
                            await SendTpl(input, "bienvenido");
                            return;
                        }
                        if (IsNo(up))
                        {
                            await _sender.SendTextAsync(input.PhoneE164, "De acuerdo. Si desea, puede elegir otro servicio o escribir *MENÚ* para volver al inicio.");
                            st.Stage = Stage.ServicesMenu;
                            await SendTpl(input, "servicios");
                            return;
                        }

                        if (!handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, "No le he entendido. Por favor responda *sí* o *no*.");
                        }
                        return;
                    }
            }
        }

        // ===== Helpers =====

        private bool IsAgentHandoffActive(int conversationId)
        {
            try
            {
                var r = _convBus.Find(conversationId);
                if (r?.Data == null) return false;

                var status = (r.Data.Status ?? "open").ToLowerInvariant();
                var hasFlag = r.Data.GetType().GetProperty("AgentRequestedAt") != null
                              && (r.Data.GetType().GetProperty("AgentRequestedAt")!.GetValue(r.Data) as System.DateTime?) != null;

                return hasFlag && status == "open";
            }
            catch
            {
                return false;
            }
        }

        private static bool IsExplicitCommand(string up, Stage stage)
        {
            if (up == "MENU" || up == "MENU ") return true;
            if (up.Contains("SERVICIO") || up.Contains("HORARIO") || up.Contains("UBICACION")) return true;
            if (up.Contains("AGENTE") || up.Contains("ASESOR")) return true;
            if (MapToOption(up) != null) return true;

            if (stage == Stage.AwaitYesNo && (IsYes(up) || IsNo(up))) return true;

            if (MapToService(up) != null) return true;

            return false;
        }

        private async Task SendTpl(FlowInput input, string templateName)
        {
            var tpl = _tplBus.FindActiveByEmpresaAndName(input.CompanyId, templateName);
            if (tpl != null)
            {
                var r = await _sender.SendTemplateAsync(input.PhoneE164, tpl.Name, tpl.Language, null);
                if (!r.Exitoso)
                    await _sender.SendTextAsync(input.PhoneE164, $"Error enviando plantilla '{tpl.Name}/{tpl.Language}': {r.Mensaje}");
            }
            else
            {
                await _sender.SendTextAsync(input.PhoneE164, $"No hay plantilla activa '{templateName}' para su empresa.");
            }
        }

        // NUEVO: envía la plantilla "ubicacion" con header de ubicación
        private async Task SendUbicacionTpl(FlowInput input)
        {
            var tpl = _tplBus.FindActiveByEmpresaAndName(input.CompanyId, "ubicacion");
            if (tpl == null)
            {
                await _sender.SendTextAsync(input.PhoneE164, "No hay plantilla activa 'ubicacion' para su empresa.");
                return;
            }

            // Usa los datos que mencionaste en Swagger. Cámbialos si los tomas de config/BD.
            var headerLoc = new TemplateHeaderLocation(
                9.90992805427277,               // lat
                -83.99568586699415,             // lng
                "CNET Technology Systems",      // name
                "Tres Rios, Provincia de Cartago Carretera vieja a 3 Ríos Del Saint Gregory School 200 metros Este y 100 al Norte bordeando la Subestación del ICE, Provincia de Cartago, Tres Rios, 30303"
            );

            var r = await _sender.SendTemplateAsync(
                input.PhoneE164,
                tpl.Name,
                tpl.Language,
                null,
                headerLoc
            );

            if (!r.Exitoso)
            {
                await _sender.SendTextAsync(input.PhoneE164, $"Error enviando plantilla '{tpl.Name}/{tpl.Language}': {r.Mensaje}");
            }
        }

        private async Task SendServicePitch(string phone, string serviceKey)
        {
            string titulo, texto;

            switch (serviceKey)
            {
                case "legal":
                    titulo = "Acompañamiento legal";
                    texto = "Apoyo en cumplimiento normativo, contratos y asesoría personalizada para su negocio.";
                    break;
                case "financiero":
                    titulo = "Acompañamiento financiero";
                    texto = "Planificación y organización de sus finanzas para alcanzar metas claras y sostenibles.";
                    break;
                case "contable":
                    titulo = "Acompañamiento contable";
                    texto = "Soporte en manejo de libros, declaraciones y reportes para mantener su contabilidad en orden.";
                    break;
                case "marketing":
                    titulo = "Marketing digital";
                    texto = "Estrategias de redes sociales, anuncios y campañas para atraer más clientes.";
                    break;
                case "web":
                    titulo = "Diseño web";
                    texto = "Sitios modernos, optimizados y adaptados a su negocio para destacar en internet.";
                    break;
                case "grafico":
                    titulo = "Diseño gráfico";
                    texto = "Logos, identidad visual y material gráfico que refuerzan la imagen de su marca.";
                    break;
                default:
                    titulo = "Servicio";
                    texto = "Servicio personalizado según sus necesidades.";
                    break;
            }

            var body = $"*{titulo}*\n{textosafe(texto)}";
            await _sender.SendTextAsync(phone, body);
        }

        private static string textosafe(string s) => (s ?? "").Trim();

        // Normaliza mayúsculas, quita tildes y otras cosas
        private static string Normalize(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            var formD = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in formD)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            }
            var stripped = sb.ToString().Normalize(NormalizationForm.FormC);
            stripped = stripped.Replace('Ñ', 'N');
            stripped = Regex.Replace(stripped, @"\s+", " ").Trim();
            return stripped;
        }

        private static bool IsYes(string up)
            => up == "SI" || up == "SI." || up == "AFIRMATIVO" || up == "OK" || up == "OK.";

        private static bool IsNo(string up)
            => up == "NO" || up == "NO." || up == "NEGATIVO";

        private static string? MapToOption(string up)
        {
            if (up.Contains("1️⃣")) return "1";
            if (up.Contains("2️⃣")) return "2";
            if (up.Contains("3️⃣")) return "3";

            var clean = Regex.Replace(up, @"[^\w\s]", " ").Trim();

            if (Regex.IsMatch(clean, @"\b(OPCION|OPCION|ELEGIR|ELIJO)\s*1\b")) return "1";
            if (Regex.IsMatch(clean, @"\b(OPCION|OPCION|ELEGIR|ELIJO)\s*2\b")) return "2";
            if (Regex.IsMatch(clean, @"\b(OPCION|OPCION|ELEGIR|ELIJO)\s*3\b")) return "3";

            if (Regex.IsMatch(clean, @"\b(UNO|PRIMERO|1)\b")) return "1";
            if (Regex.IsMatch(clean, @"\b(DOS|SEGUNDO|2)\b")) return "2";
            if (Regex.IsMatch(clean, @"\b(TRES|TERCERO|3)\b")) return "3";

            if (clean == "1") return "1";
            if (clean == "2") return "2";
            if (clean == "3") return "3";

            return null;
        }

        // Mapea texto de botones/servicios a una clave
        private static string? MapToService(string up)
        {
            if (up.Contains("ACOMPANAMIENTO LEGAL")) return "legal";
            if (up.Contains("ACOMPANAMIENTO FINANCIERO")) return "financiero";
            if (up.Contains("ACOMPANAMIENTO CONTABLE")) return "contable";
            if (up.Contains("MARKETING DIGITAL")) return "marketing";
            if (up.Contains("DISENO WEB")) return "web";
            if (up.Contains("DISENO GRAFICO")) return "grafico";
            return null;
        }
    }
}
