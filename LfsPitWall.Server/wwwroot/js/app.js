/* ===========================================================
   LFS Pit Wall – Frontend Application
   SignalR real-time timing dashboard
   =========================================================== */

// ── State ──────────────────────────────────────────────────

let hoveredDriverId = null;
let hoveredLapHistoryDriverId = null;
let visibleLapHistoryDriverId = null;
let latestSessionData = null;
let lapHistorySessionKey = null;
let lapHistoryShowTimer = null;
let lapHistoryHideTimer = null;
let lastPointerClientX = null;
let lastPointerClientY = null;
let sessionClockTimerId = null;
let sessionClockBaseMs = 0;
let sessionClockSyncedAtMs = 0;
let sessionClockLastServerMs = 0;
let sessionClockRunning = false;
const driverLapHistoryCache = new Map();
const LAP_HISTORY_SHOW_DELAY_MS = 240;
const LAP_HISTORY_HIDE_DELAY_MS = 80;

function formatDurationClock(totalMs) {
    const safeMs = Math.max(0, Math.floor(totalMs));
    const hours = Math.floor(safeMs / 3600000);
    const minutes = Math.floor((safeMs % 3600000) / 60000);
    const seconds = Math.floor((safeMs % 60000) / 1000);

    return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

function getDisplayedSessionTimeMs() {
    if (!sessionClockRunning) {
        return sessionClockBaseMs;
    }

    return sessionClockBaseMs + Math.max(0, Date.now() - sessionClockSyncedAtMs);
}

function renderSessionDuration() {
    const durationElement = document.getElementById("session-duration");
    if (!durationElement) {
        return;
    }

    durationElement.textContent = formatDurationClock(getDisplayedSessionTimeMs());
}

function startSessionClock() {
    if (sessionClockTimerId !== null) {
        return;
    }

    sessionClockTimerId = window.setInterval(renderSessionDuration, 250);
}

function stopSessionClock() {
    if (sessionClockTimerId === null) {
        return;
    }

    window.clearInterval(sessionClockTimerId);
    sessionClockTimerId = null;
}

function syncSessionClock(data) {
    const nextServerMs = Number(data.sessionTimeMs || 0);
    const now = Date.now();
    const currentDisplayedMs = getDisplayedSessionTimeMs();
    const isClockReset = nextServerMs === 0 || nextServerMs + 1000 < sessionClockLastServerMs;

    sessionClockLastServerMs = nextServerMs;

    if (isClockReset) {
        sessionClockBaseMs = nextServerMs;
        sessionClockSyncedAtMs = now;
        sessionClockRunning = nextServerMs > 0;
    } else {
        sessionClockBaseMs = Math.max(currentDisplayedMs, nextServerMs);
        sessionClockSyncedAtMs = now;
        sessionClockRunning = true;
    }

    if (sessionClockRunning) {
        startSessionClock();
    } else {
        stopSessionClock();
    }

    renderSessionDuration();
}

function getLapHistoryTrigger(element) {
    return element?.closest?.("[data-last-lap-driver-id]") || null;
}

function clearLapHistoryTimers() {
    window.clearTimeout(lapHistoryShowTimer);
    window.clearTimeout(lapHistoryHideTimer);
    lapHistoryShowTimer = null;
    lapHistoryHideTimer = null;
}

function refreshLapHistoryTriggerStyles() {
    document.querySelectorAll("[data-last-lap-driver-id]").forEach((trigger) => {
        const driverId = String(trigger.dataset.lastLapDriverId || "");
        trigger.classList.toggle("is-hovered", driverId === hoveredLapHistoryDriverId);
        trigger.classList.toggle("is-active", driverId === visibleLapHistoryDriverId);
    });
}

function setLapHistoryHoverTarget(driverId) {
    const nextDriverId = driverId ? String(driverId) : null;
    if (hoveredLapHistoryDriverId === nextDriverId) {
        if (visibleLapHistoryDriverId === nextDriverId) {
            updateLapHistoryTooltip();
        }

        refreshLapHistoryTriggerStyles();
        return;
    }

    hoveredLapHistoryDriverId = nextDriverId;
    refreshLapHistoryTriggerStyles();
    window.clearTimeout(lapHistoryShowTimer);
    window.clearTimeout(lapHistoryHideTimer);

    if (!nextDriverId) {
        lapHistoryHideTimer = window.setTimeout(() => {
            visibleLapHistoryDriverId = null;
            refreshLapHistoryTriggerStyles();
            hideLapHistoryTooltip();
        }, LAP_HISTORY_HIDE_DELAY_MS);
        return;
    }

    if (visibleLapHistoryDriverId === nextDriverId) {
        refreshLapHistoryTriggerStyles();
        updateLapHistoryTooltip();
        return;
    }

    lapHistoryShowTimer = window.setTimeout(() => {
        if (hoveredLapHistoryDriverId !== nextDriverId) {
            return;
        }

        visibleLapHistoryDriverId = nextDriverId;
        refreshLapHistoryTriggerStyles();
        const driver = getDriverById(nextDriverId);
        if (driver) {
            ensureDriverLapHistory(driver);
        }

        updateLapHistoryTooltip();
    }, LAP_HISTORY_SHOW_DELAY_MS);
}

function syncLapHistoryHoverState() {
    if (lastPointerClientX == null || lastPointerClientY == null) {
        return;
    }

    const trigger = getLapHistoryTrigger(document.elementFromPoint(lastPointerClientX, lastPointerClientY));
    setLapHistoryHoverTarget(trigger?.dataset.lastLapDriverId || null);
}

function initializeTableHoverState() {
    const tableBody = document.getElementById("drivers-table");
    if (!tableBody || tableBody.dataset.hoverStateInitialized === "true") {
        return;
    }

    tableBody.dataset.hoverStateInitialized = "true";

    tableBody.addEventListener("mousemove", (event) => {
        lastPointerClientX = event.clientX;
        lastPointerClientY = event.clientY;

        const row = event.target.closest("tr[data-driver-id]");
        hoveredDriverId = row?.dataset.driverId || null;

        const trigger = getLapHistoryTrigger(event.target);
        setLapHistoryHoverTarget(trigger?.dataset.lastLapDriverId || null);
    });

    tableBody.addEventListener("mouseleave", () => {
        hoveredDriverId = null;
        setLapHistoryHoverTarget(null);
    });

    window.addEventListener("scroll", () => {
        if (visibleLapHistoryDriverId) {
            updateLapHistoryTooltip();
        }
    }, true);

    window.addEventListener("resize", () => {
        syncLapHistoryHoverState();
        updateLapHistoryTooltip();
    });
}

function getDriverById(playerId) {
    return latestSessionData?.players?.find(driver => String(driver.playerId) === String(playerId)) || null;
}

function syncLapHistoryCache(data) {
    const nextSessionKey = [
        data.trackName || "",
        data.sessionType || "",
        data.maxRaceLaps || 0,
        data.qualifyingMins || 0
    ].join("|");

    const isNewSession = lapHistorySessionKey !== null && (
        lapHistorySessionKey !== nextSessionKey ||
        Number(data.sessionTimeMs || 0) < Number(latestSessionData?.sessionTimeMs || 0)
    );

    if (isNewSession) {
        driverLapHistoryCache.clear();
        clearLapHistoryTimers();
        hoveredLapHistoryDriverId = null;
        visibleLapHistoryDriverId = null;
        refreshLapHistoryTriggerStyles();
        hideLapHistoryTooltip();
    }

    lapHistorySessionKey = nextSessionKey;

    const activeDriverIds = new Set((data.players || []).map(driver => String(driver.playerId)));
    for (const cachedDriverId of driverLapHistoryCache.keys()) {
        if (!activeDriverIds.has(cachedDriverId)) {
            driverLapHistoryCache.delete(cachedDriverId);
        }
    }

    for (const driver of data.players || []) {
        const driverId = String(driver.playerId);
        const cached = driverLapHistoryCache.get(driverId);
        if (cached && cached.lapCount > Number(driver.lastLapNumber || 0)) {
            driverLapHistoryCache.delete(driverId);
        }
    }
}

function getOrCreateLapHistoryTooltip() {
    let tooltip = document.getElementById("lap-history-tooltip");
    if (tooltip) {
        return tooltip;
    }

    tooltip = document.createElement("div");
    tooltip.id = "lap-history-tooltip";
    tooltip.className = "lap-history-tooltip";
    document.body.appendChild(tooltip);
    return tooltip;
}

function hideLapHistoryTooltip() {
    const tooltip = document.getElementById("lap-history-tooltip");
    if (!tooltip) {
        return;
    }

    tooltip.classList.remove("is-visible");
    tooltip.innerHTML = "";
}

function renderLapHistoryTooltip(driver) {
    const lapCount = Number(driver.lastLapNumber || 0);
    if (lapCount === 0) {
        return `
            <div class="lap-history-tooltip-title">Lap History</div>
            <div class="lap-history-tooltip-empty">No completed laps yet.</div>`;
    }

    const cached = driverLapHistoryCache.get(String(driver.playerId));
    if (!cached || cached.loading) {
        return `
            <div class="lap-history-tooltip-title">Lap History</div>
            <div class="lap-history-tooltip-empty">Loading lap history...</div>`;
    }

    if (!Array.isArray(cached.laps) || cached.laps.length === 0) {
        return `
            <div class="lap-history-tooltip-title">Lap History</div>
            <div class="lap-history-tooltip-empty">No lap history available.</div>`;
    }

    const bestLapTimeMs = Number(driver.personalBestLapMs || 0);
    const rows = cached.laps.map(lap => {
        const isPersonalBest = bestLapTimeMs > 0 && Number(lap.lapTimeMs) === bestLapTimeMs;
        const invalidBadge = lap.isValid ? "" : '<span class="lap-history-tooltip-badge">Invalid</span>';

        return `
            <div class="lap-history-tooltip-row">
                <span class="lap-history-tooltip-label">Lap ${lap.lapNumber}</span>
                <div class="lap-history-tooltip-value">
                    ${invalidBadge}
                    <span class="lap-history-tooltip-time${isPersonalBest ? ' is-personal-best' : ''}">${formatTime(lap.lapTimeMs)}</span>
                </div>
            </div>`;
    }).join("");

    return `
        <div class="lap-history-tooltip-title">Lap History</div>
        <div class="lap-history-tooltip-list">${rows}</div>`;
}

function positionLapHistoryTooltip(trigger, tooltip) {
    const rect = trigger.getBoundingClientRect();
    const margin = 12;
    const gap = 14;

    tooltip.style.left = "0px";
    tooltip.style.top = "0px";
    tooltip.style.visibility = "hidden";
    tooltip.classList.add("is-visible");

    const tooltipWidth = tooltip.offsetWidth;
    const tooltipHeight = tooltip.offsetHeight;
    let left = rect.right + gap;
    if (left + tooltipWidth > window.innerWidth - margin) {
        left = rect.left - tooltipWidth - gap;
    }
    if (left < margin) {
        left = Math.max(margin, window.innerWidth - tooltipWidth - margin);
    }

    const centeredTop = rect.top + (rect.height / 2) - (tooltipHeight / 2);
    const top = Math.max(margin, Math.min(centeredTop, window.innerHeight - tooltipHeight - margin));

    tooltip.style.left = `${left}px`;
    tooltip.style.top = `${top}px`;
    tooltip.style.visibility = "visible";
}

function updateLapHistoryTooltip() {
    const tooltip = getOrCreateLapHistoryTooltip();
    if (!visibleLapHistoryDriverId) {
        hideLapHistoryTooltip();
        return;
    }

    const driver = getDriverById(visibleLapHistoryDriverId);
    const trigger = document.querySelector(`[data-last-lap-driver-id="${visibleLapHistoryDriverId}"]`);
    if (!driver || !trigger) {
        hideLapHistoryTooltip();
        return;
    }

    tooltip.innerHTML = renderLapHistoryTooltip(driver);
    positionLapHistoryTooltip(trigger, tooltip);
}

async function ensureDriverLapHistory(driver) {
    const lapCount = Number(driver.lastLapNumber || 0);
    const driverId = String(driver.playerId);
    const cached = driverLapHistoryCache.get(driverId);

    if (lapCount === 0 || cached?.loading || cached?.lapCount === lapCount) {
        return;
    }

    driverLapHistoryCache.set(driverId, {
        lapCount,
        laps: cached?.laps || [],
        loading: true
    });
    updateLapHistoryTooltip();

    try {
        const response = await window.signalRConnection?.invoke("GetDriverLapHistory", Number(driver.playerId));
        driverLapHistoryCache.set(driverId, {
            lapCount,
            laps: Array.isArray(response?.laps) ? response.laps : [],
            loading: false
        });
    } catch (error) {
        console.warn(`Failed to load lap history for player ${driver.playerId}: ${error?.message || error}`);
        driverLapHistoryCache.set(driverId, {
            lapCount,
            laps: cached?.laps || [],
            loading: false
        });
    }

    updateLapHistoryTooltip();
}

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
        sessionClockRunning = false;
        stopSessionClock();
        renderSessionDuration();
    });

    connection.on("ReceiveSessionUpdate", (data) => {
        syncLapHistoryCache(data);
        latestSessionData = data;
        syncSessionClock(data);
        updateSessionInfo(data);
        updateDriversTable(data);
        updateBestLaps(data);
        syncLapHistoryHoverState();

        if (visibleLapHistoryDriverId) {
            const driver = getDriverById(visibleLapHistoryDriverId);
            if (driver) {
                ensureDriverLapHistory(driver);
            }
        }

        updateLapHistoryTooltip();
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
    if (bestPersonalTime && currentTime > bestPersonalTime) return "slower-than-personal";
    return "current-time";
}

function getSectorTimeClass(currentSectorTime, displayedSectorTime, bestSessionTime, bestPersonalTime) {
    if (currentSectorTime > 0) {
        if (bestSessionTime && currentSectorTime <= bestSessionTime) return "session-best";
        if (bestPersonalTime && currentSectorTime < bestPersonalTime) return "personal-best";
        if (bestPersonalTime && currentSectorTime > bestPersonalTime) return "slower-than-personal";
        if (bestPersonalTime && currentSectorTime === bestPersonalTime) return "personal-best";
        return "current-time";
    }

    return getTimeClass(displayedSectorTime, bestSessionTime, bestPersonalTime);
}

function getLapTimeClass(driverLapTime, sessionBestTime, driverPersonalBestTime) {
    if (!driverLapTime) return "current-time";
    if (sessionBestTime && driverLapTime === sessionBestTime) return "session-best";
    if (driverPersonalBestTime && driverLapTime === driverPersonalBestTime && driverLapTime !== sessionBestTime) return "personal-best";
    return "current-time";
}

function formatLapDelta(lapTimeMs, referenceLapMs, suffix = "") {
    if (!lapTimeMs || !referenceLapMs || lapTimeMs <= referenceLapMs) {
        return "";
    }

    return `+${((lapTimeMs - referenceLapMs) / 1000).toFixed(3)}s${suffix}`;
}

function formatSectorDelta(currentSectorTimeMs, personalBestSectorTimeMs) {
    if (!currentSectorTimeMs || !personalBestSectorTimeMs || currentSectorTimeMs === personalBestSectorTimeMs) {
        return "";
    }

    const deltaMs = currentSectorTimeMs - personalBestSectorTimeMs;
    const sign = deltaMs > 0 ? "+" : "-";
    return `${sign}${(Math.abs(deltaMs) / 1000).toFixed(3)}s`;
}

function getTyreCodeLabel(tyreCode) {
    switch (Number(tyreCode)) {
        case 0: return "R1";
        case 1: return "R2";
        case 2: return "R3";
        case 3: return "R4";
        case 4: return "RS";
        case 5: return "RN";
        case 6: return "HY";
        case 7: return "KN";
        default: return "-";
    }
}

function renderTyreSummary(tyreTypes) {
    if (!Array.isArray(tyreTypes) || tyreTypes.length !== 4) {
        return '<span class="text-xs text-gray-500">-</span>';
    }

    const rearLabel = getTyreCodeLabel(tyreTypes[0]);
    const frontLabel = getTyreCodeLabel(tyreTypes[2]);

    if (rearLabel === "-" && frontLabel === "-") {
        return '<span class="text-xs text-gray-500">-</span>';
    }

    if (rearLabel === frontLabel) {
        return `<span class="tyre-chip">${frontLabel}</span>`;
    }

    return `
        <div class="tyre-stack">
            <span class="tyre-chip">F ${frontLabel}</span>
            <span class="tyre-chip">R ${rearLabel}</span>
        </div>`;
}

function formatSpeedKmh(speedKmh) {
    const numericSpeed = Number(speedKmh || 0);
    if (!numericSpeed) {
        return "-";
    }

    return `${numericSpeed.toFixed(1)} km/h`;
}

function getSectorNumbers(data) {
    const activeSectorCount = Number(data.activeSectorCount || 0);
    return Array.from({ length: activeSectorCount }, (_, index) => index + 1);
}

// ── Session Info Update ───────────────────────────────────

function updateSessionInfo(data) {
    document.getElementById("track-name").textContent = data.trackName || "Unknown";
    document.getElementById("session-type").textContent = data.sessionType || "-";
    document.getElementById("weather-type").textContent = data.weatherType || "-";
    document.getElementById("wind-type").textContent = data.windType || "-";
    document.getElementById("race-status").textContent = data.raceInProgress ? "🏁 LIVE" : "Idle";
    document.getElementById("driver-count").textContent = Array.isArray(data.players) ? data.players.length : 0;

    const maxLaps = Math.max(0, ...data.players.map(p => p.lapsCompleted || 0));
    const displayMaxLaps = data.maxRaceLaps || maxLaps;
    document.getElementById("max-laps").textContent = `${maxLaps}/${displayMaxLaps} Laps`;
}

// ── Drivers Table Update ──────────────────────────────────

function updateDriversTable(data) {
    const tableBody = document.getElementById("drivers-table");
    const playerIds = new Set((data.players || []).map(player => String(player.playerId)));

    if (hoveredDriverId && !playerIds.has(hoveredDriverId)) {
        hoveredDriverId = null;
    }

    if (!data.players || data.players.length === 0) {
        tableBody.innerHTML = `
            <tr class="driver-row">
                <td colspan="11" class="px-4 py-8 text-center text-gray-500">
                    Waiting for drivers...
                </td>
            </tr>`;
        return;
    }

    let html = "";
    data.players.forEach((driver, index) => {
        const position = index + 1;
        const sectorNumbers = getSectorNumbers(data);
        const hasGapToPrevious = driver.gapToPreviousMs !== null && driver.gapToPreviousMs !== undefined;
        const isFightForPosition = hasGapToPrevious && driver.gapToPreviousMs > 0 && driver.gapToPreviousMs < 1000;

        const gap = position === 1
            ? "-"
            : (hasGapToPrevious
                ? formatTime(driver.gapToPreviousMs, true)
                : "-");

        const lapTimeClass = getLapTimeClass(
            driver.personalBestLapMs,
            data.sessionBestLapMs,
            driver.personalBestLapMs
        );
        const bestLapDelta = formatLapDelta(driver.personalBestLapMs, data.sessionBestLapMs);
        const lastLapDelta = formatLapDelta(driver.lastLapTimeMs, driver.personalBestLapMs, ' (PB)');
        const isLapHistoryActive = String(driver.playerId) === visibleLapHistoryDriverId;

        const getDisplayedSectorTime = (sectorNum) => {
            const currentSectorTime = driver.currentSectorProgress ? driver.currentSectorProgress[sectorNum] : 0;
            const bestPersonalSectorTime = driver.personalBestSectors ? driver.personalBestSectors[sectorNum] : 0;
            return currentSectorTime || bestPersonalSectorTime || 0;
        };

        const getCurrentSectorTime = (sectorNum) => {
            return driver.currentSectorProgress ? driver.currentSectorProgress[sectorNum] || 0 : 0;
        };

        const getSectorDelta = (sectorNum) => {
            const currentSectorTime = getCurrentSectorTime(sectorNum);
            const bestPersonalSectorTime = driver.personalBestSectors ? driver.personalBestSectors[sectorNum] : 0;
            return formatSectorDelta(currentSectorTime, bestPersonalSectorTime);
        };

        const sectorTimeClass = (sectorNum) => {
            const currentSectorTime = getCurrentSectorTime(sectorNum);
            const displayedTime = getDisplayedSectorTime(sectorNum);
            const bestSession = data.sessionBestSectors ? data.sessionBestSectors[sectorNum] : 0;
            const bestPersonal = driver.personalBestSectors ? driver.personalBestSectors[sectorNum] : 0;
            return getSectorTimeClass(currentSectorTime, displayedTime, bestSession, bestPersonal);
        };

        const positionBadgeClass = position <= 3
            ? `position-${position}`
            : "";
        const hoverClass = String(driver.playerId) === hoveredDriverId ? " is-hovered" : "";

        const driverColor = driver.driverColor || "#9CA3AF";
        const driverNameStyle = `style="background-color: ${driverColor}15; border-left: 3px solid ${driverColor}; color: #F5F5F5;"`;

        html += `
            <tr class="driver-row${hoverClass}" data-driver-id="${driver.playerId}">
                <td class="px-4 py-3">
                    <div class="position-badge ${positionBadgeClass}">${position}</div>
                </td>
                <td class="px-4 py-3 font-semibold driver-name" ${driverNameStyle} data-driver-name-id="${driver.playerId}"></td>
                <td class="px-4 py-3 text-sm text-gray-400">${driver.carName}</td>
                <td class="px-4 py-3">${driver.lapsCompleted}</td>
                <td class="px-4 py-3 font-mono text-sm">
                    <div class="lap-time-cell lap-history-trigger${isLapHistoryActive ? ' is-active' : ''}" data-last-lap-driver-id="${driver.playerId}">
                        <span class="current-time px-2 py-1 rounded">${formatTime(driver.lastLapTimeMs)}</span>
                        ${lastLapDelta ? `<span class="lap-time-delta">${lastLapDelta}</span>` : ""}
                    </div>
                </td>
                <td class="px-4 py-3 text-sm gap-indicator">
                    <span class="gap-chip${isFightForPosition ? ' is-battle' : ''}">
                        ${isFightForPosition ? '<span class="gap-fight-dot"></span>' : ''}
                        <span>${gap}</span>
                    </span>
                </td>
                <td class="px-4 py-3 font-mono text-sm">
                    <div class="lap-time-cell">
                        <span class="${lapTimeClass} px-2 py-1 rounded">${formatTime(driver.personalBestLapMs)}</span>
                        ${bestLapDelta ? `<span class="lap-time-delta">${bestLapDelta}</span>` : ""}
                    </div>
                </td>
                <td class="px-4 py-3 text-xs">
                    ${sectorNumbers.length === 0
                        ? '<span class="text-gray-500">-</span>'
                        : sectorNumbers.map(sectorNum => `
                            <div class="sector-row">
                                <span class="sector-time ${sectorTimeClass(sectorNum)}">S${sectorNum}: ${formatTime(getDisplayedSectorTime(sectorNum))}</span>
                                ${getSectorDelta(sectorNum) ? `<span class="sector-delta">${getSectorDelta(sectorNum)}</span>` : ""}
                            </div>`).join('')}
                </td>
                <td class="px-4 py-3 text-sm font-mono text-gray-300">${formatSpeedKmh(driver.topSpeedKmh)}</td>
                <td class="px-4 py-3">
                    ${renderTyreSummary(driver.tyreTypes)}
                </td>
                <td class="px-4 py-3 text-center">${driver.pitStops}</td>
            </tr>`;
    });

    tableBody.innerHTML = html;

    // Apply colored driver names via innerHTML (safe: generated server-side with HtmlEncode)
    data.players.forEach((driver) => {
        const nameCell = tableBody.querySelector(`[data-driver-name-id="${driver.playerId}"]`);
        if (nameCell) {
            nameCell.innerHTML = driver.nameHtml || driver.name;
        }
    });

    refreshLapHistoryTriggerStyles();
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

    document.getElementById("session-top-speed").textContent = formatSpeedKmh(data.sessionTopSpeedKmh);

    const topSpeedInfoDiv = document.getElementById("session-top-speed-info");
    if (data.sessionTopSpeedAuthorName && data.sessionTopSpeedKmh) {
        const author = data.sessionTopSpeedAuthorName;
        const username = data.sessionTopSpeedAuthorUsername
            ? ` <span style="color:#AAAAAA">(${data.sessionTopSpeedAuthorUsername})</span>`
            : "";
        topSpeedInfoDiv.innerHTML = `<p style="color: #c0c0c0;">${author}${username}</p>`;
    } else {
        topSpeedInfoDiv.innerHTML = `<p style="color: #888;">-</p>`;
    }

    const sectorNumbers = getSectorNumbers(data);
    const bestSectorsGrid = document.getElementById("best-sectors-grid");
    bestSectorsGrid.className = `grid gap-2 ${sectorNumbers.length > 0 ? `grid-cols-${sectorNumbers.length}` : 'grid-cols-1'}`;
    bestSectorsGrid.innerHTML = sectorNumbers.length === 0
        ? `<p class="text-sm text-gray-500">No sector timing</p>`
        : sectorNumbers.map(sectorNum => {
            const sectorInfo = data.sessionBestSectorInfos?.[sectorNum];
            const sectorTime = sectorInfo?.timeMs ?? data.sessionBestSectors?.[sectorNum] ?? 0;
            const authorName = sectorInfo?.authorNameHtml || "";
            const authorUsername = sectorInfo?.authorUsername
                ? ` <span style="color:#AAAAAA">(${sectorInfo.authorUsername})</span>`
                : "";
            const authorLine = authorName
                ? `<p class="text-xs mt-1" style="color: #c0c0c0;">${authorName}${authorUsername}</p>`
                : `<p class="text-xs mt-1 text-gray-500">-</p>`;

            return `
            <div>
                <p class="text-xs text-gray-500">S${sectorNum}</p>
                <p class="text-xl font-bold text-purple-300">${formatTime(sectorTime) || "-"}</p>
                ${authorLine}
            </div>`;
        }).join("");
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
    initializeTableHoverState();

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
