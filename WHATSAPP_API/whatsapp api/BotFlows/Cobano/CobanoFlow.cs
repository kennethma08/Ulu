using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Whatsapp_API.BotFlows.Core;
using Whatsapp_API.Business.General;
using System.IO;
using System.Linq;
using Whatsapp_API.Business.Integrations;

namespace Whatsapp_API.BotFlows.Cobano
{
    public class CobanoFlow : IChatFlow
    {
        public string Key => "cobano";

        private readonly WhatsappSender _sender;
        private readonly ConversationBus _convBus;

        // Espera entre envío de imagen y menú para asegurar el orden de llegada en WhatsApp
        private const int ImageToMenuDelayMs = 4000;

        public CobanoFlow(WhatsappSender sender, WhatsappTemplateBus tplBus, ConversationBus convBus)
        {
            _sender = sender;
            _convBus = convBus;
        }

        private enum Stage
        {
            Menu,
            MotosMenu,
            ScootersMenu,
            SportMenu,
            DualPurposeMenu,
            HighCcMenu,
            QuadsMenu,
            SpecialsMenu,
            SxsCategoriesMenu,
            SxsPioneerMenu,
            SxsTalon2Menu,
            SxsTalon4Menu,
            AfterVehicleView,
            AskAnother,
            WaitAgent,
            CreditAgent
        }

        private enum Lang { Unknown, Es, En }

        private class ConvState
        {
            public Stage Stage { get; set; } = Stage.Menu;
            public Lang Language { get; set; } = Lang.Unknown;
            public bool LanguagePrompted { get; set; } = false;

            public Stage LastVehicleMenuStage { get; set; } = Stage.Menu;
        }

        private static readonly ConcurrentDictionary<int, ConvState> _state = new();

        public async Task HandleAsync(FlowInput input)
        {
            var raw = (input.MessageText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return;

            var up = Normalize(raw);
            var st = _state.GetOrAdd(input.ConversationId, _ => new ConvState());

            var handoffActive = IsAgentHandoffActive(input.ConversationId);
            var isExplicit = IsExplicitCommand(up, st.Stage, st.Language);

            if (st.Stage == Stage.WaitAgent && handoffActive)
                return;

            if (st.Stage == Stage.WaitAgent && !handoffActive)
            {
                st.Stage = Stage.Menu;
                await SendMainMenu(st.Language, input.PhoneE164);
                return;
            }

            if (handoffActive && !isExplicit)
                return;

            if (input.JustCreated)
            {
                st.Stage = Stage.Menu;
                st.Language = Lang.Unknown;
                st.LanguagePrompted = true;
                await SendMainMenu(st.Language, input.PhoneE164);
                return;
            }

            if (up == "MENU" || up == "MENU ")
            {
                st.Stage = Stage.Menu;
                if (st.Language == Lang.Unknown) st.LanguagePrompted = true;
                await SendMainMenu(st.Language, input.PhoneE164);
                return;
            }

            switch (st.Stage)
            {
                case Stage.Menu:
                    {
                        var opt = MapToOption(up);

                        // Selección de idioma
                        if (st.Language == Lang.Unknown)
                        {
                            if (IsValidOption(Stage.Menu, st.Language, opt))
                            {
                                if (opt == "1")
                                {
                                    st.Language = Lang.Es;
                                    st.LanguagePrompted = false;
                                }
                                else if (opt == "2")
                                {
                                    st.Language = Lang.En;
                                    st.LanguagePrompted = false;
                                }
                                await SendMainMenu(st.Language, input.PhoneE164);
                                return;
                            }

                            if (!st.LanguagePrompted)
                            {
                                st.LanguagePrompted = true;
                                await SendMainMenu(st.Language, input.PhoneE164);
                                return;
                            }

                            if (!handoffActive)
                            {
                                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.Menu, st.Language));
                                await SendMainMenu(st.Language, input.PhoneE164);
                            }
                            return;
                        }

                        // Menú principal 
                        if (IsValidOption(Stage.Menu, st.Language, opt))
                        {
                            switch (opt)
                            {
                                case "1":
                                    st.Stage = Stage.MotosMenu;
                                    await SendMotosTypesMenu(st.Language, input.PhoneE164);
                                    return;
                                case "2":
                                    await SendRepuestosMensaje(st.Language, input.PhoneE164);
                                    st.Stage = Stage.AskAnother;
                                    await SendAskAnother(st.Language, input.PhoneE164);
                                    return;
                                case "3":
                                    await SendFinancingPdfAsync(input.PhoneE164, st.Language);
                                    await Task.Delay(1000);
                                    await SendCreditosMensaje(st.Language, input.PhoneE164);
                                    st.Stage = Stage.CreditAgent;
                                    return;
                                case "4":
                                    await SendHorarioUbicacionMensaje(st.Language, input.PhoneE164);
                                    st.Stage = Stage.AskAnother;
                                    await SendAskAnother(st.Language, input.PhoneE164);
                                    return;
                                case "5":
                                    // Hablar con asesor
                                    _convBus.MarkAgentRequested(input.ConversationId);
                                    var txt = st.Language == Lang.Es
                                        ? "Solicitaste hablar con un asesor. Un momento por favor. 😊"
                                        : "You requested to talk to an agent. One moment please. 😊";
                                    await _sender.SendTextAsync(input.PhoneE164, txt);
                                    st.Stage = Stage.WaitAgent;
                                    return;
                            }
                        }
                        else
                        {
                            if (!handoffActive)
                            {
                                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.Menu, st.Language));
                                await SendMainMenu(st.Language, input.PhoneE164);
                            }
                        }
                        return;
                    }

                case Stage.MotosMenu:
                    {
                        var opt = MapToOption(up);
                        if (IsValidOption(Stage.MotosMenu, st.Language, opt))
                        {
                            switch (opt)
                            {
                                case "1":
                                    await _sender.SendTextAsync(input.PhoneE164, st.Language == Lang.Es
                                        ? "🛵 ¡Tenemos una gran variedad de scooters! Elige el modelo:"
                                        : "🛵 We have a wide variety of scooters! Choose a model:");
                                    st.Stage = Stage.ScootersMenu;
                                    await SendScootersMenu(st.Language, input.PhoneE164);
                                    return;
                                case "2":
                                    await _sender.SendTextAsync(input.PhoneE164, st.Language == Lang.Es
                                        ? "🔥 ¡Pura adrenalina! Motos Sport. Selecciona tu favorita:"
                                        : "🔥 Pure adrenaline! Sport bikes. Pick your favorite:");
                                    st.Stage = Stage.SportMenu;
                                    await SendSportMenu(st.Language, input.PhoneE164);
                                    return;
                                case "3":
                                    await _sender.SendTextAsync(input.PhoneE164, st.Language == Lang.Es
                                        ? "🏔️ Ciudad hoy, aventura mañana. Elige tu compañera:"
                                        : "🏔️ City today, adventure tomorrow. Choose your companion:");
                                    st.Stage = Stage.DualPurposeMenu;
                                    await SendDualPurposeMenu(st.Language, input.PhoneE164);
                                    return;
                                case "4":
                                    await _sender.SendTextAsync(input.PhoneE164, st.Language == Lang.Es
                                        ? "💪 Potencia que impone. Alta Cilindrada:"
                                        : "💪 Power that commands. High Displacement:");
                                    st.Stage = Stage.HighCcMenu;
                                    await SendHighCcMenu(st.Language, input.PhoneE164);
                                    return;
                                case "5":
                                    await _sender.SendTextAsync(input.PhoneE164, st.Language == Lang.Es
                                        ? "🛞 Cuadriciclos (ATV): Elige un modelo:"
                                        : "🛞 Quad Bikes (ATV): Choose a model:");
                                    st.Stage = Stage.QuadsMenu;
                                    await SendQuadMenu(st.Language, input.PhoneE164);
                                    return;
                                case "6":
                                    await _sender.SendTextAsync(input.PhoneE164, st.Language == Lang.Es
                                        ? "🏍️✨ Especiales y off‑road puro. Elige un modelo:"
                                        : "🏍️✨ Special & pure off‑road. Choose a model:");
                                    st.Stage = Stage.SpecialsMenu;
                                    await SendSpecialsMenu(st.Language, input.PhoneE164);
                                    return;
                                case "7":
                                    await _sender.SendTextAsync(input.PhoneE164, st.Language == Lang.Es
                                        ? "🚙 Side by Side: Elige la categoría:"
                                        : "🚙 Side by Side: Choose a category:");
                                    st.Stage = Stage.SxsCategoriesMenu;
                                    await SendSxsCategoriesMenu(st.Language, input.PhoneE164);
                                    return;
                                case "8":
                                    st.Stage = Stage.Menu;
                                    await SendMainMenu(st.Language, input.PhoneE164);
                                    return;
                            }
                        }
                        else
                        {
                            if (!handoffActive)
                            {
                                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.MotosMenu, st.Language));
                                await SendMotosTypesMenu(st.Language, input.PhoneE164);
                            }
                        }
                        return;
                    }

                case Stage.ScootersMenu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleScooterSelection(opt, st, input, handoffActive)) return;
                        if (!IsValidOption(Stage.ScootersMenu, st.Language, opt) && !handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.ScootersMenu, st.Language));
                            await SendScootersMenu(st.Language, input.PhoneE164);
                        }
                        break;
                    }
                case Stage.SportMenu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleSportSelection(opt, st, input, handoffActive)) return;
                        if (!IsValidOption(Stage.SportMenu, st.Language, opt) && !handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SportMenu, st.Language));
                            await SendSportMenu(st.Language, input.PhoneE164);
                        }
                        break;
                    }
                case Stage.DualPurposeMenu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleDualPurposeSelection(opt, st, input, handoffActive)) return;
                        if (!IsValidOption(Stage.DualPurposeMenu, st.Language, opt) && !handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.DualPurposeMenu, st.Language));
                            await SendDualPurposeMenu(st.Language, input.PhoneE164);
                        }
                        break;
                    }
                case Stage.HighCcMenu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleHighCcSelection(opt, st, input, handoffActive)) return;
                        if (!IsValidOption(Stage.HighCcMenu, st.Language, opt) && !handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.HighCcMenu, st.Language));
                            await SendHighCcMenu(st.Language, input.PhoneE164);
                        }
                        break;
                    }
                case Stage.QuadsMenu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleQuadSelection(opt, st, input, handoffActive)) return;
                        if (!IsValidOption(Stage.QuadsMenu, st.Language, opt) && !handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.QuadsMenu, st.Language));
                            await SendQuadMenu(st.Language, input.PhoneE164);
                        }
                        break;
                    }
                case Stage.SpecialsMenu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleSpecialSelection(opt, st, input, handoffActive)) return;
                        if (!IsValidOption(Stage.SpecialsMenu, st.Language, opt) && !handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SpecialsMenu, st.Language));
                            await SendSpecialsMenu(st.Language, input.PhoneE164);
                        }
                        break;
                    }
                case Stage.SxsCategoriesMenu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleSxsCategoriesSelection(opt, st, input, handoffActive)) return;
                        if (!IsValidOption(Stage.SxsCategoriesMenu, st.Language, opt) && !handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SxsCategoriesMenu, st.Language));
                            await SendSxsCategoriesMenu(st.Language, input.PhoneE164);
                        }
                        break;
                    }
                case Stage.SxsPioneerMenu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleSxsPioneerSelection(opt, st, input, handoffActive)) return;
                        if (!IsValidOption(Stage.SxsPioneerMenu, st.Language, opt) && !handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SxsPioneerMenu, st.Language));
                            await SendSxsPioneerMenu(st.Language, input.PhoneE164);
                        }
                        break;
                    }
                case Stage.SxsTalon2Menu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleSxsTalon2Selection(opt, st, input, handoffActive)) return;
                        if (!IsValidOption(Stage.SxsTalon2Menu, st.Language, opt) && !handoffActive)
                        {
                            await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SxsTalon2Menu, st.Language));
                            await SendSxsTalon2Menu(st.Language, input.PhoneE164);
                        }
                        break;
                    }
                case Stage.SxsTalon4Menu:
                    {
                        var opt = MapToOption(up);
                        if (await HandleSxsTalon4Selection(opt, st, input, handoffActive)) return;
                        break;
                    }
                case Stage.AfterVehicleView:
                    {
                        var opt = MapToOption(up);

                        if (IsValidOption(Stage.AfterVehicleView, st.Language, opt))
                        {
                            if (opt == "1")
                            {
                                // Volver al submenú de la categoría desde la que se mostró la imagen
                                st.Stage = st.LastVehicleMenuStage;
                                await SendMenuForStage(st.LastVehicleMenuStage, st.Language, input.PhoneE164);
                                return;
                            }
                            if (opt == "2")
                            {
                                // Volver al menú principal
                                st.Stage = Stage.Menu;
                                await SendMainMenu(st.Language, input.PhoneE164);
                                return;
                            }
                        }

                        // Opción inválida: advierte y repite las opciones
                        await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.AfterVehicleView, st.Language));
                        await SendPostVehicleOptions(st.Language, input.PhoneE164);
                        return;
                    }

                case Stage.AskAnother:
                    {
                        var opt = MapToOption(up);
                        if (IsValidOption(Stage.AskAnother, st.Language, opt))
                        {
                            if (opt == "1")
                            {
                                // Volver al menú principal
                                st.Stage = Stage.Menu;
                                await SendMainMenu(st.Language, input.PhoneE164);
                                return;
                            }
                            if (opt == "2")
                            {
                                // Mensaje de cierre 
                                var bye = st.Language == Lang.Es
                                    ? "¡Gracias por contactarnos! Si necesitas algo más, escribe de nuevo. 🙌"
                                    : "Thanks for contacting us! If you need anything else, just write again. 🙌";
                                await _sender.SendTextAsync(input.PhoneE164, bye);
                                st.Stage = Stage.Menu;
                                return;
                            }
                        }

                        await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.AskAnother, st.Language));
                        await SendAskAnother(st.Language, input.PhoneE164);
                        return;
                    }

                case Stage.CreditAgent:
                    {
                        var opt = MapToOption(up);
                        if (IsValidOption(Stage.CreditAgent, st.Language, opt))
                        {
                            if (opt == "1")
                            {
                                _convBus.MarkAgentRequested(input.ConversationId);
                                var txt = st.Language == Lang.Es
                                    ? "Perfecto. Un asesor se comunicará contigo pronto. 😊"
                                    : "Great. An advisor will contact you shortly. 😊";
                                await _sender.SendTextAsync(input.PhoneE164, txt);
                                st.Stage = Stage.WaitAgent;
                                return;
                            }
                            if (opt == "2")
                            {
                                st.Stage = Stage.AskAnother;
                                await SendAskAnother(st.Language, input.PhoneE164);
                                return;
                            }
                        }

                        await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.CreditAgent, st.Language));
                        await SendCreditosMensaje(st.Language, input.PhoneE164);
                        return;
                    }
            }
        }

        // Helper centralizado
        private async Task SendVehicleAndPostOptions(
            ConvState st,
            FlowInput input,
            string url,
            string descEs,
            string descEn,
            string linkEs,
            string linkEn,
            Stage originMenu)
        {
            await SendVehicleImageWithCaption(st.Language, input.PhoneE164, url, descEs, descEn, linkEs, linkEn);
            await Task.Delay(ImageToMenuDelayMs);              // Delay principal
            st.LastVehicleMenuStage = originMenu;
            st.Stage = Stage.AfterVehicleView;
            await SendPostVehicleOptions(st.Language, input.PhoneE164);
        }

        // Scooters
        private Task SendScootersMenu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Modelos Scooters 🛵\n1️⃣ NAVI\n2️⃣ ELITE\n3️⃣ WAVE 110S\n4️⃣ ACTIVA 5G\n5️⃣ X-ADV 750\n6️⃣ ADV350\n7️⃣ Regresar al menú anterior"
                : "Scooter Models 🛵\n1️⃣ NAVI\n2️⃣ ELITE\n3️⃣ WAVE 110S\n4️⃣ ACTIVA 5G\n5️⃣ X-ADV 750\n6️⃣ ADV350\n7️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleScooterSelection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt is null)
            {
                if (!handoffActive)
                {
                    await _sender.SendTextAsync(input.PhoneE164, st.Language == Lang.Es ? "⚠️ Opción no válida (1️⃣-7️⃣)." : "⚠️ Invalid option (1️⃣-7️⃣).");
                    await SendScootersMenu(st.Language, input.PhoneE164);
                }
                return true;
            }
            if (opt == "7")
            {
                st.Stage = Stage.MotosMenu;
                await SendMotosTypesMenu(st.Language, input.PhoneE164);
                return true;
            }

            var data = opt switch
            {
                "1" => ("https://multiagenciacobano.com/wp-content/uploads/2025/06/NAVI-MOBILE-.png", "NAVI: Compacta, ágil e ideal para la ciudad.", "NAVI: Compact, agile and ideal for the city."),
                "2" => ("https://multiagenciacobano.com/wp-content/uploads/2025/06/ELITE-MOBILE-.png", "ELITE: Diseño moderno y eficiente en consumo.", "ELITE: Modern design and fuel efficient."),
                "3" => ("https://multiagenciacobano.com/wp-content/uploads/2025/06/WAVE-110S-MOBILE-.png", "WAVE 110S: Economía y confiabilidad diaria.", "WAVE 110S: Daily economy & reliability."),
                "4" => ("https://multiagenciacobano.com/wp-content/uploads/2025/06/ACTIVA-5G-MOBILE-.png", "ACTIVA 5G: Comodidad y tecnología práctica.", "ACTIVA 5G: Comfort and practical tech."),
                "5" => ("https://multiagenciacobano.com/wp-content/uploads/2025/06/XADV750-2-1024x851.png", "X-ADV 750: Aventura y prestaciones premium.", "X-ADV 750: Adventure & premium performance."),
                "6" => ("https://multiagenciacobano.com/wp-content/uploads/2025/07/AFRICA-TWIN-STD-4.png", "ADV350: Versatilidad urbana con espíritu aventurero.", "ADV350: Urban versatility with adventure spirit."),
                _ => default
            };

            if (data != default)
            {
                await SendVehicleAndPostOptions(st, input, data.Item1, data.Item2, data.Item3,
                    "https://multiagenciacobano.com/scooters/", "https://multiagenciacobano.com/scooters/", Stage.ScootersMenu);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.ScootersMenu, st.Language));
                await SendScootersMenu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // Sport
        private Task SendSportMenu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Modelos Sport 🏍️🔥\n1️⃣ CGL125\n2️⃣ CGL150 CARGO\n3️⃣ CBF150S\n4️⃣ SHADOW 150\n5️⃣ CB160 HORNET\n6️⃣ XBLADE\n7️⃣ NX190\n8️⃣ CB190R\n9️⃣ CB300F TWISTER\n🔟 CB350 H'NESS\n1️⃣1️⃣ Regresar al menú anterior"
                : "Sport Models 🏍️🔥\n1️⃣ CGL125\n2️⃣ CGL150 CARGO\n3️⃣ CBF150S\n4️⃣ SHADOW 150\n5️⃣ CB160 HORNET\n6️⃣ XBLADE\n7️⃣ NX190\n8️⃣ CB190R\n9️⃣ CB300F TWISTER\n🔟 CB350 H'NESS\n1️⃣1️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleSportSelection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt == "11")
            {
                st.Stage = Stage.MotosMenu;
                await SendMotosTypesMenu(st.Language, input.PhoneE164);
                return true;
            }

            var map = new (string url, string es, string en)[] {
                ("https://multiagenciacobano.com/wp-content/uploads/2025/09/CGL125-MOBILE-1024x843.png","CGL125: Trabajo diario, bajo consumo.","CGL125: Daily workhorse, great fuel economy."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/GL150-CARGO-.png","CGL150 CARGO: Versátil para carga ligera.","CGL150 CARGO: Versatile for light cargo."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CBF15OS.png","CBF150S: Equilibrio rendimiento/eficiencia.","CBF150S: Balanced performance & efficiency."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/SHADOW-150-.png","SHADOW 150: Estilo clásico y comodidad.","SHADOW 150: Classic style & comfort."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CB160-HORNET.png","CB160 HORNET: Deportiva ágil y moderna.","CB160 HORNET: Agile, modern sporty ride."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/XBLADE.png","XBLADE: Diseño agresivo y tecnología.","XBLADE: Aggressive styling & tech."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/NX-190.png","NX190: Lista para ciudad y aventura ligera.","NX190: Ready for city & light adventure."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CB190R-2.png","CB190R: Performance deportivo superior.","CB190R: Superior sporty performance."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CB300F-TWISTER.png","CB300F TWISTER: Potencia y control liviano.","CB300F TWISTER: Power & lightweight control."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CB350-HNESS.png","CB350 H'NESS: Retro premium con torque.","CB350 H'NESS: Premium retro with torque.")
            };

            if (int.TryParse(opt, out var idx) && idx is >= 1 and <= 10)
            {
                var v = map[idx - 1];
                await SendVehicleAndPostOptions(st, input, v.url, v.es, v.en,
                    "https://multiagenciacobano.com/motos-sport/", "https://multiagenciacobano.com/motos-sport/", Stage.SportMenu);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SportMenu, st.Language));
                await SendSportMenu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // Dual Purpose
        private Task SendDualPurposeMenu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Doble Propósito 🏔️\n1️⃣ XR150L\n2️⃣ XR190L\n3️⃣ XR190CT\n4️⃣ XR300L\n5️⃣ CRF300L\n6️⃣ CRF300L RALLY\n7️⃣ SAHARA 300\n8️⃣ SAHARA 300 RALLY\n9️⃣ Regresar al menú anterior"
                : "Dual Purpose 🏔️\n1️⃣ XR150L\n2️⃣ XR190L\n3️⃣ XR190CT\n4️⃣ XR300L\n5️⃣ CRF300L\n6️⃣ CRF300L RALLY\n7️⃣ SAHARA 300\n8️⃣ SAHARA 300 RALLY\n9️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleDualPurposeSelection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt == "9")
            {
                st.Stage = Stage.MotosMenu;
                await SendMotosTypesMenu(st.Language, input.PhoneE164);
                return true;
            }

            var map = new (string url, string es, string en)[] {
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/XR150L.png","XR150L: Ligera, confiable para ciudad y camino.","XR150L: Light, reliable for street & trail."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/XR190L-1.png","XR190L: Más potencia y suspensión para aventuras.","XR190L: More power & suspension for adventures."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/XR190CT.png","XR190CT: Enfoque utilitario con capacidad dual.","XR190CT: Utility-focused with dual capability."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF250F-tornado-MOBILE-.png","XR300L: Versátil 300cc para terrenos variados.","XR300L: Versatile 300cc for varied terrain."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF300L.png","CRF300L: Chasis liviano y respuesta off-road.","CRF300L: Lightweight chassis & off-road response."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF300RALLY-1.png","CRF300L RALLY: Inspiración rally, mayor protección.","CRF300L RALLY: Rally-inspired, added protection."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/SAHARA-300.png","SAHARA 300: Estilo robusto y desempeño equilibrado.","SAHARA 300: Robust style & balanced performance."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/SAHARA-300-RALLY.png","SAHARA 300 RALLY: Para largas travesías, ergonomía y resistencia.","SAHARA 300 RALLY: Long journeys, ergonomics & durability.")
            };

            if (int.TryParse(opt, out var idx) && idx is >= 1 and <= 8)
            {
                var v = map[idx - 1];
                await SendVehicleAndPostOptions(st, input, v.url, v.es, v.en,
                    "https://multiagenciacobano.com/doble-proposito/", "https://multiagenciacobano.com/doble-proposito/", Stage.DualPurposeMenu);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.DualPurposeMenu, st.Language));
                await SendDualPurposeMenu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // High CC
        private Task SendHighCcMenu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Alta Cilindrada 💪\n1️⃣ NX500\n2️⃣ CB 500 HORNET\n3️⃣ CBR500R\n4️⃣ CB650R\n5️⃣ CB650RE CLUTCH\n6️⃣ CBR650RE CLUTCH\n7️⃣ NC750XD\n8️⃣ TRANSALP\n9️⃣ CB750 HORNET\n🔟 NT1100 MT\n1️⃣1️⃣ NT1100 DCT ES\n1️⃣2️⃣ AFRICA TWIN ADV SPORTS DCT\n1️⃣3️⃣ AFRICA TWIN ADVENTURE SPORTS\n1️⃣4️⃣ Regresar al menú anterior"
                : "High Displacement 💪\n1️⃣ NX500\n2️⃣ CB 500 HORNET\n3️⃣ CBR500R\n4️⃣ CB650R\n5️⃣ CB650RE CLUTCH\n6️⃣ CBR650RE CLUTCH\n7️⃣ NC750XD\n8️⃣ TRANSALP\n9️⃣ CB750 HORNET\n🔟 NT1100 MT\n1️⃣1️⃣ NT1100 DCT ES\n1️⃣2️⃣ AFRICA TWIN ADV SPORTS DCT\n1️⃣3️⃣ AFRICA TWIN ADVENTURE SPORTS\n1️⃣4️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleHighCcSelection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt == "14")
            {
                st.Stage = Stage.MotosMenu;
                await SendMotosTypesMenu(st.Language, input.PhoneE164);
                return true;
            }

            var map = new (string url, string es, string en)[] {
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/NX500-1.png","NX500: Trail media versátil y cómoda para viajes.","NX500: Versatile mid-size trail, comfy for touring."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CB500-HORNET-.png","CB 500 HORNET: Naked ágil y divertida.","CB 500 HORNET: Agile, fun naked bike."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CBR500R.png","CBR500R: Deportiva equilibrada para el día a día.","CBR500R: Balanced daily sportbike."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CB650R-2.png","CB650R: Neo‑sports café con carácter y potencia.","CB650R: Neo‑sports café with character and power."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CB650R-E-CLUCTCH-1.png","CB650R E‑Clutch: Cambios sin embrague, más comodidad.","CB650R E‑Clutch: Clutchless shifts, extra comfort."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CB650RE-CLUCTCH-1.png","CBR650R E‑Clutch: Control deportivo y tecnología.","CBR650R E‑Clutch: Sport control with tech."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/NC750X.png","NC750X D: Crossover eficiente, gran practicidad.","NC750X D: Efficient crossover with great practicality."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRANSLAP.png","TRANSALP: Aventura media legendaria.","TRANSALP: Legendary mid-size adventure."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/09/CB750HORNET-MOBILE.png","CB750 HORNET: Naked potente, ligera y tecnológica.","CB750 HORNET: Powerful, lightweight, techy naked."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/NT1100-MT.png","NT1100 MT: Touring confortable, caja manual.","NT1100 MT: Comfortable tourer, manual gearbox."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/NT1100-DCT-ES.png","NT1100 DCT ES: Touring con DCT y suspensión electrónica.","NT1100 DCT ES: Touring with DCT and electronic suspension."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/AFRICA-TWIN-ADV-SPORT.png","Africa Twin Adv Sports DCT: Aventura premium, largo alcance.","Africa Twin Adv Sports DCT: Premium adventure, long range."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/AFRICA-TWIN-ADVENTURE-SPORT.png","Africa Twin Adventure Sports: Lista para largas travesías.","Africa Twin Adventure Sports: Built for long-distance adventures.")
            };

            if (int.TryParse(opt, out var idx) && idx is >= 1 and <= 13)
            {
                var v = map[idx - 1];
                await SendVehicleAndPostOptions(st, input, v.url, v.es, v.en,
                    "https://multiagenciacobano.com/moto-alta-cilindrada/", "https://multiagenciacobano.com/moto-alta-cilindrada/", Stage.HighCcMenu);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.HighCcMenu, st.Language));
                await SendHighCcMenu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // Specials
        private Task SendSpecialsMenu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Motos Especiales 🏍️✨\n1️⃣ CRF110F\n2️⃣ MONKEY\n3️⃣ CRF150R EXPERT\n4️⃣ CRF250F\n5️⃣ CRF250R\n6️⃣ CRF250RX\n7️⃣ CRF450R\n8️⃣ CRF450RX\n9️⃣ CRF450X\n1️⃣0️⃣ Regresar al menú anterior"
                : "Special Motorcycles 🏍️✨\n1️⃣ CRF110F\n2️⃣ MONKEY\n3️⃣ CRF150R EXPERT\n4️⃣ CRF250F\n5️⃣ CRF250R\n6️⃣ CRF250RX\n7️⃣ CRF450R\n8️⃣ CRF450RX\n9️⃣ CRF450X\n1️⃣0️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleSpecialSelection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt == "10")
            {
                st.Stage = Stage.MotosMenu;
                await SendMotosTypesMenu(st.Language, input.PhoneE164);
                return true;
            }

            var map = new (string url, string es, string en)[] {
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF110F.png","CRF110F: Ideal para iniciar en off‑road, fácil y confiable.","CRF110F: Perfect to start off‑road, easy and reliable."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/MONKEY-.png","MONKEY: Estilo retro, compacta y muy divertida.","MONKEY: Retro style, compact and great fun."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF150R-EXPERT-.png","CRF150R EXPERT: Competencia MX ligera y precisa.","CRF150R EXPERT: Lightweight, precise MX race bike."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF250f.png","CRF250F: Versátil para trail y recreación off‑road.","CRF250F: Versatile for trail and off‑road fun."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF250R.png","CRF250R: Motocross de alto rendimiento, lista para pista.","CRF250R: High‑performance motocross, track‑ready."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF250RX.png","CRF250RX: Enduro cross‑country ágil y eficiente.","CRF250RX: Agile, efficient cross‑country enduro."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/crf450r.png","CRF450R: Referente MX 450, potencia y control.","CRF450R: 450 MX benchmark, power and control."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF450RX-.png","CRF450RX: Enduro 450 para cross‑country exigente.","CRF450RX: 450 enduro for demanding cross‑country."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/CRF450X-.png","CRF450X: Off‑road resistente, lista para largas rutas.","CRF450X: Durable off‑road, ready for long routes.")
            };

            if (int.TryParse(opt, out var idx) && idx is >= 1 and <= 9)
            {
                var v = map[idx - 1];
                await SendVehicleAndPostOptions(st, input, v.url, v.es, v.en,
                    "https://multiagenciacobano.com/motos-especiales-2/", "https://multiagenciacobano.com/motos-especiales-2/", Stage.SpecialsMenu);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SpecialsMenu, st.Language));
                await SendSpecialsMenu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // Quads
        private Task SendQuadMenu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Cuadriciclos (ATV) 🛞\n1️⃣ TRX90X\n2️⃣ TRX250X\n3️⃣ TRX250TE\n4️⃣ TRX420TM\n5️⃣ TRX420FM\n6️⃣ TRX420FA2\n7️⃣ TRX520FM1\n8️⃣ TRX520FA6\n9️⃣ TRX520FA7\n🔟 TRX520FM6\n1️⃣1️⃣ TRX700FA5\n1️⃣2️⃣ Regresar al menú anterior"
                : "Quad Bikes (ATV) 🛞\n1️⃣ TRX90X\n2️⃣ TRX250X\n3️⃣ TRX250TE\n4️⃣ TRX420TM\n5️⃣ TRX420FM\n6️⃣ TRX420FA2\n7️⃣ TRX520FM1\n8️⃣ TRX520FA6\n9️⃣ TRX520FA7\n🔟 TRX520FM6\n1️⃣1️⃣ TRX700FA5\n1️⃣2️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleQuadSelection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt == "12")
            {
                st.Stage = Stage.MotosMenu;
                await SendMotosTypesMenu(st.Language, input.PhoneE164);
                return true;
            }

            var map = new (string url, string es, string en)[] {
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/trx90xmobile.png","TRX90X: ATV juvenil, ideal para iniciar con seguridad.","TRX90X: Youth ATV, great to start safely."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRX90X-1.png","TRX250X: Deportivo recreativo 250cc, ágil y divertido.","TRX250X: 250cc recreational sport, agile and fun."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRX250TE.png","TRX250TE: Utilitario automático, práctico y confiable.","TRX250TE: Utility automatic, practical and reliable."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRX420TM.png","TRX420TM: 420cc 2x4 manual, hecho para el trabajo.","TRX420TM: 420cc 2x4 manual, built for work."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRX420FM.png","TRX420FM: 420cc 4x4 manual, tracción y resistencia.","TRX420FM: 420cc 4x4 manual, traction and durability."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRX700FA2.png","TRX420FA2: 420cc 4x4 automático, comodidad y control.","TRX420FA2: 420cc 4x4 automatic, comfort and control."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/07/TRX520FM1-1024x854.png","TRX520FM1: 520cc 4x4 manual, robusto y confiable.","TRX520FM1: 520cc 4x4 manual, robust and reliable."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRX520FA6-1.png","TRX520FA6: 520cc 4x4 automático, potencia y eficiencia.","TRX520FA6: 520cc 4x4 automatic, power and efficiency."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRX520fA7-1.png","TRX520FA7: 520cc 4x4, tecnología y gran capacidad.","TRX520FA7: 520cc 4x4, technology and great capability."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRX520FM6-1.png","TRX520FM6: 520cc 4x4 manual, listo para faena dura.","TRX520FM6: 520cc 4x4 manual, ready for hard work."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TRX700FA5.png","TRX700FA5: 700cc 4x4, máximo desempeño y fuerza.","TRX700FA5: 700cc 4x4, maximum performance and strength.")
            };

            if (int.TryParse(opt, out var idx) && idx is >= 1 and <= 11)
            {
                var v = map[idx - 1];
                await SendVehicleAndPostOptions(st, input, v.url, v.es, v.en,
                    "https://multiagenciacobano.com/atv/", "https://multiagenciacobano.com/atv/", Stage.QuadsMenu);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.QuadsMenu, st.Language));
                await SendQuadMenu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // SxS Categories
        private Task SendSxsCategoriesMenu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Side by Side 🚙\n1️⃣ Línea Pioneer\n2️⃣ Talon 2 Plazas\n3️⃣ Talon 4 Plazas\n4️⃣ Regresar al menú anterior"
                : "Side by Side 🚙\n1️⃣ Pioneer Line\n2️⃣ Talon 2 Seats\n3️⃣ Talon 4 Seats\n4️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleSxsCategoriesSelection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt == "4")
            {
                st.Stage = Stage.MotosMenu;
                await SendMotosTypesMenu(st.Language, input.PhoneE164);
                return true;
            }
            if (opt == "1")
            {
                st.Stage = Stage.SxsPioneerMenu;
                await SendSxsPioneerMenu(st.Language, input.PhoneE164);
                return true;
            }
            if (opt == "2")
            {
                st.Stage = Stage.SxsTalon2Menu;
                await SendSxsTalon2Menu(st.Language, input.PhoneE164);
                return true;
            }
            if (opt == "3")
            {
                st.Stage = Stage.SxsTalon4Menu;
                await SendSxsTalon4Menu(st.Language, input.PhoneE164);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SxsCategoriesMenu, st.Language));
                await SendSxsCategoriesMenu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // Pioneer
        private Task SendSxsPioneerMenu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Línea Pioneer 🚙\n1️⃣ PIONEER 520\n2️⃣ PIONEER 700-4\n3️⃣ PIONEER 1000-M5\n4️⃣ PIONEER 1000-6\n5️⃣ Regresar al menú anterior"
                : "Pioneer Line 🚙\n1️⃣ PIONEER 520\n2️⃣ PIONEER 700-4\n3️⃣ PIONEER 1000-M5\n4️⃣ PIONEER 1000-6\n5️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleSxsPioneerSelection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt == "5")
            {
                st.Stage = Stage.SxsCategoriesMenu;
                await SendSxsCategoriesMenu(st.Language, input.PhoneE164);
                return true;
            }

            var map = new (string url, string es, string en)[] {
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/PIONNER-510.png","PIONEER 520: Compacto utilitario 4x4, ideal para trabajo en fincas.","PIONEER 520: Compact 4x4 utility, ideal for farm work."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/PIONNER-7004-.png","PIONEER 700-4: Configurable 4 plazas, versatilidad y durabilidad.","PIONEER 700-4: Configurable 4-seat, versatile and durable."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/PIONNER-1000-5-.png","PIONEER 1000-M5: 5 plazas, potencia y confort.","PIONEER 1000-M5: 5-seat, power & comfort."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/07/Pionner-1000-6-MOBILE-4-1024x852.png","PIONEER 1000-6: 6 plazas, máxima capacidad.","PIONEER 1000-6: 6-seat, maximum capacity.")
            };

            if (int.TryParse(opt, out var idx) && idx is >= 1 and <= 4)
            {
                var v = map[idx - 1];
                await SendVehicleAndPostOptions(st, input, v.url, v.es, v.en,
                    "https://multiagenciacobano.com/side-by-side-2/", "https://multiagenciacobano.com/side-by-side-2/", Stage.SxsPioneerMenu);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SxsPioneerMenu, st.Language));
                await SendSxsPioneerMenu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // Talon 2
        private Task SendSxsTalon2Menu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Talon 2 Plazas 🚙\n1️⃣ TALON 1000 XD LIVE VALVE\n2️⃣ Regresar al menú anterior"
                : "Talon 2 Seats 🚙\n1️⃣ TALON 1000 XD LIVE VALVE\n2️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleSxsTalon2Selection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt == "2")
            {
                st.Stage = Stage.SxsCategoriesMenu;
                await SendSxsCategoriesMenu(st.Language, input.PhoneE164);
                return true;
            }
            if (opt == "1")
            {
                await SendVehicleAndPostOptions(st, input,
                    "https://multiagenciacobano.com/wp-content/uploads/2025/07/TALON-100O-XD-MOBILE-1024x851.png",
                    "TALON 1000 XD LIVE VALVE (2 plazas): Suspensión electrónica Live Valve y desempeño deportivo.",
                    "TALON 1000 XD LIVE VALVE (2 seats): Live Valve electronic suspension & sporty performance.",
                    "https://multiagenciacobano.com/side-by-side-2/",
                    "https://multiagenciacobano.com/side-by-side-2/",
                    Stage.SxsTalon2Menu);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SxsTalon2Menu, st.Language));
                await SendSxsTalon2Menu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // Talon 4
        private Task SendSxsTalon4Menu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Talon 4 Plazas 🚙\n1️⃣ TALON 1000 XD LIVE VALVE\n2️⃣ TALON 1000X-4\n3️⃣ Regresar al menú anterior"
                : "Talon 4 Seats 🚙\n1️⃣ TALON 1000 XD LIVE VALVE\n2️⃣ TALON 1000X-4\n3️⃣ Return to previous menu";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task<bool> HandleSxsTalon4Selection(string? opt, ConvState st, FlowInput input, bool handoffActive)
        {
            if (opt == "3")
            {
                st.Stage = Stage.SxsCategoriesMenu;
                await SendSxsCategoriesMenu(st.Language, input.PhoneE164);
                return true;
            }

            var map = new (string url, string es, string en)[] {
                ("https://multiagenciacobano.com/wp-content/uploads/2025/07/TALON-100O-XD-MOBILE-2-1024x851.png","TALON 1000 XD LIVE VALVE (4 plazas): Misma tecnología Live Valve con espacio para 4.","TALON 1000 XD LIVE VALVE (4 seats): Same Live Valve tech with space for 4."),
                ("https://multiagenciacobano.com/wp-content/uploads/2025/06/TALON-1000X-4.png","TALON 1000X-4: Chasis ágil y potencia para rutas exigentes en 4 plazas.","TALON 1000X-4: Agile chassis & power for demanding trails in 4 seats.")
            };

            if (int.TryParse(opt, out var idx) && idx is >= 1 and <= 2)
            {
                var v = map[idx - 1];
                await SendVehicleAndPostOptions(st, input, v.url, v.es, v.en,
                    "https://multiagenciacobano.com/side-by-side-2/", "https://multiagenciacobano.com/side-by-side-2/", Stage.SxsTalon4Menu);
                return true;
            }

            if (!handoffActive)
            {
                await _sender.SendTextAsync(input.PhoneE164, BuildInvalidOptionMessage(Stage.SxsTalon4Menu, st.Language));
                await SendSxsTalon4Menu(st.Language, input.PhoneE164);
            }
            return true;
        }

        // Menú principal y otros
        private Task SendMainMenu(Lang lang, string to)
        {
            if (lang == Lang.Unknown)
            {
                var choose = "Hola 👋 / Hello 👋\nBienvenido a Multiagencia Cóbano 🏍️\nWelcome to Multiagencia Cóbano 🏍️\nPor favor, selecciona tu idioma 🌎 / Please select your language 🌎\n1️⃣ Español\n2️⃣ English";
                return _sender.SendTextAsync(to, choose);
            }

            var msg = lang == Lang.Es
                ? "En Multiagencia Cóbano es un gusto atenderte 😁\n¿En qué te puedo ayudar hoy? 🤔\n1️⃣ Información de los vehículos disponibles 🏍️\n2️⃣ Información sobre repuestos y accesorios disponibles 🛠️\n3️⃣ Formas de crédito 💳\n4️⃣ Horario y ubicación 📍🕒\n5️⃣ Hablar con un asesor 👤"
                : "At Multiagencia Cóbano, it's a pleasure to assist you 😁\nHow can I help you today? 🤔\n1️⃣ Information about available vehicles 🏍️\n2️⃣ Information about available spare parts 🛠️\n3️⃣ Forms of credit 💳\n4️⃣ Hours and Address 📍🕒\n5️⃣ Talk to an advisor 👤";
            return _sender.SendTextAsync(to, msg);
        }

        private Task SendMotosTypesMenu(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Selecciona el tipo de vehículo 🏍️\n1️⃣ Scooters\n2️⃣ Motos Sport\n3️⃣ Motos Doble Propósito\n4️⃣ Motos Alta Cilindrada\n5️⃣ Cuadriciclos\n6️⃣ Motos Especiales\n7️⃣ Side by sides\n8️⃣ Regresar al menú principal"
                : "Select vehicle type 🏍️\n1️⃣ Scooters\n2️⃣ Sport Motorcycles\n3️⃣ Dual Purpose Motorcycles\n4️⃣ High Displacement Motorcycles\n5️⃣ Quadracycles\n6️⃣ Special Motorcycles\n7️⃣ Side by sides\n8️⃣ Return to main menu";
            return _sender.SendTextAsync(to, msg);
        }

        private Task SendAskAnother(Lang lang, string to)
        {
            var msg = lang == Lang.Es ? "🤔 ¿Tiene alguna otra consulta?\n1️⃣ Sí\n2️⃣ No" : "🤔 Do you have another inquiry?\n1️⃣ Yes\n2️⃣ No";
            return _sender.SendTextAsync(to, msg);
        }

        private Task SendRepuestosMensaje(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "🔧 ¡Encontrá el repuesto exacto en segundos!\n📍 Ingresá ya y descubrí todo lo que tenemos para tu equipo:\n https://multiagenciacobano.com/servicio-y-repuestos/"
                : "🔧 Find the exact spare part in seconds!\n📍 Visit now and discover everything we have for your equipment:\n https://multiagenciacobano.com/servicio-y-repuestos/";
            return _sender.SendTextAsync(to, msg);
        }

        private Task SendCreditosMensaje(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "💳 Información de créditos y requisitos.\n¿Hablar con asesor?\n1️⃣ Sí 2️⃣ No"
                : "💳 Loan information and requirements.\nTalk to advisor?\n1️⃣ Yes 2️⃣ No";
            return _sender.SendTextAsync(to, msg);
        }

        private async Task SendHorarioUbicacionMensaje(Lang lang, string to)
        {
            const double lat = 9.680631406109592;
            const double lng = -85.09637682406724;
            var name = "Multiagencia Cóbano";
            var addressEs = "Puntarenas, Cóbano, 150 m norte del Cementerio de Cóbano, carretera a Montezuma";
            var addressEn = "Puntarenas, Cóbano, 150 m north of the Cóbano Cemetery, Montezuma Road";

            await _sender.SendLocationAsync(to, lat, lng, name, lang == Lang.Es ? addressEs : addressEn);

            var msg = lang == Lang.Es
                ? "📍DIRECCIÓN\n" + addressEs + "\n🕒 L-V: 8:00–17:00\nSáb: 8:00–13:00\nDom: Cerrado"
                : "📍ADDRESS\n" + addressEn + "\n🕒 Mon-Fri: 8:00–17:00\nSat: 8:00–13:00\nSun: Closed";
            await _sender.SendTextAsync(to, msg);
        }

        private async Task SendFinancingPdfAsync(string to, Lang lang)
        {
            const string url = "https://multiagenciacobano.com/wp-content/uploads/2025/11/Creditos_y_Requisitos.pdf";
            var caption = lang == Lang.Es ? "💳 Información de financiamiento" : "💳 Financing information";
            const string filename = "Creditos_y_Requisitos.pdf";
            await _sender.SendDocumentByUrlAsync(to, url, caption, filename);
        }

        // Enviar imagen con caption (descripción + link)
        private Task SendVehicleImageWithCaption(
            Lang lang,
            string to,
            string imageUrl,
            string descEs,
            string descEn,
            string linkEs,
            string linkEn)
        {
            var caption = lang == Lang.Es
                ? $"{descEs}\n🔗 Conoce más detalles: {linkEs}"
                : $"{descEn}\n🔗 Know more details: {linkEn}";
            return _sender.SendImageByUrlAsync(to, imageUrl, caption);
        }

        // opciones tras ver un vehículo
        private Task SendPostVehicleOptions(Lang lang, string to)
        {
            var msg = lang == Lang.Es
                ? "Elige una opción:\n1️⃣ Regresar al menú anterior\n2️⃣ Regresar al menú principal"
                : "Choose an option:\n1️⃣ Return to previous menu\n2️⃣ Return to main menu";
            return _sender.SendTextAsync(to, msg);
        }

        // reenvía el menú correcto según la categoría recordada
        private Task SendMenuForStage(Stage menuStage, Lang lang, string to)
        {
            return menuStage switch
            {
                Stage.ScootersMenu => SendScootersMenu(lang, to),
                Stage.SportMenu => SendSportMenu(lang, to),
                Stage.DualPurposeMenu => SendDualPurposeMenu(lang, to),
                Stage.HighCcMenu => SendHighCcMenu(lang, to),
                Stage.QuadsMenu => SendQuadMenu(lang, to),
                Stage.SpecialsMenu => SendSpecialsMenu(lang, to),
                Stage.SxsPioneerMenu => SendSxsPioneerMenu(lang, to),
                Stage.SxsTalon2Menu => SendSxsTalon2Menu(lang, to),
                Stage.SxsTalon4Menu => SendSxsTalon4Menu(lang, to),
                _ => SendMotosTypesMenu(lang, to)
            };
        }

        private async Task CloseConversationAsync(int conversationId)
        {
            try
            {
                var r = _convBus.Find(conversationId);
                if (r?.Data != null)
                {
                    r.Data.Status = "closed";
                    r.Data.EndedAt = DateTime.UtcNow;
                    _convBus.Update(r.Data);
                }
            }
            catch { }
        }

        private bool IsAgentHandoffActive(int conversationId)
        {
            try
            {
                var r = _convBus.Find(conversationId);
                if (r?.Data == null) return false;
                var status = (r.Data.Status ?? "open").ToLowerInvariant();
                var hasFlag = r.Data.GetType().GetProperty("AgentRequestedAt") != null
                              && (r.Data.GetType().GetProperty("AgentRequestedAt")!
                                  .GetValue(r.Data) as System.DateTime?) != null;
                return hasFlag && status == "open";
            }
            catch { return false; }
        }

        // === Helpers de validación dinámicos ===

        private static IReadOnlyList<int> GetValidOptions(Stage stage, Lang lang)
        {
            return stage switch
            {
                Stage.Menu => (lang == Lang.Unknown) ? new[] { 1, 2 } : new[] { 1, 2, 3, 4, 5 },
                Stage.MotosMenu => Enumerable.Range(1, 8).ToArray(),
                Stage.ScootersMenu => Enumerable.Range(1, 7).ToArray(),
                Stage.SportMenu => Enumerable.Range(1, 11).ToArray(),
                Stage.DualPurposeMenu => Enumerable.Range(1, 9).ToArray(),
                Stage.HighCcMenu => Enumerable.Range(1, 14).ToArray(),
                Stage.QuadsMenu => Enumerable.Range(1, 12).ToArray(),
                Stage.SpecialsMenu => Enumerable.Range(1, 10).ToArray(),
                Stage.SxsCategoriesMenu => Enumerable.Range(1, 4).ToArray(),
                Stage.SxsPioneerMenu => Enumerable.Range(1, 5).ToArray(),
                Stage.SxsTalon2Menu => Enumerable.Range(1, 2).ToArray(),
                Stage.SxsTalon4Menu => Enumerable.Range(1, 3).ToArray(),
                Stage.AfterVehicleView => new[] { 1, 2 },
                Stage.AskAnother => new[] { 1, 2 },
                Stage.CreditAgent => new[] { 1, 2 },
                Stage.WaitAgent => Array.Empty<int>(),
                _ => Array.Empty<int>()
            };
        }

        private static bool IsValidOption(Stage stage, Lang lang, string? opt)
        {
            if (opt is null) return false;
            var list = GetValidOptions(stage, lang);
            return int.TryParse(opt, out var num) && list.Contains(num);
        }

        private static string BuildInvalidOptionMessage(Stage stage, Lang lang)
        {
            var list = GetValidOptions(stage, lang);
            if (list.Count == 0)
                return lang == Lang.Es ? "⚠️ Esta etapa no acepta opciones." : "⚠️ This stage does not accept options.";

            var min = list.Min();
            var max = list.Max();
            var rangeText = list.Count <= 6 && max <= 10
                ? string.Join(", ", list)
                : $"{min} y {max}";

            return lang == Lang.Es
                ? $"⚠️ Opción inválida. Usa un número entre {min} y {max}."
                : $"⚠️ Invalid option. Use a number between {min} and {max}.";
        }

        private static bool IsExplicitCommand(string up, Stage stage, Lang lang)
        {
            if (up == "MENU" || up == "MENU ") return true;
            var opt = MapToOption(up);
            if (stage == Stage.AskAnother) return IsValidOption(stage, lang, opt);
            if (stage == Stage.CreditAgent) return IsValidOption(stage, lang, opt);
            if (stage == Stage.AfterVehicleView) return IsValidOption(stage, lang, opt);
            if (lang == Lang.Unknown) return IsValidOption(Stage.Menu, lang, opt);
            return IsValidOption(stage, lang, opt);
        }

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

        private static string? MapToOption(string up)
        {
            var m = Regex.Match(up, @"^\(\s*(1[0-4]|[1-9])\s*\)$");
            if (m.Success) return m.Groups[1].Value;
            return Regex.IsMatch(up, @"^(1[0-4]|[1-9])$") ? up : null;
        }
    }
}