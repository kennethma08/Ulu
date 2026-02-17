(() => {
    window.showError = window.showError || function (msg) {
        console.error('[ERROR]', msg);
        if (window.Swal) {
            Swal.fire({ icon: 'error', title: 'Error', text: String(msg), confirmButtonText: 'Aceptar' });
        } else {
            alert('Error: ' + msg);
        }
    };
    window.showSuccess = window.showSuccess || function (msg) {
        console.log('[OK]', msg);
        if (window.Swal) {
            Swal.fire({ icon: 'success', title: 'Correcto', text: String(msg), timer: 1800, showConfirmButton: false });
        } else {
            alert('OK: ' + msg);
        }
    };
    window.showInfo = window.showInfo || function (msg) {
        console.log('[INFO]', msg);
        if (window.Swal) {
            Swal.fire({ icon: 'info', title: 'Información', text: String(msg), confirmButtonText: 'Aceptar' });
        } else {
            alert('Info: ' + msg);
        }
    };
    window.showToast = window.showToast || function (msg, type) {
        console.log('[TOAST]', type || 'info', msg);
        if (window.Swal) {
            Swal.fire({
                toast: true, position: 'top-end', icon: type || 'info', title: String(msg),
                showConfirmButton: false, timer: 2000, timerProgressBar: true
            });
        }
    };

    const showError = window.showError;
    const showSuccess = window.showSuccess;
    const showInfo = window.showInfo;
    const showToast = window.showToast;

    document.addEventListener('DOMContentLoaded', () => {
        const root = document.getElementById('chat-root');
        const updateNameUrl = (root && root.dataset && root.dataset.updateNameUrl) ? root.dataset.updateNameUrl : '/Contact/ActualizarNombre';

        const usersEl = document.getElementById('users');
        const usersMobEl = document.getElementById('users-mob');
        const messagesEl = document.getElementById('messages');
        const searchBox = document.getElementById('searchBox');
        const statusFilter = document.getElementById('statusFilter');
        const searchBoxMob = document.getElementById('searchBoxMob');
        const statusFilterMob = document.getElementById('statusFilterMob');
        const badgeEl = document.getElementById('statusBadge');
        const toggleBtn = document.getElementById('toggleStatusBtn');
        const sendBtn = document.getElementById('send-btn');
        const inputEl = document.getElementById('message-input');
        const convNumberEl = document.getElementById('convNumber');
        const chatNameEl = document.getElementById('chat-contact-name');
        const chatBtnEdit = document.getElementById('chat-btnEditName');
        const chatBtnSave = document.getElementById('chat-btnSaveName');
        const antifgTokenEl = document.querySelector('#chat-antiforgery input[name="__RequestVerificationToken"]');
        const audioBtn = document.getElementById('btn-audio');
        const audioInput = document.getElementById('audio-input');
        const recIndicator = document.getElementById('recIndicator');

        const agentSelect = document.getElementById('agentSelect');
        const takeBtn = document.getElementById('takeBtn');
        const releaseBtn = document.getElementById('releaseBtn');
        const assignedInfo = document.getElementById('assignedInfo');

        const NAME_MAX = 20;

        let me = { userId: 0, profileId: 0, isAdmin: false };
        let agents = [];

        let conversations = [];
        let selectedConversation = null;
        let messages = [];
        const contactNamesById = Object.create(null);

        let isEditingHeaderName = false;
        let currentHeaderName = '';

        let mediaRecorder = null;
        let recordedChunks = [];
        let recordingStream = null;
        let isRecording = false;
        let recordingMime = null;

        let plyrPlayers = [];

        const CR_TZ = 'America/Costa_Rica';

        function asDateUTC(ts) {
            if (ts == null) return new Date(NaN);
            if (typeof ts === 'number') return new Date(ts);
            const s = String(ts);
            if (/Z|[+-]\d{2}:\d{2}$/.test(s)) return new Date(s);
            return new Date(s + 'Z');
        }

        const fmtDateTimeCR = new Intl.DateTimeFormat('es-CR', { timeZone: CR_TZ, dateStyle: 'short', timeStyle: 'short' });
        const fmtTimeCR = new Intl.DateTimeFormat('es-CR', { timeZone: CR_TZ, hour: '2-digit', minute: '2-digit' });
        const fmtDateCR = new Intl.DateTimeFormat('es-CR', { timeZone: CR_TZ, year: 'numeric', month: '2-digit', day: '2-digit' });

        const fmt = (ts) => {
            const d = asDateUTC(ts);
            return isNaN(d) ? '' : fmtDateTimeCR.format(d);
        };
        const fmtTime = (ts) => {
            const d = asDateUTC(ts);
            return isNaN(d) ? '' : fmtTimeCR.format(d);
        };
        const fmtDateOnly = (ts) => {
            const d = asDateUTC(ts);
            return isNaN(d) ? '' : fmtDateCR.format(d);
        };
        const toMillisUTC = (ts) => {
            const d = asDateUTC(ts);
            return isNaN(d) ? 0 : d.getTime();
        };

        const esc = (s) =>
            String(s || '')
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;');

        const normalizeSender = (s) => {
            const v = (s || 'contact').toLowerCase();
            return (v === 'agent' || v === 'user' || v === 'me' || v === 'admin') ? 'me' : 'contact';
        };

        function applyStatusUI(status) {
            const s = (status || 'open').toLowerCase();
            badgeEl.textContent = s.toUpperCase();
            badgeEl.className = 'badge ' + (s === 'open' ? 'badge-open' : 'badge-closed');

            const isClosed = (s !== 'open');

            inputEl.disabled = isClosed;
            sendBtn.disabled = isClosed;
            inputEl.placeholder = isClosed ? 'Conversación cerrada' : 'Escribe un mensaje...';

            if (audioBtn) audioBtn.disabled = isClosed;
            if (audioInput) audioInput.disabled = isClosed;

            if (isClosed && isRecording) {
                stopRecording();
            }

            toggleBtn.disabled = (!selectedConversation) || isClosed;
            toggleBtn.textContent = isClosed ? 'Cerrada' : 'Cerrar';

            updateAssignUI();
        }

        function updateActiveConvLink() {
            if (usersEl) usersEl.querySelectorAll('.list-group-item').forEach(a => a.classList.remove('active'));
            if (usersMobEl) usersMobEl.querySelectorAll('.list-group-item').forEach(a => a.classList.remove('active'));

            if (!selectedConversation) return;

            const sel = String(selectedConversation.id);
            const el1 = usersEl ? usersEl.querySelector(`.list-group-item[data-conv-id="${sel}"]`) : null;
            const el2 = usersMobEl ? usersMobEl.querySelector(`.list-group-item[data-conv-id="${sel}"]`) : null;

            if (el1) el1.classList.add('active');
            if (el2) el2.classList.add('active');
        }

        function getFilterStatus() {
            const st = (statusFilter?.value || 'all');
            const q = (searchBox?.value || '').trim().toLowerCase();
            return { st, q };
        }

        function setFiltersFromMob() {
            if (searchBox && searchBoxMob) searchBox.value = searchBoxMob.value;
            if (statusFilter && statusFilterMob) statusFilter.value = statusFilterMob.value;
        }

        function syncMobFromDesktop() {
            if (searchBox && searchBoxMob) searchBoxMob.value = searchBox.value;
            if (statusFilter && statusFilterMob) statusFilterMob.value = statusFilter.value;
        }

        function conversationsHTML(arr) {
            return arr.map(c => {
                const isClosed = (String(c.status || 'open').toLowerCase() !== 'open');
                const lastAt = c.lastActivityAt ? fmt(c.lastActivityAt) : (c.startedAt ? fmt(c.startedAt) : '');
                const localName = (c.contactId ? contactNamesById[c.contactId] : '') || c.contactName || '';
                const displayName = (localName || c.contactPhone || ('Contacto ' + (c.contactId || ''))).trim();

                const assigned = c.assignedUserName ? `Asignada: ${esc(c.assignedUserName)}` : 'Sin asignar';
                const statusPill = isClosed
                    ? `<span class="badge badge-closed ms-1">CLOSED</span>`
                    : `<span class="badge badge-open ms-1">OPEN</span>`;

                return `
        <a href="#"
           class="list-group-item list-group-item-action d-flex align-items-start${isClosed ? ' closed' : ''}"
           data-conv-id="${c.id}">
            <img src="https://static.vecteezy.com/system/resources/previews/002/318/271/original/user-profile-icon-free-vector.jpg"
                 class="rounded-circle me-2"
                 width="40"
                 height="40"
                 alt="">
            <div class="flex-grow-1 min-w-0">
                <div class="d-flex align-items-center justify-content-between gap-2">
                    <div class="fw-bold text-truncate-1">${esc(displayName)}</div>
                    <div class="d-flex align-items-center flex-shrink-0">
                        <span class="badge conv-pill">#${c.id}</span>
                        ${statusPill}
                    </div>
                </div>
                <div class="small text-muted">${lastAt}</div>
                <div class="small">${esc(c.contactPhone || '')}</div>
                <div class="small text-muted">${assigned}</div>
            </div>
        </a>`;
            }).join('');
        }

        function renderConversations(list) {
            const { st, q } = getFilterStatus();

            const src = (list || []).filter(c => {
                const statusOk = (st === 'all') ? true : ((c.status || 'open').toLowerCase() === st);

                const qLower = q;
                const idStr = String(c.id);
                const phoneStr = (c.contactPhone || '').toLowerCase();
                const stStr = (c.status || '').toLowerCase();
                const asgStr = (c.assignedUserName || '').toLowerCase();
                const nmStr = ((c.contactId ? contactNamesById[c.contactId] : '') || c.contactName || '').toLowerCase();

                const match = !qLower ? true : (
                    idStr.includes(qLower) ||
                    phoneStr.includes(qLower) ||
                    stStr.includes(qLower) ||
                    asgStr.includes(qLower) ||
                    nmStr.includes(qLower)
                );

                return statusOk && match;
            });

            if (!src.length) {
                const emptyHtml = '<div class="p-3 text-muted">No hay conversaciones.</div>';
                if (usersEl) usersEl.innerHTML = emptyHtml;
                if (usersMobEl) usersMobEl.innerHTML = emptyHtml;
                return;
            }

            const html = conversationsHTML(src);
            if (usersEl) usersEl.innerHTML = html;
            if (usersMobEl) usersMobEl.innerHTML = html;

            updateActiveConvLink();
        }

        function setHeaderNameFromConv(conv) {
            const localName = contactNamesById[conv.contactId] || conv.contactName || '';
            const display = (localName || conv.contactPhone || ('Contacto ' + (conv.contactId || ''))).trim();
            currentHeaderName = display;
            chatNameEl.textContent = display;
        }

        function updateAssignUI() {
            if (!agentSelect || !takeBtn || !releaseBtn) return;

            if (!selectedConversation) {
                agentSelect.disabled = true;
                takeBtn.disabled = true;
                releaseBtn.disabled = true;
                if (assignedInfo) assignedInfo.textContent = '';
                return;
            }

            const asgId = selectedConversation.assignedUserId || null;
            const asgName = selectedConversation.assignedUserName || '';

            if (assignedInfo) {
                assignedInfo.textContent = asgId ? `Asignada a: ${asgName || ('Usuario ' + asgId)}` : 'Sin asignar';
            }

            agentSelect.disabled = !(me && me.isAdmin);

            takeBtn.disabled = !!asgId || !me.userId;

            const canRelease = !!asgId && (me.isAdmin || String(asgId) === String(me.userId));
            releaseBtn.disabled = !canRelease;

            if (me && me.isAdmin) {
                if (asgId) {
                    agentSelect.value = String(asgId);
                } else {
                    agentSelect.value = '';
                }
            }
        }

        function selectConversation(conv) {
            selectedConversation = conv;

            setHeaderNameFromConv(conv);

            const sub = document.getElementById('chat-contact-sub');
            if (sub) sub.textContent = `Conversación #${conv.id}`;

            if (convNumberEl) {
                if (conv?.id != null) {
                    convNumberEl.textContent = `#${conv.id}`;
                    convNumberEl.style.display = '';
                } else {
                    convNumberEl.style.display = 'none';
                }
            }

            applyStatusUI(conv.status);
            updateActiveConvLink();
            loadMessages(conv.id);

            try {
                const el = document.getElementById('convOffcanvas');
                if (window.bootstrap && el) {
                    const oc = bootstrap.Offcanvas.getInstance(el);
                    if (oc) oc.hide();
                }
            } catch { }

            exitEditHeaderName(false);
            updateAssignUI();
        }

        function selectConversationById(id) {
            const c = conversations.find(x => String(x.id) === String(id));
            if (c) selectConversation(c);
        }

        async function loadMe() {
            try {
                const res = await fetch('/Chat/Me');
                const data = await res.json();
                me = data || me;
            } catch { }
        }

        async function loadAgents() {
            try {
                const res = await fetch('/Chat/GetAgents');
                const data = await res.json();
                agents = (data && data.agents) ? data.agents : [];

                if (!agentSelect) return;
                agentSelect.innerHTML = '<option value="">Agentes...</option>';

                agents.forEach(a => {
                    const opt = document.createElement('option');
                    opt.value = String(a.id);
                    opt.textContent = a.name || ('User ' + a.id);
                    agentSelect.appendChild(opt);
                });

                updateAssignUI();
            } catch (e) {
                console.error(e);
            }
        }

        async function loadAllConversations() {
            try {
                const res = await fetch('/Chat/GetAllConversations');
                const data = await res.json();

                if (data.error) {
                    const errHtml = `<div class="p-3 text-danger">Error: ${data.error}</div>`;
                    if (usersEl) usersEl.innerHTML = errHtml;
                    if (usersMobEl) usersMobEl.innerHTML = errHtml;
                    return;
                }

                conversations = (data.conversations || []);

                conversations.sort((a, b) => {
                    const ta = toMillisUTC(a.lastActivityAt || a.startedAt);
                    const tb = toMillisUTC(b.lastActivityAt || b.startedAt);
                    return tb - ta;
                });

                renderConversations(conversations);

                if (selectedConversation) {
                    const keepId = selectedConversation.id;
                    const updated = conversations.find(x => String(x.id) === String(keepId));
                    if (updated) {
                        selectedConversation = updated;
                        updateAssignUI();
                        applyStatusUI(updated.status);
                    }
                }

                if (!selectedConversation && conversations.length > 0) {
                    selectConversation(conversations[0]);
                }
            } catch (e) {
                const errHtml = '<div class="p-3 text-danger">Error cargando conversaciones</div>';
                if (usersEl) usersEl.innerHTML = errHtml;
                if (usersMobEl) usersMobEl.innerHTML = errHtml;
                console.error(e);
            }
        }

        async function loadMessages(conversationId) {
            messagesEl.innerHTML = '<div class="text-muted">Cargando mensajes...</div>';
            destroyPlyrPlayers();

            try {
                const res = await fetch(`/Chat/GetConversationMessages?conversationId=${encodeURIComponent(conversationId)}`);
                const data = await res.json();

                if (data.error) {
                    messagesEl.innerHTML = `<div class="text-danger">Error: ${data.error}</div>`;
                    return;
                }

                messages = data.messages || [];
                renderMessages();
            } catch (e) {
                messagesEl.innerHTML = '<div class="text-danger">No se pudieron cargar los mensajes</div>';
                console.error(e);
            }
        }

        function renderMessages() {
            messagesEl.innerHTML = '';

            if (!messages.length) {
                messagesEl.innerHTML = '<div class="text-muted">Sin mensajes aún.</div>';
                return;
            }

            messages.sort((a, b) => toMillisUTC(a.sentAt) - toMillisUTC(b.sentAt));

            let lastDateKey = '';
            messages.forEach(m => {
                const dateKey = fmtDateOnly(m.sentAt);
                if (dateKey && dateKey !== lastDateKey) {
                    const sep = document.createElement('div');
                    sep.className = 'date-sep';
                    sep.innerHTML = `<span>${esc(dateKey)}</span>`;
                    messagesEl.appendChild(sep);
                    lastDateKey = dateKey;
                }

                const who = normalizeSender(m.sender);
                const wrap = document.createElement('div');
                wrap.className = 'msg ' + (who === 'me' ? 'me' : 'contact');

                const hasAttachments = Array.isArray(m.attachments) && m.attachments.length > 0;

                if (m.type === 'audio' && hasAttachments) {
                    const att = m.attachments[0];
                    const src = '/Chat/Attachment?id=' + encodeURIComponent(att.id);
                    const timeText = fmtTime(m.sentAt);
                    const timeClass = (who === 'me' ? 'time' : 'time-dark');

                    const audioContainer = document.createElement('div');
                    audioContainer.className = 'audio-container';

                    audioContainer.innerHTML = `
        <audio class="js-plyr-audio" controls preload="metadata">
            <source src="${src}" type="${esc(att.mimeType || 'audio/mp4')}">
            Tu navegador no soporta la reproducción de audio.
        </audio>
        <div class="bubble-footer audio-footer">
            <span class="${timeClass}">${timeText}</span>
        </div>`;

                    wrap.appendChild(audioContainer);
                    messagesEl.appendChild(wrap);
                    return;
                }

                const bubble = document.createElement('div');
                bubble.className = 'bubble';

                const time = document.createElement('span');
                time.textContent = fmtTime(m.sentAt);
                time.className = (who === 'me' ? 'time' : 'time-dark');

                if (m.type && m.type !== 'text') {
                    bubble.innerHTML = `[${esc(m.type)}] ${esc(m.message || '')}`;
                    bubble.appendChild(time);
                } else {
                    bubble.textContent = m.message || '';
                    bubble.appendChild(time);
                }

                wrap.appendChild(bubble);
                messagesEl.appendChild(wrap);
            });

            messagesEl.scrollTop = messagesEl.scrollHeight;
            setupPlyrPlayers();
        }

        function destroyPlyrPlayers() {
            if (!plyrPlayers || !plyrPlayers.length) return;
            plyrPlayers.forEach(p => { try { p.destroy(); } catch { } });
            plyrPlayers = [];
        }

        function setupPlyrPlayers() {
            if (!window.Plyr) return;
            destroyPlyrPlayers();

            const audios = document.querySelectorAll('.js-plyr-audio');
            if (!audios.length) return;

            plyrPlayers = Array.from(audios).map(el => new Plyr(el, {
                controls: ['play', 'progress', 'current-time', 'mute', 'volume'],
                autoplay: false
            }));

            plyrPlayers.forEach(p => {
                p.on('play', () => {
                    plyrPlayers.forEach(other => { if (other !== p) other.pause(); });
                });
            });
        }

        function autoGrowInput() {
            if (!inputEl) return;
            inputEl.style.height = 'auto';
            const maxH = 120;
            inputEl.style.height = Math.min(inputEl.scrollHeight, maxH) + 'px';
        }

        async function sendMessage() {
            const txt = (inputEl.value || '').trim();
            if (!txt || !selectedConversation) return;

            const status = (selectedConversation.status || 'open').toLowerCase();
            if (status !== 'open') return;

            messages.push({ sender: 'agent', message: txt, type: 'text', sentAt: new Date().toISOString() });
            renderMessages();
            inputEl.value = '';
            autoGrowInput();

            const payload = {
                conversationId: selectedConversation.id,
                contactId: selectedConversation.contactId || null,
                contactPhone: selectedConversation.contactPhone || null,
                message: txt
            };

            try {
                const res = await fetch('/Chat/SendMessage', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });

                const data = await res.json();
                if (!data || data.success === false) {
                    showError(data?.error || 'No se pudo enviar el mensaje.');
                } else {
                    const newId = data.conversationId;
                    if (newId && newId !== selectedConversation.id) {
                        await loadAllConversations();
                        selectConversationById(newId);
                    } else {
                        setTimeout(() => loadMessages(selectedConversation.id), 350);
                    }
                    showToast('Mensaje enviado', 'success');
                }
            } catch (e) {
                console.error(e);
                showError('Error al enviar el mensaje.');
            }
        }

        async function toggleStatus() {
            if (!selectedConversation) return;

            const current = (selectedConversation.status || 'open').toLowerCase();
            if (current !== 'open') return;

            const payload = {
                conversationId: selectedConversation.id,
                status: 'closed',
                contactId: selectedConversation.contactId || null,
                startedAt: selectedConversation.startedAt || null
            };

            try {
                const res = await fetch('/Chat/UpdateConversationStatus', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });

                const data = await res.json();
                if (data && data.success) {
                    selectedConversation.status = 'closed';
                    const idx = conversations.findIndex(c => String(c.id) === String(selectedConversation.id));
                    if (idx >= 0) conversations[idx].status = 'closed';

                    applyStatusUI('closed');
                    renderConversations(conversations);
                    showSuccess('Conversación cerrada.');
                } else {
                    showError(data?.error || 'No se pudo actualizar el estado.');
                }
            } catch (e) {
                console.error(e);
                showError('Error al cambiar el estado.');
            }
        }

        function placeCaretEndEl(el) {
            try {
                const r = document.createRange();
                r.selectNodeContents(el);
                r.collapse(false);
                const s = window.getSelection();
                s.removeAllRanges();
                s.addRange(r);
            } catch { }
        }

        function validateHeaderNameAndToggle() {
            const v = (chatNameEl.textContent || '').trim();
            const trimmed = v.slice(0, NAME_MAX);

            if (trimmed !== chatNameEl.textContent) {
                chatNameEl.textContent = trimmed;
                placeCaretEndEl(chatNameEl);
            }

            chatBtnSave.disabled = (trimmed.length === 0 || trimmed === currentHeaderName);
        }

        function enterEditHeaderName() {
            if (!selectedConversation || isEditingHeaderName) return;

            isEditingHeaderName = true;
            chatNameEl.setAttribute('contenteditable', 'true');
            chatNameEl.focus();
            placeCaretEndEl(chatNameEl);

            chatBtnEdit.style.display = 'none';
            chatBtnSave.style.display = 'inline-flex';
            chatBtnSave.disabled = true;

            chatNameEl.oninput = validateHeaderNameAndToggle;
            chatNameEl.onkeydown = (ev) => {
                if (ev.key === 'Enter') {
                    ev.preventDefault();
                    saveHeaderContactName();
                }
                if (ev.key === 'Escape') {
                    ev.preventDefault();
                    exitEditHeaderName(false);
                }
            };
        }

        function exitEditHeaderName(saved) {
            if (!isEditingHeaderName) return;

            chatNameEl.removeAttribute('contenteditable');
            chatNameEl.oninput = null;
            chatNameEl.onkeydown = null;

            if (!saved) chatNameEl.textContent = currentHeaderName;
            else currentHeaderName = (chatNameEl.textContent || '').trim();

            chatBtnSave.style.display = 'none';
            chatBtnEdit.style.display = 'inline-flex';

            isEditingHeaderName = false;
        }

        async function saveHeaderContactName() {
            if (!selectedConversation) return;

            const id = selectedConversation.contactId || 0;
            let nombre = (chatNameEl.textContent || '').trim();

            if (id <= 0) { showError('Id de contacto inválido.'); return; }
            if (!nombre) { showInfo('Ingrese un nombre.'); return; }

            if (nombre.length > NAME_MAX) {
                nombre = nombre.slice(0, NAME_MAX);
                chatNameEl.textContent = nombre;
            }

            try {
                chatBtnSave.disabled = true;

                const form = new URLSearchParams();
                form.set('id', String(id));
                form.set('nombre', nombre);

                const token = antifgTokenEl ? antifgTokenEl.value : '';

                const res = await fetch(updateNameUrl, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                        'X-Requested-With': 'XMLHttpRequest',
                        ...(token ? { 'RequestVerificationToken': token } : {})
                    },
                    body: form.toString()
                });

                if (!res.ok) {
                    const msg = await res.text();
                    showError(`No se pudo guardar (${res.status}). ${msg || 'No se pudo actualizar el nombre.'}`);
                    chatBtnSave.disabled = false;
                    return;
                }

                contactNamesById[id] = nombre;
                chatNameEl.textContent = nombre;
                exitEditHeaderName(true);
                showSuccess('Nombre actualizado.');
                renderConversations(conversations);
            } catch (err) {
                showError(`Error: ${err?.message ?? err}`);
                chatBtnSave.disabled = false;
            }
        }

        const MAX_AUDIO_BYTES = 16 * 1024 * 1024;

        async function startRecording() {
            if (!selectedConversation) { showInfo('Seleccione una conversación.'); return; }

            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia || !window.MediaRecorder) {
                if (audioInput) audioInput.click();
                else showError('Este navegador no soporta grabación de audio.');
                return;
            }

            try {
                recordingStream = await navigator.mediaDevices.getUserMedia({ audio: true });

                const preferredMimes = [
                    'audio/mp4;codecs=mp4a.40.2',
                    'audio/mp4',
                    'audio/webm;codecs=opus',
                    'audio/webm',
                    'audio/ogg;codecs=opus',
                    'audio/ogg',
                    'audio/mpeg'
                ];

                let mimeToUse = null;
                if (typeof MediaRecorder.isTypeSupported === 'function') {
                    for (const m of preferredMimes) {
                        if (MediaRecorder.isTypeSupported(m)) { mimeToUse = m; break; }
                    }
                }

                mediaRecorder = mimeToUse ? new MediaRecorder(recordingStream, { mimeType: mimeToUse }) : new MediaRecorder(recordingStream);

                recordingMime = (mimeToUse || mediaRecorder.mimeType || 'audio/webm');
                recordedChunks = [];

                mediaRecorder.ondataavailable = (ev) => {
                    if (ev.data && ev.data.size > 0) recordedChunks.push(ev.data);
                };

                mediaRecorder.onstop = () => {
                    try {
                        if (recordedChunks.length) {
                            const rawMime = recordingMime || mediaRecorder.mimeType || 'audio/webm';
                            const cleanMime = String(rawMime).split(';')[0] || 'audio/webm';

                            let ext = '.webm';
                            if (cleanMime === 'audio/mp4') ext = '.m4a';
                            else if (cleanMime === 'audio/mpeg' || cleanMime === 'audio/mp3') ext = '.mp3';
                            else if (cleanMime === 'audio/ogg') ext = '.ogg';
                            else if (cleanMime === 'audio/aac') ext = '.aac';
                            else if (cleanMime === 'audio/amr') ext = '.amr';

                            const blob = new Blob(recordedChunks, { type: cleanMime });
                            if (blob.size > MAX_AUDIO_BYTES) {
                                showError('El audio excede 16MB. Grabe un audio más corto.');
                                return;
                            }

                            const fileName = 'audio-whatsapp' + ext;
                            const file = new File([blob], fileName, { type: cleanMime });

                            sendAudio(file);
                        }
                    } finally {
                        if (recordingStream) {
                            recordingStream.getTracks().forEach(t => t.stop());
                            recordingStream = null;
                        }
                        mediaRecorder = null;
                        recordedChunks = [];
                        recordingMime = null;
                    }
                };

                mediaRecorder.start();
                isRecording = true;

                if (recIndicator) recIndicator.classList.remove('d-none');

                audioBtn.classList.remove('btn-outline-secondary');
                audioBtn.classList.add('btn-danger');
                audioBtn.innerHTML = '<i class="bi bi-stop-fill"></i>';
            } catch (err) {
                console.error(err);
                showError('No se pudo acceder al micrófono.');
            }
        }

        function stopRecording() {
            if (mediaRecorder && isRecording) {
                mediaRecorder.stop();
            }
            isRecording = false;

            if (recIndicator) recIndicator.classList.add('d-none');

            audioBtn.classList.remove('btn-danger');
            audioBtn.classList.add('btn-outline-secondary');
            audioBtn.innerHTML = '<i class="bi bi-mic-fill"></i>';
        }

        async function sendAudio(file) {
            if (!file || !selectedConversation) return;

            const status = (selectedConversation.status || 'open').toLowerCase();
            if (status !== 'open') { showInfo('La conversación está cerrada.'); return; }

            if (file.size > MAX_AUDIO_BYTES) { showError('El audio excede 16MB. Use un audio más corto.'); return; }

            const form = new FormData();
            form.append('file', file);
            form.append('conversationId', String(selectedConversation.id));
            if (selectedConversation.contactId) form.append('contactId', String(selectedConversation.contactId));
            if (selectedConversation.contactPhone) form.append('toPhone', String(selectedConversation.contactPhone));

            try {
                const res = await fetch('/api/integraciones/whatsapp/agent/audio', { method: 'POST', body: form });
                const data = await res.json().catch(() => null);

                if (!res.ok || !data || data.success === false) {
                    showError(data?.error || 'No se pudo enviar el audio.');
                    return;
                }

                showToast('Audio enviado', 'success');
                setTimeout(() => loadMessages(selectedConversation.id), 500);
            } catch (err) {
                console.error(err);
                showError('Error al enviar el audio.');
            }
        }

        function onListClick(e) {
            const link = e.target.closest('.list-group-item[data-conv-id]');
            if (!link) return;

            e.preventDefault();
            selectConversationById(link.dataset.convId);
        }

        if (usersEl) usersEl.addEventListener('click', onListClick);
        if (usersMobEl) usersMobEl.addEventListener('click', onListClick);

        if (statusFilter) statusFilter.addEventListener('change', () => { syncMobFromDesktop(); renderConversations(conversations); });
        if (searchBox) searchBox.addEventListener('input', () => { syncMobFromDesktop(); renderConversations(conversations); });

        if (statusFilterMob) statusFilterMob.addEventListener('change', () => { setFiltersFromMob(); renderConversations(conversations); });
        if (searchBoxMob) searchBoxMob.addEventListener('input', () => { setFiltersFromMob(); renderConversations(conversations); });

        if (sendBtn) sendBtn.addEventListener('click', sendMessage);

        if (inputEl) {
            inputEl.addEventListener('input', autoGrowInput);
            inputEl.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    sendMessage();
                }
            });
        }

        if (toggleBtn) toggleBtn.addEventListener('click', toggleStatus);

        if (audioBtn) {
            audioBtn.addEventListener('click', () => {
                if (audioBtn.disabled) return;
                if (isRecording) stopRecording();
                else startRecording();
            });
        }

        if (audioInput) {
            audioInput.addEventListener('change', (e) => {
                const file = e.target.files && e.target.files[0];
                if (!file) return;
                sendAudio(file);
                audioInput.value = '';
            });
        }

        if (chatBtnEdit) chatBtnEdit.addEventListener('click', enterEditHeaderName);
        if (chatBtnSave) chatBtnSave.addEventListener('click', () => saveHeaderContactName());

        async function assignToUser(toUserId) {
            if (!selectedConversation) return;

            try {
                const res = await fetch('/Chat/AssignConversation', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ conversationId: selectedConversation.id, toUserId: Number(toUserId) })
                });
                const data = await res.json().catch(() => ({}));

                if (!res.ok || !data.ok) {
                    showError(data.error || 'No se pudo asignar.');
                    return;
                }

                showToast('Asignación OK', 'success');
                const keepId = selectedConversation.id;
                await loadAllConversations();
                selectConversationById(keepId);
            } catch (e) {
                console.error(e);
                showError('Error asignando.');
            }
        }

        async function releaseConversation() {
            if (!selectedConversation) return;

            try {
                const res = await fetch('/Chat/ReleaseConversation', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ conversationId: selectedConversation.id })
                });
                const data = await res.json().catch(() => ({}));

                if (!res.ok || !data.ok) {
                    showError(data.error || 'No se pudo soltar.');
                    return;
                }

                showToast('Soltada', 'success');
                const keepId = selectedConversation.id;
                await loadAllConversations();
                selectConversationById(keepId);
            } catch (e) {
                console.error(e);
                showError('Error soltando.');
            }
        }

        if (agentSelect) {
            agentSelect.addEventListener('change', async () => {
                if (!me.isAdmin) return;
                const v = agentSelect.value;
                if (!v) return;
                if (!selectedConversation) return;

                await assignToUser(v);
            });
        }

        if (takeBtn) {
            takeBtn.addEventListener('click', async () => {
                if (!selectedConversation) return;
                if (!me.userId) return;
                await assignToUser(me.userId);
            });
        }

        if (releaseBtn) {
            releaseBtn.addEventListener('click', async () => {
                if (!selectedConversation) return;
                await releaseConversation();
            });
        }

        (async function init() {
            await loadMe();
            await loadAgents();
            await loadAllConversations();
            autoGrowInput();
        })();
    });
})();
