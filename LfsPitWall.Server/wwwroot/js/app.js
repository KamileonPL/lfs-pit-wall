/* ===========================================================
   LFS Pit Wall – Frontend Application
   SignalR real-time timing dashboard
   =========================================================== */

// ── State ──────────────────────────────────────────────────

let lastRenderState = null;

// ── SignalR Loading & Connection ───────────────────────────

function loadSignalRScript() {
    return new Promise((resolve, reject) => {
        if (typeof signalR !== 'undefined') {
            resolve(); // Already loaded
            return;
        }

        const script = document.createElement('script');
        script.src = 'https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js';
        script.async = true;
        script.onerror = () => reject(new Error('Failed to load SignalR'));
        script.onload = () => {
            if (typeof signalR !== 'undefined') {
                resolve();
            } else {
                reject(new Error('SignalR loaded but not available'));
            }
        };
        document.head.appendChild(script);
    });
}

function waitForSignalR(callback, maxAttempts = 300) {
    let attempts = 0;
    updateConnectionStatus(false, "Loading SignalR...");

    const checkInterval = setInterval(() => {
        attempts++;
        const timeElapsed = (attempts * 100) / 1000;

        if (typeof signalR !== 'undefined') {
            clearInterval(checkInterval);
            debugLog("SignalR loaded successfully", 'info');
            callback();
        } else if (attempts % 10 === 0) {
            debugLog(`Waiting for SignalR... (${timeElapsed.toFixed(1)}s)`, 'warn');

            if (attempts >= maxAttempts) {
                clearInterval(checkInterval);
                debugLog(`Failed to load SignalR after ${timeElapsed.toFixed(1)}s`, 'error');
                updateConnectionStatus(false, "SignalR Load Failed");
            }
        }
    }, 100);
}

function initializeConnection() {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/timing")
        .withAutomaticReconnect([1000, 3000, 5000, 10000, 30000])
        .build();

    connection.onreconnecting(() => {
        debugLog("Reconnecting...", 'warn');
        updateConnectionStatus(false, "Reconnecting...");
    });

    connection.onreconnected(() => {
        debugLog("Reconnected successfully", 'info');
        updateConnectionStatus(true, "Connected");
    });

    connection.onclose(error => {
        debugLog(`Connection closed: ${error?.message || 'Unknown'}`, 'error');
        updateConnectionStatus(false, "Disconnected");
    });

    connection.on("ReceiveSessionUpdate", (data) => {
        updateSessionInfo(data);
        updateDriversTable(data);
        updateBestLaps(data);
    });

    connection.start()
        .then(() => {
            debugLog("Connected to SignalR Hub", 'info');
            updateConnectionStatus(true, "Connected");
        })
        .catch(error => {
            debugLog(`Connection failed: ${error.message}`, 'error');
            updateConnectionStatus(false, `Error: ${error.message || 'Connection Failed'}`);
            setTimeout(() => attemptReconnect(connection), 3000);
        });

    window.signalRConnection = connection;
}

function attemptReconnect(connection) {
    if (!connection || connection.state === signalR.HubConnectionState.Connected) return;

    debugLog("Attempting reconnect...", 'warn');
    connection.start()
        .then(() => debugLog("Reconnected", 'info'))
        .catch(error => {
            debugLog(`Reconnect failed: ${error.message}`, 'error');
            setTimeout(() => attemptReconnect(connection), 5000);
        });
}

// ── Connection Status UI ──────────────────────────────────

function updateConnectionStatus(connected, text) {
    const el = document.getElementById("connection-status");
    if (!el) return; // DOM not ready yet
    
    if (connected) {
        el.innerHTML = `
            <div class="inline-block px-4 py-2 rounded bg-green-900 text-green-200">
                <span class="inline-block w-2 h-2 bg-green-500 rounded-full mr-2 pulse-live"></span>
                ${text}
            </div>`;
    } else {
        el.innerHTML = `
            <div class="inline-block px-4 py-2 rounded bg-red-900 text-red-200">
                <span class="inline-block w-2 h-2 bg-red-500 rounded-full mr-2"></span>
                ${text}
            </div>`;
    }
}

// ── Time Formatting ───────────────────────────────────────

function formatTime(ms, isGap = false) {
    if (!ms || ms === 0) return "-";

    const isNegative = ms < 0;
    const absMs = Math.abs(ms);

    const minutes = Math.floor(absMs / 60000);
    const seconds = Math.floor((absMs % 60000) / 1000);
    const milliseconds = absMs % 1000;

    const formatted = `${minutes}:${String(seconds).padStart(2, "0")}.${String(milliseconds).padStart(3, "0")}`;

    if (isGap) {
        return isNegative ? `-${formatted}` : `+${formatted}`;
    }
    return formatted;
}

function getTimeClass(currentTime, bestSessionTime, bestPersonalTime) {
    if (!currentTime || currentTime === 0) return "current-time";
    if (bestSessionTime && currentTime === bestSessionTime) return "session-best";
    if (bestPersonalTime && currentTime === bestPersonalTime && currentTime !== bestSessionTime) return "personal-best";
    return "current-time";
}

function getLapTimeClass(driverLapTime, sessionBestTime, driverPersonalBestTime) {
    if (!driverLapTime) return "current-time";
    if (sessionBestTime && driverLapTime === sessionBestTime) return "session-best";
    if (driverPersonalBestTime && driverLapTime === driverPersonalBestTime && driverLapTime !== sessionBestTime) return "personal-best";
    return "current-time";
}

// ── Session Info Update ───────────────────────────────────

function updateSessionInfo(data) {
    document.getElementById("track-name").textContent = data.trackName || "Unknown";
    document.getElementById("session-type").textContent = data.sessionType || "-";
    document.getElementById("weather-type").textContent = data.weatherType || "-";
    document.getElementById("race-status").textContent = data.raceInProgress ? "🏁 LIVE" : "Idle";

    const maxLaps = Math.max(0, ...data.players.map(p => p.lapsCompleted || 0));
    const displayMaxLaps = data.maxRaceLaps || maxLaps;
    document.getElementById("max-laps").textContent = `${maxLaps}/${displayMaxLaps} Laps`;

    const hours = Math.floor(data.sessionTimeMs / 3600000);
    const minutes = Math.floor((data.sessionTimeMs % 3600000) / 60000);
    const seconds = Math.floor((data.sessionTimeMs % 60000) / 1000);
    document.getElementById("session-duration").textContent =
        `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

// ── Drivers Table Update ──────────────────────────────────

function updateDriversTable(data) {
    const tableBody = document.getElementById("drivers-table");

    if (!data.players || data.players.length === 0) {
        tableBody.innerHTML = `
            <tr class="driver-row">
                <td colspan="10" class="px-4 py-8 text-center text-gray-500">
                    Waiting for drivers...
                </td>
            </tr>`;
        return;
    }

    const renderKey = data.players.map(p => p.playerId).join(',');
    lastRenderState = renderKey;

    let html = "";
    data.players.forEach((driver, index) => {
        const position = index + 1;

        const gap = position === 1 ? "-" :
            (data.players[index - 1]?.lastElapsedTimeMs && driver.lastElapsedTimeMs
                ? formatTime(driver.lastElapsedTimeMs - data.players[index - 1].lastElapsedTimeMs, true)
                : "-");

        const lapTimeClass = getLapTimeClass(
            driver.personalBestLapMs,
            data.sessionBestLapMs,
            driver.personalBestLapMs
        );

        const sectorTimeClass = (sectorNum) => {
            const time = driver.currentSectorProgress ? driver.currentSectorProgress[sectorNum] : 0;
            const bestSession = data.sessionBestSectors ? data.sessionBestSectors[sectorNum] : 0;
            const bestPersonal = driver.personalBestSectors ? driver.personalBestSectors[sectorNum] : 0;
            return getTimeClass(time, bestSession, bestPersonal);
        };

        const positionBadgeClass = position <= 3
            ? `position-${position}`
            : "";

        const driverColor = driver.driverColor || "#9CA3AF";
        const driverNameStyle = `style="background-color: ${driverColor}15; border-left: 3px solid ${driverColor}; color: #F5F5F5;"`;

        html += `
            <tr class="driver-row">
                <td class="px-4 py-3">
                    <div class="position-badge ${positionBadgeClass}">${position}</div>
                </td>
                <td class="px-4 py-3 font-semibold driver-name" ${driverNameStyle} data-driver-id="${driver.playerId}"></td>
                <td class="px-4 py-3 text-sm text-gray-400">${driver.carName}</td>
                <td class="px-4 py-3">${driver.lapsCompleted}</td>
                <td class="px-4 py-3 font-mono text-sm">
                    <span class="${lapTimeClass} px-2 py-1 rounded">${formatTime(driver.personalBestLapMs)}</span>
                </td>
                <td class="px-4 py-3 font-mono text-sm">${formatTime(driver.currentLapTimeMs)}</td>
                <td class="px-4 py-3 text-xs">
                    <span class="sector-time ${sectorTimeClass(1)}">S1: ${formatTime((driver.currentSectorProgress && driver.currentSectorProgress[1]) || 0)}</span><br>
                    <span class="sector-time ${sectorTimeClass(2)}">S2: ${formatTime((driver.currentSectorProgress && driver.currentSectorProgress[2]) || 0)}</span><br>
                    <span class="sector-time ${sectorTimeClass(3)}">S3: ${formatTime((driver.currentSectorProgress && driver.currentSectorProgress[3]) || 0)}</span>
                </td>
                <td class="px-4 py-3 text-sm gap-indicator">${gap}</td>
                <td class="px-4 py-3">
                    ${driver.fuelPercent !== null && driver.fuelPercent !== undefined ? `
                    <div class="relative w-12 h-2 bg-gray-700 rounded-full overflow-hidden">
                        <div class="absolute h-full bg-gradient-to-r from-green-500 to-yellow-500 rounded-full"
                             style="width: ${driver.fuelPercent}%"></div>
                    </div>
                    <span class="text-xs text-gray-400">${driver.fuelPercent}%</span>
                    ` : '<span class="text-xs text-gray-500 italic">N/A</span>'}
                </td>
                <td class="px-4 py-3 text-center">${driver.pitStops}</td>
            </tr>`;
    });

    tableBody.innerHTML = html;

    // Apply colored driver names via innerHTML (safe: generated server-side with HtmlEncode)
    data.players.forEach((driver) => {
        const nameCell = tableBody.querySelector(`[data-driver-id="${driver.playerId}"]`);
        if (nameCell) {
            nameCell.innerHTML = driver.nameHtml || driver.name;
        }
    });
}

// ── Best Laps Update ──────────────────────────────────────

function updateBestLaps(data) {
    document.getElementById("session-best-lap-time").textContent = formatTime(data.sessionBestLapMs) || "-";

    const infoDiv = document.getElementById("session-best-lap-info");
    if (data.sessionBestLapAuthorName && data.sessionBestLapNumber != null) {
        const author = data.sessionBestLapAuthorName;
        const username = data.sessionBestLapAuthorUsername
            ? ` <span style="color:#AAAAAA">(${data.sessionBestLapAuthorUsername})</span>`
            : "";
        infoDiv.innerHTML = `<p style="color: #c0c0c0;">${author}${username}<br/><span style="font-size: 0.8rem; color: #888;">Lap ${data.sessionBestLapNumber}</span></p>`;
    } else {
        infoDiv.innerHTML = `<p style="color: #888;">-</p>`;
    }

    if (data.sessionBestSectors) {
        document.getElementById("best-s1").textContent = formatTime(data.sessionBestSectors[1]) || "-";
        document.getElementById("best-s2").textContent = formatTime(data.sessionBestSectors[2]) || "-";
        document.getElementById("best-s3").textContent = formatTime(data.sessionBestSectors[3]) || "-";
    }
}

// ── Debug Console ─────────────────────────────────────────

function debugLog(message, type = 'info') {
    const consoleEl = document.getElementById("debug-console");
    if (!consoleEl) return; // DOM not ready yet
    
    const timestamp = new Date().toLocaleTimeString();
    const prefix = type === 'error' ? '❌' : type === 'warn' ? '⚠️' : 'ℹ️';

    const line = document.createElement('div');
    line.textContent = `[${timestamp}] ${prefix} ${message}`;
    line.className = type === 'error' ? 'text-red-400' : type === 'warn' ? 'text-yellow-400' : 'text-green-400';
    consoleEl.appendChild(line);
    consoleEl.scrollTop = consoleEl.scrollHeight;

    // Keep only last 50 lines
    const lines = consoleEl.querySelectorAll('div');
    if (lines.length > 50) {
        lines[0].remove();
    }
}

function clearDebugLog() {
    document.getElementById("debug-console").innerHTML = '';
}

// ── Console Override ──────────────────────────────────────

const originalLog = console.log;
const originalError = console.error;
const originalWarn = console.warn;

console.log = function (...args) {
    debugLog(args.join(' '), 'info');
    originalLog.apply(console, args);
};

console.error = function (...args) {
    debugLog(args.join(' '), 'error');
    originalError.apply(console, args);
};

console.warn = function (...args) {
    debugLog(args.join(' '), 'warn');
    originalWarn.apply(console, args);
};

// ── Initialize ────────────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
    loadSignalRScript()
        .then(() => {
            debugLog("SignalR library loaded", 'info');
            initializeConnection();
        })
        .catch(error => {
            debugLog(`Failed to load SignalR: ${error.message}`, 'error');
            updateConnectionStatus(false, "Failed to load SignalR");
        });
});
