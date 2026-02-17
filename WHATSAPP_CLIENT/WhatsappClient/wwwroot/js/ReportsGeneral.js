// ReportsGeneral.js
// Analíticas generales - gráficos en morado

(function () {
    const cfg = window.REPORTS_CFG || {
        from: '',
        to: '',
        urls: {
            kpis: '/Report/Kpis',
            series: '/Report/Series',
            countries: '/Report/Countries',
            topclients: '/Report/TopClients'
        }
    };

    // === Colores para los gráficos, leídos desde CSS ===
    const rootStyles = getComputedStyle(document.documentElement);

    const CHART_LINE_COLOR =
        (rootStyles.getPropertyValue('--chart-line-color') || '#A860E0').trim();

    const CHART_LINE_FILL =
        (rootStyles.getPropertyValue('--chart-line-fill') || 'rgba(168, 96, 224, 0.15)').trim();

    const CHART_PIE_COLORS = [
        '#A860E0',
        '#B57EDC',
        '#CDABE6',
        '#F4C2FF',
        '#7C4DFF',
        '#6C4AB6',
        '#D0A8FF'
    ];

    let lineChart = null;
    let pieChart = null;

    // ==== Helpers DOM ====

    function $(selector) {
        return document.querySelector(selector);
    }

    function formatNumber(n) {
        if (n === null || n === undefined || isNaN(n)) return '0';
        return Number(n).toLocaleString('es-CR');
    }

    function setText(id, value) {
        const el = document.getElementById(id);
        if (!el) return;
        el.textContent = value;
    }

    // ==== Fetch helper ====

    async function fetchJson(url) {
        const resp = await fetch(url, {
            headers: { 'Accept': 'application/json' }
        });

        if (!resp.ok) {
            console.error('Error HTTP', resp.status, url);
            throw new Error(`HTTP ${resp.status}`);
        }

        return await resp.json();
    }

    // ==== KPIs ====

    function pick(obj, ...names) {
        if (!obj) return null;
        for (const n of names) {
            if (obj[n] !== undefined && obj[n] !== null) return obj[n];
        }
        return null;
    }

    async function loadKpis(from, to) {
        const url = `${cfg.urls.kpis}?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`;
        let data;
        try {
            data = await fetchJson(url);
        } catch (e) {
            console.error('Error KPIs', e);
            return;
        }

        const totalMessages =
            pick(data, 'totalMessages', 'TotalMessages') ?? 0;
        const closedConversations =
            pick(data, 'closedConversations', 'ClosedConversations') ?? 0;
        const newClients =
            pick(data, 'newClients', 'NewClients') ?? 0;

        const openConversations =
            pick(data, 'openConversations', 'OpenConversations') ?? 0;
        const activeClients =
            pick(data, 'activeClients', 'ActiveClients') ?? 0;
        const activeCountries =
            pick(data, 'activeCountries', 'ActiveCountries') ?? 0;
        const avgMessagesPerConversation =
            pick(data, 'avgMessagesPerConversation', 'AvgMessagesPerConversation') ?? 0;

        // KPIs principales
        setText('kpiMensajes', formatNumber(totalMessages));
        setText('kpiCierres', formatNumber(closedConversations));
        setText('kpiClientesNuevos', formatNumber(newClients));

        const rangeText = `Del ${from} al ${to}`;
        setText('kpiMensajesRange', rangeText);
        setText('kpiCierresRange', rangeText);
        setText('kpiClientesRange', rangeText);

        // KPIs secundarios
        setText('kpiAbiertas', formatNumber(openConversations));
        setText('kpiClientesActivos', formatNumber(activeClients));
        setText('kpiPaises', formatNumber(activeCountries));
        setText(
            'kpiPromMsgs',
            avgMessagesPerConversation ? avgMessagesPerConversation.toFixed(1) : '0.0'
        );
    }

    // ==== Serie de mensajes por fecha ====

    async function loadSeries(from, to) {
        const url = `${cfg.urls.series}?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&groupBy=day`;
        let data;
        try {
            data = await fetchJson(url);
        } catch (e) {
            console.error('Error series', e);
            return;
        }

        const points = Array.isArray(data.points)
            ? data.points
            : (Array.isArray(data) ? data : []);

        const labels = points.map(p => p.label ?? p.Label ?? '');
        const values = points.map(p => p.value ?? p.Value ?? 0);

        buildLineChart(labels, values);
    }

    function buildLineChart(labels, values) {
        const canvas = document.getElementById('lineMensajes');
        if (!canvas) {
            console.warn('No se encontró el canvas lineMensajes');
            return;
        }

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        if (lineChart) {
            lineChart.destroy();
        }

        lineChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels,
                datasets: [
                    {
                        label: 'Mensajes',
                        data: values,
                        borderColor: CHART_LINE_COLOR,
                        backgroundColor: CHART_LINE_FILL,
                        borderWidth: 2,
                        tension: 0.3,
                        fill: true,
                        pointRadius: 3,
                        pointBackgroundColor: CHART_LINE_COLOR,
                        pointBorderColor: '#ffffff',
                        pointBorderWidth: 1
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    x: {
                        ticks: {
                            maxRotation: 0,
                            minRotation: 0
                        }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: {
                            precision: 0
                        }
                    }
                },
                plugins: {
                    legend: {
                        display: false
                    },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                const v = ctx.parsed.y ?? 0;
                                return ` ${formatNumber(v)} mensajes`;
                            }
                        }
                    }
                }
            }
        });
    }

    // ==== Países de contactos ====

    async function loadCountries(from, to) {
        const url = `${cfg.urls.countries}?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`;
        let data;
        try {
            data = await fetchJson(url);
        } catch (e) {
            console.error('Error countries', e);
            return;
        }

        const items = Array.isArray(data)
            ? data
            : (Array.isArray(data.data) ? data.data : []);

        const labels = [];
        const values = [];
        const listEl = document.getElementById('listaPaises');
        if (listEl) listEl.innerHTML = '';

        items.forEach(item => {
            const code = (item.code || item.Code || '').toString();
            const name = (item.name || item.Name || code || 'N/D').toString();
            const count = item.count ?? item.Count ?? item.total ?? 0;

            // Para el gráfico: texto simple (CR, US, Otros...)
            const labelForChart = name;
            labels.push(labelForChart);
            values.push(count);

            if (listEl) {
                const li = document.createElement('li');

                const left = document.createElement('span');
                left.style.display = 'inline-flex';
                left.style.alignItems = 'center';
                left.style.gap = '0.35rem';

                if (code && code !== 'OTHERS' && code !== '??') {
                    // Banderita gráfica con flag-icons (fi fi-cr, fi fi-us, etc.)
                    const icon = document.createElement('span');
                    icon.className = `fi fi-${code.toLowerCase()}`;
                    left.appendChild(icon);

                    const txt = document.createElement('span');
                    txt.textContent = code; // CR, MX, US...
                    left.appendChild(txt);
                } else {
                    // Otros / desconocido
                    left.textContent = name;
                }

                const right = document.createElement('span');
                right.textContent = formatNumber(count);

                li.appendChild(left);
                li.appendChild(right);
                listEl.appendChild(li);
            }
        });

        buildPieChart(labels, values);
    }

    function buildPieChart(labels, values) {
        const canvas = document.getElementById('chartPaises');
        if (!canvas) {
            console.warn('No se encontró el canvas chartPaises');
            return;
        }

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        if (pieChart) {
            pieChart.destroy();
        }

        const colors = [];
        for (let i = 0; i < values.length; i++) {
            colors.push(CHART_PIE_COLORS[i % CHART_PIE_COLORS.length]);
        }

        pieChart = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels,
                datasets: [
                    {
                        data: values,
                        backgroundColor: colors,
                        borderColor: '#ffffff',
                        borderWidth: 2
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '60%',
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: {
                            boxWidth: 12,
                            usePointStyle: true
                        }
                    },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                const label = ctx.label || '';
                                const value = ctx.parsed ?? 0;
                                return ` ${label}: ${formatNumber(value)}`;
                            }
                        }
                    }
                }
            }
        });
    }

    // ==== Top clientes ====

    async function loadTopClients(from, to) {
        const url =
            `${cfg.urls.topclients}?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&take=10`;
        let data;
        try {
            data = await fetchJson(url);
        } catch (e) {
            console.error('Error top clients', e);
            return;
        }

        const items = Array.isArray(data)
            ? data
            : (Array.isArray(data.data) ? data.data : []);

        const tbody = document.getElementById('tablaClientes');
        if (!tbody) return;

        tbody.innerHTML = '';

        items.forEach(item => {
            const name = item.name ?? item.cliente ?? item.Cliente ?? 'Cliente';
            const count = item.count ?? item.Count ?? item.total ?? 0;

            const tr = document.createElement('tr');

            const tdName = document.createElement('td');
            tdName.textContent = name;

            const tdCount = document.createElement('td');
            tdCount.className = 'text-end';
            tdCount.textContent = formatNumber(count);

            tr.appendChild(tdName);
            tr.appendChild(tdCount);
            tbody.appendChild(tr);
        });
    }

    // ==== DateRangePicker ====

    function setupDateRange(initialFrom, initialTo) {
        const input = $('#dateRange');
        if (!input) return;
        if (typeof window.jQuery === 'undefined' || !window.moment) return;

        const $input = window.jQuery('#dateRange');
        if (!$input || !$input.daterangepicker) return;

        const start = initialFrom || moment().subtract(29, 'days').format('YYYY-MM-DD');
        const end = initialTo || moment().format('YYYY-MM-DD');

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
                        'Enero',
                        'Febrero',
                        'Marzo',
                        'Abril',
                        'Mayo',
                        'Junio',
                        'Julio',
                        'Agosto',
                        'Septiembre',
                        'Octubre',
                        'Noviembre',
                        'Diciembre'
                    ],
                    firstDay: 1
                },
                ranges: {
                    'Hoy': [moment(), moment()],
                    'Ayer': [moment().subtract(1, 'days'), moment().subtract(1, 'days')],
                    'Últimos 7 días': [moment().subtract(6, 'days'), moment()],
                    'Últimos 30 días': [moment().subtract(29, 'days'), moment()],
                    'Este mes': [moment().startOf('month'), moment().endOf('month')],
                    'Mes pasado': [
                        moment().subtract(1, 'month').startOf('month'),
                        moment().subtract(1, 'month').endOf('month')
                    ]
                }
            },
            function (startDate, endDate) {
                const f = startDate.format('YYYY-MM-DD');
                const t = endDate.format('YYYY-MM-DD');
                renderAll(f, t);
            }
        );
    }

    // ==== Render global ====

    async function renderAll(from, to) {
        try {
            await Promise.all([
                loadKpis(from, to),
                loadSeries(from, to),
                loadCountries(from, to),
                loadTopClients(from, to)
            ]);
        } catch (e) {
            console.error('Error renderAll', e);
        }
    }

    // ==== Init ====

    document.addEventListener('DOMContentLoaded', function () {
        const hasLine = document.getElementById('lineMensajes');
        const hasPie = document.getElementById('chartPaises');

        // Si no es la página de analíticas generales, no hacer nada
        if (!hasLine && !hasPie) {
            return;
        }

        const initialFrom = cfg.from || '';
        const initialTo = cfg.to || '';

        // Daterangepicker
        setupDateRange(initialFrom, initialTo);

        // Primer render
        if (initialFrom && initialTo) {
            renderAll(initialFrom, initialTo);
        }
    });
})();
