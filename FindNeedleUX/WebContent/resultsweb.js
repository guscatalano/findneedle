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

// DataTables 2.x recurses internally on the full row set during draw/sort/search; that overflows
// the JS stack at very large counts. Cap displayed rows for the web viewer; recommend the native
// viewer (WinUI DataGrid + virtualization) when a log exceeds this.
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
    if (verb === 'total') {
        totalExpected = payload.data.total || 0;
        updateResultsDisplay();
        var loader = document.getElementById('loader');
        if (loader) loader.textContent = 'Loading 0 / ' + totalExpected + ' rows…';
        return;
    }
    if (verb === 'newresult') {
        // Backwards-compat single-row path
        ingestRow(payload.data);
        scheduleDraw();
        return;
    }
    if (verb === 'newresults') {
        // Batched path
        var batch = payload.data || [];
        for (var i = 0; i < batch.length; i++) ingestRow(batch[i]);
        scheduleDraw();
        return;
    }
    if (verb === 'done') {
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

// DataTables init
function codeAddress() {
    document.getElementById("tablediv").style.display = "none";
    window.table = new DataTable('#myTable', {
        columns: [
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
        ],
        colReorder: true,
        columnDefs: [
            { targets: 0, className: 'min-width-message-id' },
            { targets: 1, className: 'min-width-message-time' }
        ],
        deferRender: true,
        stateSave: true,
        stateDuration: -1, // sessionStorage
        pageLength: 100,
        autoWidth: false, // we manage column widths manually for drag-resize
        initComplete: function() {
            // Relocate the filter row from <tfoot> into <thead>. We keep it in <tfoot>
            // in markup so DataTables doesn't auto-inject column-title spans into our
            // <th> cells (which it does for every row inside <thead>).
            var filterRow = document.querySelector('#myTable tfoot .filter-row');
            var thead = document.querySelector('#myTable thead');
            if (filterRow && thead) {
                thead.appendChild(filterRow);
                // Block clicks inside filter inputs from bubbling up and triggering
                // DataTables' sort handler on the parent <th>.
                filterRow.querySelectorAll('input, select').forEach(function(el) {
                    el.addEventListener('click', function(e) { e.stopPropagation(); });
                });
            }
            enableColumnResize();
            restoreColumnWidths();
        },
        layout: {
            topStart: { pageLength: { menu: [25, 50, 100, 500, 1000, 5000, { value: -1, label: 'All' }] } }
        },
        rowCallback: function(row, data) {
            var color = (levelColorMap[data.Level] !== undefined) ? levelColorMap[data.Level] : defaultColorFor(data.Level);
            row.style.backgroundColor = color || '';
        }
    });
    window.chrome.webview.postMessage('getData');

    // Build Columns ▾ panel
    buildColumnTogglePanel();

    // Top searchbox
    var searchBox = document.getElementById('searchbox');
    searchBox.addEventListener('input', function() {
        window.table.search(this.value).draw();
        updateLevelCounts();
    });

    // Per-column filters
    document.querySelectorAll('#myTable .filter-row input').forEach(function(input) {
        input.addEventListener('input', function() {
            var col = parseInt(this.getAttribute('data-col'), 10);
            window.table.column(col).search(this.value).draw();
            updateLevelCounts();
        });
    });
    document.querySelectorAll('#myTable .filter-row select').forEach(function(sel) {
        sel.addEventListener('change', function() {
            var col = parseInt(this.getAttribute('data-col'), 10);
            var v = this.value;
            // Use exact match for dropdown filter
            window.table.column(col).search(v ? '^' + escapeRegex(v) + '$' : '', true, false).draw();
            updateLevelCounts();
        });
    });

    // Time range — redraw on change to apply ext.search filter
    ['timeFrom','timeTo'].forEach(function(id) {
        var el = document.getElementById(id);
        if (el) el.addEventListener('change', function() { window.table.draw(); updateLevelCounts(); });
    });

    // Keyboard shortcuts
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

    // Click outside Columns panel closes it
    document.addEventListener('click', function(e) {
        var panel = document.getElementById('columnTogglePanel');
        if (panel.style.display === 'block' && !panel.contains(e.target) && !e.target.matches('button.toolbar-btn')) {
            panel.style.display = 'none';
        }
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
