// State
var count = 0;
var totalExpected = 0;
var startTime = null;
var levelColorMap = {};
var knownLevels = new Set();
var levelCounts = {};
var allLogLines = [];
var pendingRows = [];
var drawScheduled = false;

// Hybrid mode state. C# decides whether we're in 'client' or 'server' mode based on the result
// set's row count and the user's threshold setting. The setMode message arrives early; we use it
// to build DataTables with the right config and to gate behavior of features that differ across
// modes (level-count tallies, CSV export, time-range filter wiring).
var viewerMode = null;            // 'client' | 'server', set by setMode handler
var serverTotalRows = 0;
var pendingPageRequests = {};     // requestId -> DataTables ajax callback
var nextPageRequestId = 1;

// DataTables 2.x recurses internally on the full row set during draw/sort/search; that overflows
// the JS stack at very large counts. Cap displayed rows in CLIENT mode only; server mode renders
// one page at a time so no cap is needed.
var ROW_DISPLAY_CAP = 100000;
var ADD_CHUNK_SIZE = 5000;
var capReached = false;

// Default level colors (used when a level first appears with no user override)
var defaultLevelColors = {
    'Catastrophic': '#ffb3b3',
    'Critical':     '#ffcccc',
    'Error':        '#ffe1e1',
    'Warning':      '#fff4cc',
    'Info':         '',
    'Verbose':      '#f1f1f1',
    'Debug':        '#eef3ff'
};

var COLUMN_NAMES = ["Index","Time","Provider","TaskName","Message","Source","Level","More"];

function defaultColorFor(level) {
    if (defaultLevelColors.hasOwnProperty(level)) return defaultLevelColors[level];
    return '';
}

function updateResultsDisplay() {
    var resultsSpan = document.getElementById("results");
    var totalSpan = document.getElementById("totalResults");
    var rpmSpan = document.getElementById("resultsPerMinute");
    if (resultsSpan) resultsSpan.textContent = count;
    if (totalSpan) totalSpan.textContent = totalExpected || count;
    if (startTime) {
        var elapsed = (Date.now() - startTime) / 1000;
        var rpm = elapsed > 0 ? (count / elapsed) * 60 : 0;
        rpmSpan.textContent = `(${rpm.toFixed(0)}/min)`;
    } else {
        rpmSpan.textContent = "";
    }
}

// More modal — full LogLine details
function showMoreModal(rowIdx) {
    var logLine = allLogLines[rowIdx];
    if (!logLine) return;
    var modal = document.getElementById('moreModal');
    var contentDiv = document.getElementById('moreModalContent');
    contentDiv.innerHTML = '';
    var btn = document.createElement('button');
    btn.className = 'close toolbar-btn';
    btn.textContent = 'Close';
    btn.onclick = hideMoreModal;
    contentDiv.appendChild(btn);
    var h = document.createElement('h2'); h.textContent = 'LogLine details'; contentDiv.appendChild(h);
    var copy = document.createElement('button');
    copy.className = 'toolbar-btn'; copy.textContent = 'Copy as JSON';
    copy.onclick = function() { navigator.clipboard.writeText(JSON.stringify(logLine, null, 2)); };
    contentDiv.appendChild(copy);
    var table = document.createElement('table');
    Object.keys(logLine).forEach(function(key) {
        var tr = document.createElement('tr');
        var k = document.createElement('td'); k.className = 'k'; k.textContent = key;
        var v = document.createElement('td'); v.textContent = logLine[key];
        tr.appendChild(k); tr.appendChild(v); table.appendChild(tr);
    });
    contentDiv.appendChild(table);
    modal.style.display = 'block';
}
function hideMoreModal() { document.getElementById('moreModal').style.display = 'none'; }

// Level color picker controls
function addLevelColorControl(level) {
    if (document.getElementById('colorpicker-' + level)) return;
    if (!levelColorMap.hasOwnProperty(level)) levelColorMap[level] = defaultColorFor(level);
    var controlsDiv = document.getElementById('levelColorControls');
    var wrapper = document.createElement('span');
    wrapper.className = 'level-control-wrapper';
    var input = document.createElement('input');
    input.type = 'color';
    input.id = 'colorpicker-' + level;
    input.value = levelColorMap[level] || '#ffffff';
    input.title = 'Background color for ' + level;
    input.addEventListener('input', function() {
        levelColorMap[level] = input.value;
        recolorRowsForLevel(level);
    });
    var label = document.createElement('span'); label.textContent = level;
    var countSpan = document.createElement('span');
    countSpan.className = 'level-count';
    countSpan.id = 'level-count-' + level;
    countSpan.textContent = '(' + (levelCounts[level] || 0) + ')';
    wrapper.appendChild(input); wrapper.appendChild(label); wrapper.appendChild(countSpan);
    controlsDiv.appendChild(wrapper);
    // Also append to the Level filter dropdown
    var sel = document.querySelector('select[data-col="6"]');
    if (sel && !Array.from(sel.options).some(function(o) { return o.value === level; })) {
        var opt = document.createElement('option');
        opt.value = level; opt.textContent = level;
        sel.appendChild(opt);
    }
}

function updateLevelCounts() {
    if (!window.table) return;
    var newCounts = {};
    var dataArr = window.table.rows({search:'applied'}).data();
    for (var i = 0; i < dataArr.length; i++) {
        var lvl = dataArr[i].Level;
        newCounts[lvl] = (newCounts[lvl] || 0) + 1;
    }
    Object.keys(newCounts).forEach(function(level) {
        if (levelCounts[level] !== newCounts[level]) {
            var c = document.getElementById('level-count-' + level);
            if (c) c.textContent = '(' + newCounts[level] + ')';
        }
    });
    levelCounts = newCounts;
}

function recolorRowsForLevel(level) {
    if (window.table) window.table.rows().invalidate().draw(false);
}

// Time-range filter
$.fn.dataTable.ext.search.push(function(settings, data, dataIndex) {
    if (settings.nTable.id !== 'myTable') return true;
    var fromEl = document.getElementById('timeFrom');
    var toEl = document.getElementById('timeTo');
    var from = fromEl && fromEl.value ? new Date(fromEl.value) : null;
    var to   = toEl && toEl.value   ? new Date(toEl.value)   : null;
    if (!from && !to) return true;
    var rowTime = window.table && window.table.row(dataIndex) ? window.table.row(dataIndex).data().Time : null;
    if (!rowTime) return true;
    var t = new Date(rowTime);
    if (isNaN(t.getTime())) return true;
    if (from && t < from) return false;
    if (to && t > to) return false;
    return true;
});

// Web message handler
window.chrome.webview.addEventListener('message', function (event) {
    var payload = event.data;
    var verb = payload.verb;

    if (verb === 'setTheme') {
        applyTheme(payload.data);
        return;
    }
    if (verb === 'setLevelColors') {
        levelColorMap = Object.assign({}, levelColorMap, payload.data || {});
        Object.keys(levelColorMap).forEach(function(level) {
            knownLevels.add(level);
            addLevelColorControl(level);
        });
        if (window.table) window.table.rows().invalidate().draw(false);
        return;
    }
    if (verb === 'setMode') {
        // C# has decided client vs server. Construct DataTables with the matching config now,
        // *then* wire up the event listeners that depend on window.table.
        viewerMode = payload.data.mode || 'client';
        if (viewerMode === 'server') {
            serverTotalRows = payload.data.total || 0;
            (payload.data.levels || []).forEach(function(lvl) {
                knownLevels.add(lvl);
                addLevelColorControl(lvl);
            });
            initDataTableServerMode();
        } else {
            initDataTableClientMode();
        }
        attachTableListeners();
        return;
    }
    if (verb === 'pageResult') {
        // DataTables ajax response for server-side mode.
        var cb = pendingPageRequests[payload.id];
        if (cb) {
            cb(payload.data);
            delete pendingPageRequests[payload.id];
        }
        // Also push any new levels we haven't seen yet, then update level counts via a side call.
        try {
            (payload.data && payload.data.data || []).forEach(function(r) {
                if (r.Level && !knownLevels.has(r.Level)) {
                    knownLevels.add(r.Level);
                    addLevelColorControl(r.Level);
                }
            });
        } catch (e) {}
        requestServerLevelCounts();
        return;
    }
    if (verb === 'levelCountsResult') {
        var counts = (payload.data && payload.data.counts) || {};
        Object.keys(counts).forEach(function(lvl) {
            knownLevels.add(lvl);
            addLevelColorControl(lvl);
        });
        applyServerLevelCounts(counts);
        return;
    }
    if (verb === 'total') {
        // Client-mode loader text.
        totalExpected = payload.data.total || 0;
        updateResultsDisplay();
        var loader = document.getElementById('loader');
        if (loader) loader.textContent = 'Loading 0 / ' + totalExpected + ' rows…';
        return;
    }
    if (verb === 'newresult') {
        // Backwards-compat single-row path (client mode)
        ingestRow(payload.data);
        scheduleDraw();
        return;
    }
    if (verb === 'newresults') {
        // Batched path (client mode)
        var batch = payload.data || [];
        for (var i = 0; i < batch.length; i++) ingestRow(batch[i]);
        scheduleDraw();
        return;
    }
    if (verb === 'done') {
        // Client-mode bootstrap complete.
        flushPending();
        document.getElementById('tablediv').style.display = 'block';
        document.getElementById('loader').style.display = 'none';
        window.table.draw(false);
        updateLevelCounts();
        return;
    }
});

function ingestRow(row) {
    if (count === 0) startTime = Date.now();
    if (allLogLines.length >= ROW_DISPLAY_CAP) {
        if (!capReached) {
            capReached = true;
            showCapBanner();
        }
        return;  // drop further rows; banner already informs the user
    }
    count++;
    allLogLines.push(row);
    pendingRows.push(row);
    var lvl = row.Level;
    if (lvl && !knownLevels.has(lvl)) {
        knownLevels.add(lvl);
        addLevelColorControl(lvl);
    }
}

function showCapBanner() {
    var existing = document.getElementById('capBanner');
    if (existing) return;
    var banner = document.createElement('div');
    banner.id = 'capBanner';
    banner.style.cssText =
        'background:#fff4cc;border:1px solid #d8a800;color:#5a3e00;' +
        'padding:6px 10px;margin:0 0 8px 0;border-radius:4px;font-size:13px;';
    banner.textContent = 'Showing first ' + ROW_DISPLAY_CAP.toLocaleString() +
        ' rows. The web viewer caps display for performance — switch to View Results → Switch Viewer → Native for the full set.';
    var toolbar = document.getElementById('toolbar');
    if (toolbar && toolbar.parentNode) {
        toolbar.parentNode.insertBefore(banner, toolbar.nextSibling);
    } else {
        document.body.insertBefore(banner, document.body.firstChild);
    }
}

function scheduleDraw() {
    if (drawScheduled) return;
    drawScheduled = true;
    setTimeout(function() {
        flushPending();
        drawScheduled = false;
    }, 50);
}

function flushPending() {
    if (!pendingRows.length || !window.table) return;
    // Add in chunks; one giant rows.add() on hundreds of thousands of rows triggers DataTables
    // internal recursion and blows the JS stack.
    while (pendingRows.length > 0) {
        var slice = pendingRows.splice(0, ADD_CHUNK_SIZE);
        window.table.rows.add(slice);
    }
    updateResultsDisplay();
    var loader = document.getElementById('loader');
    if (loader && totalExpected > 0) loader.textContent = 'Loading ' + count + ' / ' + totalExpected + ' rows…';
}

function applyTheme(data) {
    document.body.style.backgroundColor = data.background;
    document.body.style.color = data.foreground;
    document.documentElement.style.setProperty('--bg', data.background);
    document.documentElement.style.setProperty('--fg', data.foreground);
    var htmlNode = document.getElementById('htmlRoot');
    var bgLower = (data.background || '').toLowerCase();
    var isLight = bgLower === '#ffffff' || bgLower === '#fff';
    htmlNode.setAttribute('data-bs-theme', isLight ? 'light' : 'dark');
}

// DataTables init — entry point. Doesn't construct the table any more; that happens once the
// setMode message arrives from C# (since the server-side config differs from client-side).
function codeAddress() {
    document.getElementById("tablediv").style.display = "none";

    // Ask C# to pick a mode and either start streaming rows (client) or just announce the row
    // count + levels (server). The setMode handler builds DataTables + wires listeners.
    window.chrome.webview.postMessage('getData');

    // Keyboard shortcuts + outside-click for Columns panel can be wired right away (don't
    // require the table to exist yet).
    var searchBox = document.getElementById('searchbox');
    document.addEventListener('keydown', function(e) {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'f') {
            e.preventDefault();
            searchBox.focus();
            searchBox.select();
        }
        if (e.key === 'Escape') {
            hideMoreModal();
            hideHelp();
            document.getElementById('columnTogglePanel').style.display = 'none';
            if (document.activeElement) document.activeElement.blur();
        }
    });
    document.addEventListener('click', function(e) {
        var panel = document.getElementById('columnTogglePanel');
        if (panel.style.display === 'block' && !panel.contains(e.target) && !e.target.matches('button.toolbar-btn')) {
            panel.style.display = 'none';
        }
    });
}

// Shared column definitions (identical in both modes).
function buildColumnDefs() {
    return [
        { title: "Index", data: "Index" },
        { title: "Time", data: "Time" },
        { title: "Provider", data: "Provider" },
        { title: "TaskName", data: "TaskName" },
        { title: "Message", data: "Message" },
        { title: "Source", data: "Source", render: function(data, type, row) {
            if (type === 'display' && data) return '<span title="' + data + '">' + escapeHtml(getBasename(data)) + '</span>';
            return data;
        }},
        { title: "Level", data: "Level" },
        { title: "More", data: null, orderable: false, searchable: false, render: function(data, type, row, meta) {
            return "<button class='toolbar-btn' onclick='showMoreModal(" + meta.row + ")'>[…]</button>";
        }}
    ];
}

function sharedDataTableConfig() {
    return {
        columns: buildColumnDefs(),
        colReorder: true,
        columnDefs: [
            { targets: 0, className: 'min-width-message-id' },
            { targets: 1, className: 'min-width-message-time' }
        ],
        deferRender: true,
        stateSave: true,
        stateDuration: -1, // sessionStorage
        pageLength: 100,
        autoWidth: false,
        initComplete: function() {
            // Move the filter row from <tfoot> (where we keep it to dodge DataTables' auto
            // column-title injection in <thead>) into <thead> for visual placement.
            var filterRow = document.querySelector('#myTable tfoot .filter-row');
            var thead = document.querySelector('#myTable thead');
            if (filterRow && thead) {
                thead.appendChild(filterRow);
                filterRow.querySelectorAll('input, select').forEach(function(el) {
                    el.addEventListener('click', function(e) { e.stopPropagation(); });
                });
            }
            enableColumnResize();
            restoreColumnWidths();
        },
        rowCallback: function(row, data) {
            var color = (levelColorMap[data.Level] !== undefined) ? levelColorMap[data.Level] : defaultColorFor(data.Level);
            row.style.backgroundColor = color || '';
        }
    };
}

function initDataTableClientMode() {
    var cfg = sharedDataTableConfig();
    cfg.layout = {
        topStart: { pageLength: { menu: [25, 50, 100, 500, 1000, 5000, { value: -1, label: 'All' }] } }
    };
    window.table = new DataTable('#myTable', cfg);
}

function initDataTableServerMode() {
    var cfg = sharedDataTableConfig();
    cfg.serverSide = true;
    cfg.processing = true;
    // Debounce text-search input. Without this, every keystroke fires a SQL round-trip and
    // the UI feels like it's chasing the user. 300 ms is short enough to feel responsive,
    // long enough that "errror" fires only once.
    cfg.searchDelay = 300;
    // Intentionally NOT setting `deferLoading` — we want DataTables to fire its initial ajax
    // immediately so the user sees the first page on entry. deferLoading would suppress that.
    cfg.layout = {
        // 'All' makes no sense in server mode — host refuses length=-1 to avoid materializing
        // millions of rows in a single page. Cap at 5000.
        topStart: { pageLength: { menu: [25, 50, 100, 500, 1000, 5000] } }
    };
    cfg.ajax = function(data, callback, settings) {
        // Pass DataTables' request envelope (start/length/draw/search/columns/order) plus our
        // custom time-range fields via the postMessage bridge. C# replies with verb=pageResult
        // and our handler invokes `callback` with the DataTables-shaped response.
        var requestId = nextPageRequestId++;
        pendingPageRequests[requestId] = function(resp) {
            // resp = { draw, recordsTotal, recordsFiltered, data }
            callback(resp);
            updateResultsDisplay();
        };
        // Inject time-range so C# can apply it to FilterSpec.
        var tf = document.getElementById('timeFrom');
        var tt = document.getElementById('timeTo');
        var msg = Object.assign({}, data, {
            timeFrom: tf && tf.value ? tf.value : null,
            timeTo:   tt && tt.value ? tt.value : null
        });
        window.chrome.webview.postMessage({ verb: 'getPage', id: requestId, data: msg });
    };
    window.table = new DataTable('#myTable', cfg);

    // Show the table immediately — server mode doesn't have a 'done' message to gate it.
    document.getElementById('tablediv').style.display = 'block';
    var loader = document.getElementById('loader');
    if (loader) loader.style.display = 'none';

    // Populate totals + RPM display.
    count = serverTotalRows;
    totalExpected = serverTotalRows;
    updateResultsDisplay();
}

// One place to wire up everything that depends on window.table existing. Called by the setMode
// handler after the table has been constructed in the appropriate mode.
function attachTableListeners() {
    buildColumnTogglePanel();

    var searchBox = document.getElementById('searchbox');
    searchBox.addEventListener('input', function() {
        window.table.search(this.value).draw();
        // In server mode the redraw triggers a fresh ajax with the new search.value; the
        // pageResult handler then re-runs updateLevelCounts via requestServerLevelCounts.
        if (viewerMode === 'client') updateLevelCounts();
    });

    document.querySelectorAll('#myTable .filter-row input').forEach(function(input) {
        input.addEventListener('input', function() {
            var col = parseInt(this.getAttribute('data-col'), 10);
            window.table.column(col).search(this.value).draw();
            if (viewerMode === 'client') updateLevelCounts();
        });
    });
    document.querySelectorAll('#myTable .filter-row select').forEach(function(sel) {
        sel.addEventListener('change', function() {
            var col = parseInt(this.getAttribute('data-col'), 10);
            var v = this.value;
            // Client mode: exact match via regex anchors. Server mode: send raw value (the host
            // strips ^$ if DataTables wraps it). Either way one path.
            window.table.column(col).search(v ? '^' + escapeRegex(v) + '$' : '', true, false).draw();
            if (viewerMode === 'client') updateLevelCounts();
        });
    });

    // Time range: client uses ext.search push (already registered); server uses ajax.data hook
    // which reads timeFrom/timeTo on every request. So we just trigger a redraw on change.
    ['timeFrom','timeTo'].forEach(function(id) {
        var el = document.getElementById(id);
        if (el) el.addEventListener('change', function() {
            window.table.draw();
            if (viewerMode === 'client') updateLevelCounts();
        });
    });
}

// ----- Server-side level counts -----
// DataTables in serverSide mode doesn't know the per-level distribution (it only sees the
// visible page), so after every ajax response we ask the host for fresh counts using the same
// filter state.
var levelCountsRequestPending = false;
function requestServerLevelCounts() {
    if (viewerMode !== 'server' || !window.table) return;
    if (levelCountsRequestPending) return; // coalesce: one in-flight request at a time
    levelCountsRequestPending = true;

    // Mirror the current DataTables ajax envelope so the host applies the same filters.
    var s = window.table.search();
    var cols = [];
    window.table.columns().every(function(idx) {
        cols.push({ search: { value: this.search() } });
    });
    var tf = document.getElementById('timeFrom');
    var tt = document.getElementById('timeTo');
    var requestId = nextPageRequestId++;
    pendingPageRequests[requestId] = function() { /* unused — answer comes via levelCountsResult */ };
    window.chrome.webview.postMessage({
        verb: 'getLevelCounts',
        id: requestId,
        data: {
            search: { value: s },
            columns: cols,
            timeFrom: tf && tf.value ? tf.value : null,
            timeTo:   tt && tt.value ? tt.value : null
        }
    });
}

function applyServerLevelCounts(counts) {
    levelCountsRequestPending = false;
    levelCounts = counts || {};
    Object.keys(levelCounts).forEach(function(lvl) {
        var el = document.getElementById('level-count-' + lvl);
        if (el) el.textContent = '(' + levelCounts[lvl] + ')';
    });
    // Zero out any chips whose level has no matches under current filters.
    document.querySelectorAll('[id^="level-count-"]').forEach(function(el) {
        var lvl = el.id.replace('level-count-', '');
        if (!(lvl in levelCounts)) el.textContent = '(0)';
    });
}

function buildColumnTogglePanel() {
    var panel = document.getElementById('columnTogglePanel');
    panel.innerHTML = '';
    COLUMN_NAMES.forEach(function(name, idx) {
        var label = document.createElement('label');
        var cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.checked = window.table.column(idx).visible();
        cb.addEventListener('change', function() {
            window.table.column(idx).visible(cb.checked);
        });
        label.appendChild(cb);
        label.appendChild(document.createTextNode(' ' + name));
        panel.appendChild(label);
    });
}

function toggleColumnPanel() {
    var panel = document.getElementById('columnTogglePanel');
    if (panel.style.display === 'block') {
        panel.style.display = 'none';
        return;
    }
    // Re-sync checkboxes with current state (in case state was restored)
    var labels = panel.querySelectorAll('label input');
    labels.forEach(function(cb, idx) {
        cb.checked = window.table.column(idx).visible();
    });
    panel.style.display = 'block';
}

function clearAllFilters() {
    document.getElementById('searchbox').value = '';
    document.querySelectorAll('#myTable .filter-row input').forEach(function(i) { i.value = ''; });
    document.querySelectorAll('#myTable .filter-row select').forEach(function(s) { s.value = ''; });
    var tf = document.getElementById('timeFrom'); if (tf) tf.value = '';
    var tt = document.getElementById('timeTo');   if (tt) tt.value = '';
    if (window.table) {
        window.table.search('');
        window.table.columns().every(function() { this.search(''); });
        window.table.draw();
    }
    updateLevelCounts();
}

function exportCsv() {
    if (!window.table) return;
    if (viewerMode === 'server') {
        // DataTables in serverSide mode only has the current page in memory, so iterating its
        // rows would dump at most pageLength rows. Streaming the full set requires a separate
        // host-side export path which we haven't wired up yet — point users at the native
        // viewer (which already has streaming CSV export via IPagedLogSource.WalkAllFiltered).
        alert(
            'CSV export is not available in server-side mode (result set above the threshold).\n\n' +
            'Switch to the native viewer (View Results → Switch Viewer → Native) to export the full set,\n' +
            'or lower the threshold in Settings → Results viewer if you want client-side rendering.'
        );
        return;
    }
    var visibleColumns = [];
    window.table.columns().every(function(idx) {
        if (this.visible() && COLUMN_NAMES[idx] && COLUMN_NAMES[idx] !== 'More') visibleColumns.push(idx);
    });
    var headers = visibleColumns.map(function(i) { return COLUMN_NAMES[i]; });
    var rows = [headers.map(csvEscape).join(',')];
    var data = window.table.rows({search: 'applied'}).data();
    for (var i = 0; i < data.length; i++) {
        var row = data[i];
        rows.push(visibleColumns.map(function(idx) {
            return csvEscape(row[COLUMN_NAMES[idx]]);
        }).join(','));
    }
    var blob = new Blob([rows.join('\n')], { type: 'text/csv;charset=utf-8' });
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'results-' + new Date().toISOString().replace(/[:.]/g,'-') + '.csv';
    document.body.appendChild(a); a.click(); document.body.removeChild(a);
}

function csvEscape(v) {
    if (v === null || v === undefined) return '';
    var s = String(v);
    if (/[",\n]/.test(s)) return '"' + s.replace(/"/g, '""') + '"';
    return s;
}

function escapeHtml(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function(c) {
        return ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[c];
    });
}

function escapeRegex(s) { return String(s).replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

function getBasename(p) {
    if (!p) return '';
    var s = String(p);
    var i = Math.max(s.lastIndexOf('/'), s.lastIndexOf('\\'));
    return i >= 0 ? s.substring(i + 1) : s;
}

// Help modal
function showHelp() { document.getElementById('helpModal').style.display = 'block'; }
function hideHelp() { document.getElementById('helpModal').style.display = 'none'; }

// --- Column resize ---
var COLUMN_WIDTHS_KEY = 'findneedle.colWidths';

// Per-column minimum widths in pixels. Index aligns with COLUMN_NAMES.
// Numeric/Level columns can be small; Time and Message need room.
var COLUMN_MIN_WIDTHS = {
    0: 50,   // Index
    1: 150,  // Time (ISO datetime "2026-03-01T09:20:06.0000000")
    2: 80,   // Provider
    3: 90,   // TaskName
    4: 200,  // Message
    5: 80,   // Source (filename)
    6: 80,   // Level
    7: 50    // More (button)
};

function minWidthFor(idx) {
    return COLUMN_MIN_WIDTHS[idx] != null ? COLUMN_MIN_WIDTHS[idx] : 40;
}

function loadStoredWidths() {
    try {
        var raw = sessionStorage.getItem(COLUMN_WIDTHS_KEY);
        return raw ? JSON.parse(raw) : {};
    } catch (e) { return {}; }
}
function saveStoredWidths(map) {
    try { sessionStorage.setItem(COLUMN_WIDTHS_KEY, JSON.stringify(map)); } catch (e) {}
}

function setColumnWidth(idx, px) {
    px = Math.max(minWidthFor(idx), Math.round(px));
    var th = document.querySelector('#myTable thead tr:first-child th[data-dt-column="' + idx + '"]');
    if (th) th.style.width = px + 'px';
    var col = document.querySelector('#myTable colgroup col[data-dt-column="' + idx + '"]');
    if (col) col.style.width = px + 'px';
    return px;
}

function restoreColumnWidths() {
    var stored = loadStoredWidths();
    Object.keys(stored).forEach(function(k) { setColumnWidth(parseInt(k, 10), stored[k]); });
}

function enableColumnResize() {
    var ths = document.querySelectorAll('#myTable thead tr:first-child th');
    ths.forEach(function(th) {
        if (th.querySelector('.col-resize-grip')) return; // idempotent
        var idxAttr = th.getAttribute('data-dt-column');
        if (idxAttr === null) return;
        var idx = parseInt(idxAttr, 10);
        var grip = document.createElement('div');
        grip.className = 'col-resize-grip';
        grip.title = 'Drag to resize';
        th.appendChild(grip);

        grip.addEventListener('mousedown', function(e) {
            e.preventDefault();
            e.stopPropagation();
            var startX = e.pageX;
            var startW = th.getBoundingClientRect().width;
            document.body.classList.add('col-resizing');

            function onMove(ev) {
                var newW = setColumnWidth(idx, startW + (ev.pageX - startX));
                window.__resizeNewW = newW;
            }
            function onUp() {
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
                document.body.classList.remove('col-resizing');
                if (typeof window.__resizeNewW === 'number') {
                    var stored = loadStoredWidths();
                    stored[idx] = window.__resizeNewW;
                    saveStoredWidths(stored);
                    delete window.__resizeNewW;
                }
            }
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
        });
        // Prevent the grip from triggering header sort.
        grip.addEventListener('click', function(e) { e.stopPropagation(); });
    });
}
