// /assets/js/Reports.js

(function () {
    'use strict';

    // =================== Helpers generales ===================

    function $(selector) {
        return document.querySelector(selector);
    }

    function setText(id, value) {
        var el = document.getElementById(id);
        if (!el) return;
        el.textContent = (value === null || value === undefined || isNaN(value) && value === '')
            ? '—'
            : String(value);
    }

    function setHtml(id, html) {
        var el = document.getElementById(id);
        if (!el) return;
        el.innerHTML = html;
    }

    async function fetchJson(url) {
        if (!url) return null;
        const resp = await fetch(url, { headers: { 'Accept': 'application/json' } });
        if (!resp.ok) {
            console.warn('Request failed', url, resp.status);
            return null;
        }
        try {
            return await resp.json();
        } catch {
            console.warn('Response is not JSON', url);
            return null;
        }
    }

    function buildQuery(from, to) {
        const qs = new URLSearchParams();
        if (from) qs.set('from', from);
        if (to) qs.set('to', to);
        return '?' + qs.toString();
    }

    // =================== Charts (Chart.js) ===================

    let lineChart = null;
    let countriesChart = null;

    function buildLine(series) {
        const canvas = document.getElementById('lineMensajes');
        if (!canvas || typeof Chart === 'undefined') {
            // No hay canvas en esta vista o Chart.js no cargó
            return;
        }

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const labels = (series?.points || []).map(p => p.label);
        const values = (series?.points || []).map(p => p.value);

        if (lineChart) {
            lineChart.destroy();
            lineChart = null;
        }

        lineChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Mensajes',
                    data: values,
                    tension: 0.3,
                    fill: true
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false }
                },
                scales: {
                    x: {
                        ticks: { autoSkip: true, maxTicksLimit: 10 }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: { precision: 0 }
                    }
                }
            }
        });
    }

    function buildCountriesChart(data) {
        const canvas = document.getElementById('chartPaises');
        const list = document.getElementById('listaPaises');

        const items = Array.isArray(data) ? data : [];

        // Lista de países
        if (list) {
            const html = items.map(x => {
                const name = x.name || x.code || 'N/A';
                const count = x.count ?? 0;
                return `<li><span>${name}</span><span class="fw-semibold">${count}</span></li>`;
            }).join('');
            list.innerHTML = html || '<li class="text-muted">Sin datos en el rango seleccionado.</li>';
        }

        // Gráfico
        if (!canvas || typeof Chart === 'undefined') {
            return;
        }

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const labels = items.map(x => x.name || x.code || 'N/A');
        const values = items.map(x => x.count ?? 0);

        if (countriesChart) {
            countriesChart.destroy();
            countriesChart = null;
        }

        countriesChart = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: values
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { position: 'bottom' }
                },
                cutout: '55%'
            }
        });
    }

    function buildTopClients(data) {
        const tbody = document.getElementById('tablaClientes');
        if (!tbody) return;

        const items = Array.isArray(data) ? data : [];

        const html = items.map(x => {
            const name = x.name || 'Sin cliente';
            const count = x.count ?? 0;
            return `
<tr>
  <td>${name}</td>
  <td class="text-end">${count}</td>
</tr>`;
        }).join('');

        tbody.innerHTML = html || `
<tr>
  <td colspan="2" class="text-center text-muted">Sin datos en el rango seleccionado.</td>
</tr>`;
    }

    function renderKpis(k, from, to) {
        if (!k) return;

        setText('kpiMensajes', k.totalMessages);
        setText('kpiCierres', k.closedConversations);
        setText('kpiClientesNuevos', k.newClients);

        // textitos de rango (los pequeños debajo)
        const rangeText = from && to ? `${from} a ${to}` : '';
        setText('kpiMensajesRange', rangeText);
        setText('kpiCierresRange', rangeText);
        setText('kpiClientesRange', rangeText);

        // KPIs secundarios
        setText('kpiAbiertas', k.openConversations);
        setText('kpiClientesActivos', k.activeClients);
        setText('kpiPaises', k.activeCountries);

        const prom = (typeof k.avgMessagesPerConversation === 'number')
            ? k.avgMessagesPerConversation.toFixed(1)
            : '—';
        setText('kpiPromMsgs', prom);
    }

    // =================== Render principal ===================

    async function renderAll(from, to) {
        const cfg = window.REPORTS_CFG || {};
        const urls = cfg.urls || {};

        const qs = buildQuery(from, to);

        // Evita llamadas a "undefined?from=..." si falta alguna URL
        const kpisPromise = urls.kpis ? fetchJson(urls.kpis + qs) : Promise.resolve(null);
        const seriesPromise = urls.series ? fetchJson(urls.series + qs) : Promise.resolve(null);
        const countriesPromise = urls.countries ? fetchJson(urls.countries + qs) : Promise.resolve(null);
        const topPromise = urls.topclients ? fetchJson(urls.topclients + qs) : Promise.resolve(null);

        try {
            const [kpis, series, countries, topclients] = await Promise.all([
                kpisPromise, seriesPromise, countriesPromise, topPromise
            ]);

            renderKpis(kpis, from, to);
            buildLine(series);
            buildCountriesChart(countries);
            buildTopClients(topclients);
        } catch (e) {
            console.error('Error renderAll:', e);
            if (window.showError) {
                window.showError('No se pudieron cargar las analíticas.', 'Analíticas');
            }
        }
    }

    // =================== DateRangePicker ===================

    function initDateRange(from, to) {
        const input = $('#dateRange');
        if (!input || typeof window.jQuery === 'undefined' || !window.jQuery.fn.daterangepicker) {
            // No hay daterangepicker; solo renderizamos con el rango actual
            renderAll(from, to);
            return;
        }

        const $input = window.jQuery(input);
        const start = window.moment(from, 'YYYY-MM-DD');
        const end = window.moment(to, 'YYYY-MM-DD');

        $input.daterangepicker(
            {
                startDate: start,
                endDate: end,
                locale: {
                    format: 'YYYY-MM-DD',
                    separator: ' a ',
                    applyLabel: 'Aplicar',
                    cancelLabel: 'Cancelar',
                    fromLabel: 'Desde',
                    toLabel: 'Hasta',
                    customRangeLabel: 'Personalizado',
                    daysOfWeek: ['Do', 'Lu', 'Ma', 'Mi', 'Ju', 'Vi', 'Sa'],
                    monthNames: [
                        'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
                        'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'
                    ],
                    firstDay: 1
                },
                ranges: {
                    'Hoy': [window.moment(), window.moment()],
                    'Últimos 7 días': [window.moment().subtract(6, 'days'), window.moment()],
                    'Últimos 30 días': [window.moment().subtract(29, 'days'), window.moment()],
                    'Este mes': [window.moment().startOf('month'), window.moment().endOf('month')],
                    'Mes pasado': [
                        window.moment().subtract(1, 'month').startOf('month'),
                        window.moment().subtract(1, 'month').endOf('month')
                    ]
                }
            },
            function (start, end) {
                const f = start.format('YYYY-MM-DD');
                const t = end.format('YYYY-MM-DD');
                input.value = `${f} a ${t}`;
                // Re-render con el nuevo rango (sin recargar la página)
                renderAll(f, t);
            }
        );

        // Primer render con el rango inicial
        renderAll(from, to);
    }

    // =================== Init ===================

    document.addEventListener('DOMContentLoaded', function () {
        const cfg = window.REPORTS_CFG;
        if (!cfg) {
            console.warn('REPORTS_CFG no está definido en la vista.');
            return;
        }

        const from = cfg.from || cfg.From || '';
        const to = cfg.to || cfg.To || '';

        initDateRange(from, to);
    });

})();
