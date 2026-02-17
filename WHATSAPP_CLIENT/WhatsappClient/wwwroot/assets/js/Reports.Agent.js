(function () {
    const cfg = window.AGENT_REPORT_CFG || {};

    function parseRange(str) {
        if (!str) return { from: cfg.from, to: cfg.to };
        const parts = str.split("a").map(s => s.trim());
        if (parts.length !== 2) return { from: cfg.from, to: cfg.to };
        return { from: parts[0], to: parts[1] };
    }

    function initDateRange() {
        const $input = $("#agentDateRange");
        if (!$input.length) return;

        const start = moment(cfg.from, "YYYY-MM-DD");
        const end = moment(cfg.to, "YYYY-MM-DD");

        function cb(start, end) {
            $input.val(start.format("YYYY-MM-DD") + " a " + end.format("YYYY-MM-DD"));
        }

        $input.daterangepicker({
            startDate: start,
            endDate: end,
            locale: {
                format: "YYYY-MM-DD",
                separator: " a ",
                applyLabel: "Aplicar",
                cancelLabel: "Cancelar",
                fromLabel: "Desde",
                toLabel: "Hasta",
                customRangeLabel: "Personalizado",
                daysOfWeek: ["Do", "Lu", "Ma", "Mi", "Ju", "Vi", "Sa"],
                monthNames: [
                    "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
                    "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
                ],
                firstDay: 1
            }
        }, cb);
    }

    function fmtNumber(n) {
        if (n == null) return "—";
        return n.toLocaleString("es-CR");
    }

    function loadAgentKpis(agentId, from, to) {
        if (!cfg.urls || !cfg.urls.kpis) return;
        $("#agentKpisRow").hide();

        $.getJSON(cfg.urls.kpis, { agentId, from, to })
            .done(function (data) {
                $("#agKpiClosed").text(fmtNumber(data.closedConversations));
                $("#agKpiMsgs").text(fmtNumber(data.totalMessages));
                $("#agKpiClients").text(fmtNumber(data.clientsHandled));
                $("#agKpiAvgMsgs").text(
                    data.avgMessagesPerConversation != null
                        ? data.avgMessagesPerConversation.toLocaleString("es-CR", { minimumFractionDigits: 1, maximumFractionDigits: 1 })
                        : "—"
                );
                $("#agName").text(data.agentName || "");

                $("#agentKpisRow").fadeIn(150);
            })
            .fail(function () {
                window.showError && window.showError("No se pudieron cargar las métricas del agente.", "Analíticas por agente");
            });
    }

    function loadAgentClosedList(agentId, from, to) {
        if (!cfg.urls || !cfg.urls.closedList) return;

        const $tbody = $("#tablaCierresAgente");
        if (!$tbody.length) return;

        $tbody.empty().append(`
        <tr class="text-muted">
          <td colspan="5" class="text-center small">Cargando...</td>
        </tr>`);

        $.getJSON(cfg.urls.closedList, { agentId, from, to })
            .done(function (data) {
                const items = (data && data.items) || [];
                $tbody.empty();

                if (!items.length) {
                    $tbody.append(`
            <tr class="text-muted">
              <td colspan="5" class="text-center small">
                El agente no tiene conversaciones cerradas en el rango seleccionado.
              </td>
            </tr>`);
                    return;
                }

                items.forEach(function (c) {
                    const id = c.id || "";
                    const cname = c.contactName || "Sin nombre";
                    const phone = c.contactPhone || "";
                    const started = c.startedAt ? new Date(c.startedAt).toLocaleString("es-CR") : "";
                    const ended = c.endedAt ? new Date(c.endedAt).toLocaleString("es-CR") : "";

                    $tbody.append(`
            <tr>
              <td>${id}</td>
              <td>${cname}</td>
              <td>${phone}</td>
              <td>${started}</td>
              <td>${ended}</td>
            </tr>
          `);
                });
            })
            .fail(function () {
                $tbody.empty().append(`
          <tr class="text-muted">
            <td colspan="5" class="text-center small">
              Error al cargar el detalle de conversaciones.
            </td>
          </tr>`);
            });
    }

    function reload() {
        const agentId = parseInt($("#agentSelect").val(), 10);
        if (!agentId) {
            window.showInfo && window.showInfo("Seleccione un agente para ver sus métricas.", "Analíticas por agente");
            return;
        }
        const rangeStr = $("#agentDateRange").val();
        const { from, to } = parseRange(rangeStr);

        loadAgentKpis(agentId, from, to);
        loadAgentClosedList(agentId, from, to);
    }

    $(function () {
        initDateRange();

        $("#btnAgentReload").on("click", reload);
        $("#agentSelect").on("change", function () {
            if (this.value) reload();
        });
    });
})();
