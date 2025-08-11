var count = 0;
var startTime = null;
var scrollYEnabled = false;
var defaultScrollY = null;
var levelColorMap = {};
var knownLevels = new Set();
var levelCounts = {};
var helpVisible = false;
var allLogLines = [];

function updateResultsDisplay() {
    var resultsSpan = document.getElementById("results");
    var rpmSpan = document.getElementById("resultsPerMinute");
    resultsSpan.textContent = count;
    if (startTime) {
        var elapsedSeconds = (Date.now() - startTime) / 1000;
        var rpm = elapsedSeconds > 0 ? (count / elapsedSeconds) * 60 : 0;
        rpmSpan.textContent = `(${rpm.toFixed(1)}/min)`;
    } else {
        rpmSpan.textContent = "";
    }
}

function showMoreModal(rowIdx) {
    var logLine = allLogLines[rowIdx];
    var modal = document.getElementById('moreModal');
    var modalContent = document.getElementById('moreModalContent');
    modalContent.innerHTML = '<button onclick="hideMoreModal()">Close</button><h2>LogLine Details</h2>';
    var table = document.createElement('table');
    table.style.width = '100%';
    table.style.borderCollapse = 'collapse';
    Object.keys(logLine).forEach(function(key) {
        var tr = document.createElement('tr');
        var tdKey = document.createElement('td');
        tdKey.textContent = key;
        tdKey.style.fontWeight = 'bold';
        tdKey.style.border = '1px solid #ddd';
        tdKey.style.padding = '4px 8px';
        var tdVal = document.createElement('td');
        tdVal.textContent = logLine[key];
        tdVal.style.border = '1px solid #ddd';
        tdVal.style.padding = '4px 8px';
        tr.appendChild(tdKey);
        tr.appendChild(tdVal);
        table.appendChild(tr);
    });
    modalContent.appendChild(table);
    modal.style.display = 'block';
}
function hideMoreModal() {
    document.getElementById('moreModal').style.display = 'none';
}

function addLevelColorControl(level) {
    if (document.getElementById('colorpicker-' + level)) return;
    var controlsDiv = document.getElementById('levelColorControls');
    var wrapper = document.createElement('span');
    wrapper.className = 'level-control-wrapper';
    var label = document.createElement('label');
    label.textContent = ` ${level} `;
    var input = document.createElement('input');
    input.type = 'color';
    input.id = 'colorpicker-' + level;
    input.value = levelColorMap[level] || '#ffffff';
    input.addEventListener('input', function() {
        levelColorMap[level] = input.value;
        recolorRowsForLevel(level);
    });
    label.appendChild(input);
    wrapper.appendChild(label);
    // Add count display
    var countSpan = document.createElement('span');
    countSpan.className = 'level-count';
    countSpan.id = 'level-count-' + level;
    countSpan.textContent = `(${levelCounts[level] || 0})`;
    countSpan.title = 'Click to expand';
    countSpan.style.cursor = 'pointer';
    countSpan.addEventListener('click', function() {
        toggleLevelDetails(level);
    });
    wrapper.appendChild(countSpan);
    // Add expandable details
    var detailsDiv = document.createElement('div');
    detailsDiv.className = 'level-details';
    detailsDiv.id = 'level-details-' + level;
    detailsDiv.style.display = 'none';
    detailsDiv.innerHTML = `<small>Log messages with level <b>${level}</b>: <span id='level-details-count-${level}'>${levelCounts[level] || 0}</span></small>`;
    wrapper.appendChild(detailsDiv);
    controlsDiv.appendChild(wrapper);
}

function toggleLevelDetails(level) {
    var detailsDiv = document.getElementById('level-details-' + level);
    if (detailsDiv) {
        if (detailsDiv.style.display === 'none') {
            detailsDiv.style.display = 'block';
        } else {
            detailsDiv.style.display = 'none';
        }
    }
}

function updateLevelCounts() {
    if (!window.table) return;
    var newCounts = {};
    // Use DataTables API to get all visible (filtered) rows' data efficiently
    var dataArr = window.table.rows({search:'applied'}).data();
    for (var i = 0; i < dataArr.length; i++) {
        var level = dataArr[i].Level;
        if (!newCounts[level]) newCounts[level] = 0;
        newCounts[level]++;
    }
    // Only update DOM if value changed
    Object.keys(newCounts).forEach(function(level) {
        if (levelCounts[level] !== newCounts[level]) {
            var countSpan = document.getElementById('level-count-' + level);
            if (countSpan) countSpan.textContent = `(${newCounts[level]})`;
            var detailsCount = document.getElementById('level-details-count-' + level);
            if (detailsCount) detailsCount.textContent = newCounts[level];
        }
    });
    levelCounts = newCounts;
}

function recolorRowsForLevel(level) {
    // Instead of iterating and setting color, just redraw the table
    if (window.table) {
        window.table.rows().invalidate().draw(false);
    }
}

window.chrome.webview.addEventListener('message', function (event) {
    let table = window.table;
    const payload = event.data;

    if (payload.verb === 'setTheme') {
        document.body.style.backgroundColor = payload.data.background;
        document.body.style.color = payload.data.foreground;
        var htmlNode = document.getElementById('htmlRoot');
        if (payload.data.background && payload.data.background.toLowerCase() !== '#ffffff' && payload.data.background.toLowerCase() !== '#fff') {
            htmlNode.setAttribute('data-bs-theme', 'dark');
        } else {
            htmlNode.setAttribute('data-bs-theme', 'light');
        }
        // Set help modal content colors
        var helpModalContent = document.getElementById('helpModalContent');
        if (helpModalContent) {
            helpModalContent.style.backgroundColor = payload.data.background;
            helpModalContent.style.color = payload.data.foreground;
        }
        // Set loader colors
        var loader = document.getElementById('loader');
        if (loader) {
            loader.style.backgroundColor = payload.data.background;
            loader.style.color = payload.data.foreground;
        }
        // Set DataTables dropdown and UI colors
        setTimeout(function() {
            var dtLength = document.querySelector('.dataTables_length');
            if (dtLength) {
                dtLength.style.backgroundColor = payload.data.background;
                dtLength.style.color = payload.data.foreground;
                var selects = dtLength.getElementsByTagName('select');
                for (var i = 0; i < selects.length; i++) {
                    selects[i].style.backgroundColor = payload.data.background;
                    selects[i].style.color = payload.data.foreground;
                }
            }
        }, 0);
    }

    if (payload.verb === 'setLevelColors') {
        levelColorMap = payload.data || {};
        Object.keys(levelColorMap).forEach(function(level) {
            knownLevels.add(level);
            addLevelColorControl(level);
        });
        if (window.table) {
            window.table.rows().every(function() {
                var data = this.data();
                var level = data.Level;
                var color = levelColorMap[level] || '';
                $(this.node()).css('background-color', color);
            });
        }
    }

    if (payload.verb === 'newresult') {
        if (count == 0) {
            startTime = Date.now();
        }
        count++;
        updateResultsDisplay();
        var rowIdx = table.row.add(payload.data).index();
        allLogLines[rowIdx] = payload.data;
        var level = payload.data.Level;
        if (!knownLevels.has(level)) {
            knownLevels.add(level);
            addLevelColorControl(level);
        }
        var color = levelColorMap[level] || '';
        setTimeout(function() {
            var rowNode = table.row(rowIdx).node();
            if (rowNode) {
                rowNode.style.backgroundColor = color;
            }
        }, 0);
    }
    if (payload.verb === 'done') {
        document.getElementById("tablediv").style.visibility = "visible";
        document.getElementById("tablediv").style.display = "block";
        document.getElementById("loader").style.display = "none";
        table.draw(false);
        updateLevelCounts();
    }
});

function codeAddress() {
    document.getElementById("tablediv").style.display = "none";
    var scrollY = null;
    window.table = new DataTable('#myTable', {
        columns: [
            { title: "Index", data: "Index" },
            { title: "Time", data: "Time" },
            { title: "Provider", data: "Provider" },
            { title: "TaskName", data: "TaskName" },
            { title: "Message", data: "Message" },
            { title: "Source", data: "Source" },
            { title: "Level", data: "Level" },
            { title: "More", data: null, orderable: false, render: function(data, type, row, meta) {
                return `<button onclick='showMoreModal(${meta.row})'>[...]</button>`;
            }}
        ],
        colReorder: true,
        columnDefs: [
            { targets: 0, className: 'min-width-message-id' },
            { targets: 1, className: 'min-width-message-time' },
        ],
        lengthChange: true,
        pageLength: 500,
        scrollY: scrollY,
        scrollCollapse: true,
        layout: {
            topStart: {
                pageLength: {
                    menu: [5, 10, 25, 50,100,500,1000,2000,5000,10000]
                }
            }
        },
        rowCallback: function(row, data) {
            var color = levelColorMap[data.Level] || '';
            row.style.backgroundColor = color;
        }
    });
    window.chrome.webview.postMessage('getData');
    document.querySelectorAll('a.toggle-vis').forEach((el) => {
        el.addEventListener('click', function (e) {
            e.preventDefault();
            let columnIdx = e.target.getAttribute('data-column');
            let column = window.table.column(columnIdx);
            column.visible(!column.visible());
        });
    });
    // Searchbox filter
    var searchBox = document.getElementById('searchbox');
    if (searchBox) {
        searchBox.addEventListener('input', function() {
            window.table.search(this.value).draw();
        });
    }
}

// Help modal logic
function showHelp() {
    document.getElementById('helpModal').style.display = 'block';
}
function hideHelp() {
    document.getElementById('helpModal').style.display = 'none';
}
