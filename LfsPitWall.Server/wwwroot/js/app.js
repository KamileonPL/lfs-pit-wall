/* ===========================================================
   LFS Pit Wall – Frontend Application
   SignalR real-time timing dashboard
   =========================================================== */

// ── State ──────────────────────────────────────────────────

let hoveredDriverId = null;
let hoveredDriverProfileId = null;
let hoveredLapHistoryDriverId = null;
let visibleDriverProfileId = null;
let visibleLapHistoryDriverId = null;
let latestSessionData = null;
let lapHistorySessionKey = null;
let lapHistoryShowTimer = null;
let lapHistoryHideTimer = null;
let driverProfileShowTimer = null;
let driverProfileHideTimer = null;
let driverProfileRetryTimer = null;
let lastPointerClientX = null;
let lastPointerClientY = null;
let isLapHistoryTooltipHovered = false;
let isDriverProfileTooltipHovered = false;
let localClockTimerId = null;
let sessionClockTimerId = null;
let sessionClockBaseMs = 0;
let sessionClockSyncedAtMs = 0;
let sessionClockLastServerMs = 0;
let sessionClockRunning = false;
let lastRenderedChatRevision = null;
let standingsViewMode = "table";
const selectedDriverIds = new Set();
const driverLapHistoryCache = new Map();
const driverProfileCache = new Map();
const LAP_HISTORY_SHOW_DELAY_MS = 240;
const LAP_HISTORY_HIDE_DELAY_MS = 80;
const DRIVER_PROFILE_HIDE_DELAY_MS = 180;
const DRIVER_PROFILE_RETRY_DELAY_MS = 6500;
const DRIVER_PROFILE_TOOLTIP_GAP_PX = 8;
const COUNTRY_CODE_ALIASES = {
    "czech republic": "CZ",
    "russia": "RU",
    "south korea": "KR",
    "north korea": "KP",
    "taiwan": "TW",
    "venezuela": "VE"
};
const LFS_DEFAULT_COLOR = "#6B8E23";
const LFS_COLOR_MAP = {
    0: "#000000",
    1: "#FF0000",
    2: "#00FF00",
    3: "#FFFF00",
    4: "#0000FF",
    5: "#FF00FF",
    6: "#00FFFF",
    7: "#FFFFFF",
    8: LFS_DEFAULT_COLOR
};
const LFS_ESCAPE_MAP = {
    v: "|",
    a: "*",
    c: ":",
    d: "\\",
    s: "/",
    q: "?",
    t: '"',
    l: "<",
    r: ">",
    "^": "^"
};
const countryDisplayNames = typeof Intl !== "undefined" && typeof Intl.DisplayNames === "function"
    ? new Intl.DisplayNames([navigator.language || "en"], { type: "region" })
    : null;

function renderLocalDateTime() {
    const timeElement = document.getElementById("live-local-time");
    const dateElement = document.getElementById("live-local-date");
    if (!timeElement || !dateElement) {
        return;
    }

    const now = new Date();
    timeElement.textContent = now.toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit"
    });
    dateElement.textContent = now.toLocaleDateString([], {
        day: "2-digit",
        month: "short",
        year: "numeric"
    });
}

function startLocalDateTimeClock() {
    if (localClockTimerId !== null) {
        return;
    }

    renderLocalDateTime();
    localClockTimerId = window.setInterval(renderLocalDateTime, 1000);
}

async function loadAppMetadata() {
    const versionElement = document.getElementById("app-version");
    const projectTypeElement = document.getElementById("app-project-type");
    const dataSourceLinkElement = document.getElementById("app-data-source-link");
    const debugConsoleSectionElement = document.getElementById("debug-console-section");
    if (!versionElement) {
        return;
    }

    try {
        const response = await fetch("/api/app-meta", { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const metadata = await response.json();
        if (metadata?.version) {
            versionElement.textContent = metadata.version;
        }
        if (projectTypeElement && metadata?.projectType) {
            projectTypeElement.textContent = `${metadata.projectType}.`;
        }
        if (dataSourceLinkElement && metadata?.dataSourceName) {
            dataSourceLinkElement.textContent = metadata.dataSourceName;
        }
        if (dataSourceLinkElement && metadata?.dataSourceUrl) {
            dataSourceLinkElement.href = metadata.dataSourceUrl;
        }
        if (debugConsoleSectionElement && metadata?.showDebugConsole === false) {
            debugConsoleSectionElement.remove();
        }
    } catch (error) {
        debugLog(`App metadata fallback: ${error?.message || error}`, 'warn');
    }
}

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
    renderEstimatedRemaining();
}

function getDisplayedEstimatedRemainingMs() {
    if (!latestSessionData || latestSessionData.estimatedRemainingTimeMs == null || latestSessionData.estimatedRemainingReferenceSessionMs == null) {
        return null;
    }

    const baseRemainingMs = Number(latestSessionData.estimatedRemainingTimeMs);
    const referenceSessionMs = Number(latestSessionData.estimatedRemainingReferenceSessionMs);
    const elapsedSinceEstimateMs = Math.max(0, getDisplayedSessionTimeMs() - referenceSessionMs);

    return Math.max(0, baseRemainingMs - elapsedSinceEstimateMs);
}

function renderEstimatedRemaining() {
    const estimatedRemainingElement = document.getElementById("session-estimated-remaining");
    if (!estimatedRemainingElement) {
        return;
    }

    const displayedRemainingMs = getDisplayedEstimatedRemainingMs();
    estimatedRemainingElement.textContent = displayedRemainingMs == null
        ? "Est. remaining: -"
        : `Est. remaining: ${formatDurationClock(displayedRemainingMs)}`;
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

function getDriverProfileTrigger(element) {
    return element?.closest?.("[data-driver-profile-id]") || null;
}

function clearLapHistoryTimers() {
    window.clearTimeout(lapHistoryShowTimer);
    window.clearTimeout(lapHistoryHideTimer);
    lapHistoryShowTimer = null;
    lapHistoryHideTimer = null;
}

function clearDriverProfileTimers() {
    window.clearTimeout(driverProfileShowTimer);
    window.clearTimeout(driverProfileHideTimer);
    window.clearTimeout(driverProfileRetryTimer);
    driverProfileShowTimer = null;
    driverProfileHideTimer = null;
    driverProfileRetryTimer = null;
}

function closeDriverProfileTooltipImmediately() {
    hoveredDriverProfileId = null;
    visibleDriverProfileId = null;
    isDriverProfileTooltipHovered = false;
    clearDriverProfileTimers();
    refreshDriverProfileTriggerStyles();
    hideDriverProfileTooltip();
}

function getCountryFlagEmoji(countryCode) {
    const normalized = String(countryCode || "").trim().toUpperCase();
    if (!/^[A-Z]{2}$/.test(normalized)) {
        return "";
    }

    return Array.from(normalized)
        .map((letter) => String.fromCodePoint(127397 + letter.charCodeAt(0)))
        .join("");
}

    function getCountryFlagImageUrl(countryCode) {
        const normalized = String(countryCode || "").trim().toLowerCase();
        return /^[a-z]{2}$/.test(normalized)
        ? `https://flagcdn.com/24x18/${normalized}.png`
        : "";
    }

    function resolveCountryCodeForDisplay(countryCode, countryName) {
        const normalizedCode = String(countryCode || "").trim().toUpperCase();
        if (/^[A-Z]{2}$/.test(normalizedCode)) {
            return normalizedCode;
        }

        const normalizedName = String(countryName || "").trim().toLowerCase();
        return COUNTRY_CODE_ALIASES[normalizedName] || "";
    }

function getCountryDisplayName(countryCode) {
    const normalized = String(countryCode || "").trim().toUpperCase();
    if (!normalized) {
        return "";
    }

    return countryDisplayNames?.of(normalized) || normalized;
}

function formatCompactNumber(value) {
    const numericValue = Number(value || 0);
    return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(numericValue);
}

function formatDistanceMeters(distanceMeters) {
    const numericValue = Number(distanceMeters || 0);
    if (!numericValue) {
        return "0 km";
    }

    return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(numericValue / 1000)} km`;
}

function formatRelativeTimestamp(value) {
    if (!value) {
        return "unknown";
    }

    const timestamp = new Date(value);
    if (Number.isNaN(timestamp.getTime())) {
        return "unknown";
    }

    const diffMs = Math.max(0, Date.now() - timestamp.getTime());
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffHours / 24);

    if (diffHours < 1) {
        return "<1h ago";
    }

    if (diffDays < 1) {
        return `${diffHours}h ago`;
    }

    return `${diffDays}d ago`;
}

function escapeHtml(value) {
    return String(value || "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function appendLfsHtmlSegment(segments, text, color) {
    if (!text) {
        return;
    }

    const safeText = escapeHtml(text);
    if (!color) {
        segments.push(safeText);
        return;
    }

    segments.push(`<span style="color:${color}">${safeText}</span>`);
}

function convertLfsTextToHtml(text) {
    const input = String(text || "");
    if (!input) {
        return "";
    }

    const segments = [];
    let currentColor = /\^[0-9]/.test(input) ? LFS_DEFAULT_COLOR : null;
    let buffer = "";

    for (let index = 0; index < input.length; index += 1) {
        const currentChar = input[index];
        const nextChar = input[index + 1];

        if (currentChar === "^" && nextChar) {
            if (Object.prototype.hasOwnProperty.call(LFS_COLOR_MAP, nextChar)) {
                appendLfsHtmlSegment(segments, buffer, currentColor);
                buffer = "";
                currentColor = LFS_COLOR_MAP[nextChar];
                index += 1;
                continue;
            }

            if (nextChar === "9") {
                appendLfsHtmlSegment(segments, buffer, currentColor);
                buffer = "";
                currentColor = /\^[0-9]/.test(input) ? LFS_DEFAULT_COLOR : null;
                index += 1;
                continue;
            }

            if (Object.prototype.hasOwnProperty.call(LFS_ESCAPE_MAP, nextChar)) {
                buffer += LFS_ESCAPE_MAP[nextChar];
                index += 1;
                continue;
            }
        }

        buffer += currentChar;
    }

    appendLfsHtmlSegment(segments, buffer, currentColor);
    return segments.join("");
}

function isDriverProfileHoverArea(element) {
    if (!(element instanceof Element)) {
        return false;
    }

    return Boolean(
        element.closest("#driver-profile-tooltip") ||
        element.closest("[data-driver-profile-id]")
    );
}

function renderCountryFlagBadge(countryCode, countryLabel, options = {}) {
    const normalizedLabel = String(countryLabel || "").trim() || "Country unavailable";
    const flagImageUrl = getCountryFlagImageUrl(countryCode);

    if (flagImageUrl) {
        return `<span class="driver-flag-badge" title="${normalizedLabel}"><img class="driver-flag-image" src="${flagImageUrl}" alt="${normalizedLabel} flag" loading="lazy" decoding="async"></span>`;
    }

    if (options.isPending) {
        return '<span class="driver-flag-badge is-pending" title="Loading driver profile">...</span>';
    }

    return `<span class="driver-flag-badge is-unknown" title="Country unavailable"></span>`;
}

function renderDriverIdentity(driver) {
    const countryName = String(driver.countryName || "");
    const countryCode = resolveCountryCodeForDisplay(driver.countryCode, countryName);
    const countryDisplayName = getCountryDisplayName(countryCode) || countryName;
    const hasProfileTrigger = Boolean(driver.username);
    const triggerClassName = `driver-profile-trigger${hasProfileTrigger ? " is-enabled" : ""}`;
    const flagMarkup = hasProfileTrigger
        ? renderCountryFlagBadge(countryCode, countryDisplayName || countryCode, { isPending: driver.driverProfilePending })
        : "";

    return `
        <div class="${triggerClassName}">
            ${flagMarkup}
            <div class="driver-profile-copy">
                <div class="driver-profile-name">${driver.nameHtml || driver.name || "-"}</div>
            </div>
        </div>`;
}

function refreshLapHistoryTriggerStyles() {
    document.querySelectorAll("[data-last-lap-driver-id]").forEach((trigger) => {
        const driverId = String(trigger.dataset.lastLapDriverId || "");
        trigger.classList.toggle("is-hovered", driverId === hoveredLapHistoryDriverId);
        trigger.classList.toggle("is-active", driverId === visibleLapHistoryDriverId);
    });
}

function refreshDriverProfileTriggerStyles() {
    document.querySelectorAll("[data-driver-profile-id]").forEach((trigger) => {
        const driverId = String(trigger.dataset.driverProfileId || "");
        trigger.classList.toggle("is-hovered", driverId === hoveredDriverProfileId);
        trigger.classList.toggle("is-active", driverId === visibleDriverProfileId);
    });
}

function scheduleDriverProfileRetry(driverId) {
    window.clearTimeout(driverProfileRetryTimer);

    driverProfileRetryTimer = window.setTimeout(() => {
        if (visibleDriverProfileId !== String(driverId)) {
            return;
        }

        const driver = getDriverById(driverId);
        if (!driver) {
            return;
        }

        ensureDriverProfile(driver, true);
    }, DRIVER_PROFILE_RETRY_DELAY_MS);
}

function setDriverProfileHoverTarget(driverId) {
    const nextDriverId = driverId ? String(driverId) : null;

    if (hoveredDriverProfileId === nextDriverId) {
        if (nextDriverId && visibleDriverProfileId === nextDriverId) {
            updateDriverProfileTooltip();
        }

        refreshDriverProfileTriggerStyles();
        return;
    }

    hoveredDriverProfileId = nextDriverId;
    refreshDriverProfileTriggerStyles();
    window.clearTimeout(driverProfileShowTimer);
    window.clearTimeout(driverProfileHideTimer);

    if (!nextDriverId) {
        driverProfileHideTimer = window.setTimeout(() => {
            if (isDriverProfileTooltipHovered) {
                return;
            }

            visibleDriverProfileId = null;
            refreshDriverProfileTriggerStyles();
            hideDriverProfileTooltip();
        }, DRIVER_PROFILE_HIDE_DELAY_MS);
        return;
    }

    if (visibleDriverProfileId === nextDriverId) {
        updateDriverProfileTooltip();
        return;
    }

    visibleDriverProfileId = nextDriverId;
    refreshDriverProfileTriggerStyles();
    const driver = getDriverById(nextDriverId);
    if (driver) {
        ensureDriverProfile(driver);
    }

    updateDriverProfileTooltip();
}

function setLapHistoryHoverTarget(driverId) {
    const nextDriverId = driverId ? String(driverId) : null;
    if (hoveredLapHistoryDriverId === nextDriverId) {
        if (nextDriverId && visibleLapHistoryDriverId === nextDriverId) {
            updateLapHistoryTooltip();
        }

        if (nextDriverId || visibleLapHistoryDriverId === null) {
            refreshLapHistoryTriggerStyles();
            return;
        }

        refreshLapHistoryTriggerStyles();
    }

    hoveredLapHistoryDriverId = nextDriverId;
    refreshLapHistoryTriggerStyles();
    window.clearTimeout(lapHistoryShowTimer);
    window.clearTimeout(lapHistoryHideTimer);

    if (!nextDriverId) {
        lapHistoryHideTimer = window.setTimeout(() => {
            if (isLapHistoryTooltipHovered) {
                return;
            }

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

function syncDriverProfileHoverState() {
    if (lastPointerClientX == null || lastPointerClientY == null) {
        return;
    }

    const hoveredElement = document.elementFromPoint(lastPointerClientX, lastPointerClientY);
    const trigger = getDriverProfileTrigger(hoveredElement);
    if (trigger) {
        setDriverProfileHoverTarget(trigger.dataset.driverProfileId || null);
        return;
    }

    if (!isDriverProfileHoverArea(hoveredElement)) {
        setDriverProfileHoverTarget(null);
    }
}

function getDriverRowFromEventTarget(target) {
    if (target instanceof Element) {
        return target.closest("tr[data-driver-id]");
    }

    if (target instanceof Node && target.parentElement) {
        return target.parentElement.closest("tr[data-driver-id]");
    }

    return null;
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

        const row = getDriverRowFromEventTarget(event.target);
        setHoveredDriverId(row?.dataset.driverId || null);

        const trigger = getLapHistoryTrigger(event.target);
        setLapHistoryHoverTarget(trigger?.dataset.lastLapDriverId || null);

        const profileTrigger = getDriverProfileTrigger(event.target);
        setDriverProfileHoverTarget(profileTrigger?.dataset.driverProfileId || null);
    });

    tableBody.addEventListener("pointerdown", (event) => {
        const row = getDriverRowFromEventTarget(event.target);
        if (!row?.dataset.driverId) {
            return;
        }

        event.preventDefault();
        toggleSelectedDriverId(row.dataset.driverId);
    });

    tableBody.addEventListener("mouseleave", () => {
        setHoveredDriverId(null);
        setLapHistoryHoverTarget(null);
        setDriverProfileHoverTarget(null);
    });

    window.addEventListener("scroll", () => {
        if (visibleLapHistoryDriverId) {
            updateLapHistoryTooltip();
        }

        if (visibleDriverProfileId) {
            updateDriverProfileTooltip();
        }
    }, true);

    window.addEventListener("resize", () => {
        syncLapHistoryHoverState();
        syncDriverProfileHoverState();
        updateLapHistoryTooltip();
        updateDriverProfileTooltip();
    });

    document.addEventListener("mousemove", (event) => {
        lastPointerClientX = event.clientX;
        lastPointerClientY = event.clientY;

        if (!visibleDriverProfileId && !hoveredDriverProfileId) {
            return;
        }

        const hoveredElement = document.elementFromPoint(event.clientX, event.clientY);
        if (isDriverProfileHoverArea(hoveredElement)) {
            const trigger = getDriverProfileTrigger(hoveredElement);
            if (trigger) {
                setDriverProfileHoverTarget(trigger.dataset.driverProfileId || null);
            }

            return;
        }

        setDriverProfileHoverTarget(null);
    });
}

function getDriverById(playerId) {
    return latestSessionData?.players?.find(driver => String(driver.playerId) === String(playerId)) || null;
}

function refreshDriverHoverState() {
    document.querySelectorAll("#drivers-table tr[data-driver-id]").forEach((row) => {
        row.classList.toggle("is-hovered", row.dataset.driverId === hoveredDriverId);
        row.classList.toggle("is-selected", selectedDriverIds.has(row.dataset.driverId));
    });

    window.TrackMapController?.setHoveredDriverId(hoveredDriverId);
}

function getOrCreateDriverProfileTooltip() {
    let tooltip = document.getElementById("driver-profile-tooltip");
    if (tooltip) {
        return tooltip;
    }

    tooltip = document.createElement("div");
    tooltip.id = "driver-profile-tooltip";
    tooltip.className = "driver-profile-tooltip";
    tooltip.addEventListener("mouseenter", () => {
        isDriverProfileTooltipHovered = true;
        window.clearTimeout(driverProfileHideTimer);
    });
    tooltip.addEventListener("mousemove", (event) => {
        lastPointerClientX = event.clientX;
        lastPointerClientY = event.clientY;
    });
    tooltip.addEventListener("mouseleave", (event) => {
        isDriverProfileTooltipHovered = false;
        lastPointerClientX = event.clientX;
        lastPointerClientY = event.clientY;

        const nextTrigger = getDriverProfileTrigger(event.relatedTarget);
        if (nextTrigger) {
            setDriverProfileHoverTarget(nextTrigger.dataset.driverProfileId || null);
            return;
        }

        closeDriverProfileTooltipImmediately();
    });
    document.body.appendChild(tooltip);
    return tooltip;
}

function hideDriverProfileTooltip() {
    const tooltip = document.getElementById("driver-profile-tooltip");
    if (!tooltip) {
        return;
    }

    clearDriverProfileTimers();
    tooltip.classList.remove("is-visible");
    tooltip.innerHTML = "";
}

function renderDriverProfileTooltip(driver, profile) {
    const countryName = String(profile?.countryName || driver.countryName || "");
    const countryCode = resolveCountryCodeForDisplay(profile?.countryCode || driver.countryCode, countryName);
    const countryDisplayName = getCountryDisplayName(countryCode) || countryName;
    const titleName = driver.mapLabelHtml || driver.nameHtml || driver.name || "Driver";
    const subtitle = driver.username
        ? `<span class="driver-profile-tooltip-subtitle">${driver.username}</span>`
        : "";
    const countryLine = `<div class="driver-profile-tooltip-pill${countryCode ? "" : " is-unavailable"}">${renderCountryFlagBadge(countryCode, countryDisplayName || countryCode)}${countryCode ? (countryDisplayName || countryCode) : "Country unavailable"}</div>`;

    if (!driver.username) {
        return `
            <div class="driver-profile-tooltip-header">
                <div class="driver-profile-tooltip-title">${titleName}</div>
                ${subtitle}
            </div>
            <div class="driver-profile-tooltip-empty">No LFS username is available for this driver.</div>`;
    }

    if (!profile || profile.isLoading) {
        return `
            <div class="driver-profile-tooltip-header">
                <div class="driver-profile-tooltip-title">${titleName}</div>
                ${subtitle}
            </div>
            <div class="driver-profile-tooltip-empty">Loading driver profile...</div>`;
    }

    if (!profile.isAvailable) {
        const waitingNote = profile.canRefresh && profile.isRefreshQueued
            ? "The first profile fetch is queued and will appear automatically."
            : (profile.unavailableReason || "Driver profile is unavailable.");

        return `
            <div class="driver-profile-tooltip-header">
                <div class="driver-profile-tooltip-title">${titleName}</div>
                ${subtitle}
                ${countryLine}
            </div>
            <div class="driver-profile-tooltip-empty">${waitingNote}</div>`;
    }

    const stats = profile.stats || {};
    const hostNameHtml = profile.currentOrLastHostNameHtml || convertLfsTextToHtml(stats.currentOrLastHostName || "") || "-";
    const refreshedText = profile.lastSuccessAtUtc
        ? `Refreshed ${formatRelativeTimestamp(profile.lastSuccessAtUtc)}`
        : "Cached locally";

    return `
        <div class="driver-profile-tooltip-header">
            <div>
                <div class="driver-profile-tooltip-title">${titleName}</div>
                ${subtitle}
            </div>
            ${countryLine}
        </div>
        <div class="driver-profile-tooltip-grid">
            <div class="driver-profile-stat-card">
                <span class="driver-profile-stat-label">Wins</span>
                <span class="driver-profile-stat-value">${formatCompactNumber(stats.wins)}</span>
            </div>
            <div class="driver-profile-stat-card">
                <span class="driver-profile-stat-label">Podiums</span>
                <span class="driver-profile-stat-value">${formatCompactNumber(stats.podiums)}</span>
            </div>
            <div class="driver-profile-stat-card">
                <span class="driver-profile-stat-label">Finishes</span>
                <span class="driver-profile-stat-value">${formatCompactNumber(stats.finishes)}</span>
            </div>
            <div class="driver-profile-stat-card">
                <span class="driver-profile-stat-label">Laps</span>
                <span class="driver-profile-stat-value">${formatCompactNumber(stats.laps)}</span>
            </div>
            <div class="driver-profile-stat-card">
                <span class="driver-profile-stat-label">Hosts</span>
                <span class="driver-profile-stat-value">${formatCompactNumber(stats.hostsJoined)}</span>
            </div>
            <div class="driver-profile-stat-card">
                <span class="driver-profile-stat-label">Distance</span>
                <span class="driver-profile-stat-value">${formatDistanceMeters(stats.distanceMeters)}</span>
            </div>
        </div>
        <div class="driver-profile-tooltip-meta">
            <div class="driver-profile-tooltip-meta-row"><span>Last host</span><strong class="driver-profile-tooltip-host">${hostNameHtml}</strong></div>
            <div class="driver-profile-tooltip-meta-row"><span>Last combo</span><strong>${stats.currentOrLastTrack || "-"} ${stats.currentOrLastCar || ""}</strong></div>
            <div class="driver-profile-tooltip-meta-row"><span>LFS World</span><strong>${refreshedText}</strong></div>
        </div>`;
}

function positionDriverProfileTooltip(trigger, tooltip) {
    const rect = trigger.getBoundingClientRect();
    const margin = 12;
    const gap = DRIVER_PROFILE_TOOLTIP_GAP_PX;

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

function updateDriverProfileTooltip() {
    const tooltip = getOrCreateDriverProfileTooltip();
    if (!visibleDriverProfileId) {
        hideDriverProfileTooltip();
        return;
    }

    const trigger = document.querySelector(`[data-driver-profile-id="${visibleDriverProfileId}"]`);
    const driver = getDriverById(visibleDriverProfileId);
    if (!trigger || !driver) {
        hideDriverProfileTooltip();
        return;
    }

    const profile = driverProfileCache.get(String(driver.playerId));
    tooltip.innerHTML = renderDriverProfileTooltip(driver, profile);
    positionDriverProfileTooltip(trigger, tooltip);

    if (profile?.canRefresh && profile?.isRefreshQueued && !profile?.isAvailable) {
        scheduleDriverProfileRetry(driver.playerId);
    }
}

async function ensureDriverProfile(driver, force = false) {
    if (!driver?.username) {
        return;
    }

    const cacheKey = String(driver.playerId);
    const cached = driverProfileCache.get(cacheKey);
    const hasFormattedHostName = typeof cached?.currentOrLastHostNameHtml === "string" && cached.currentOrLastHostNameHtml.length > 0;
    if (!force && cached?.isAvailable && hasFormattedHostName && !cached?.isRefreshQueued) {
        return;
    }

    if (cached?.isLoading) {
        return;
    }

    driverProfileCache.set(cacheKey, {
        ...(cached || {}),
        playerId: Number(driver.playerId),
        isLoading: true
    });

    try {
        const profile = await window.signalRConnection?.invoke("GetDriverProfile", Number(driver.playerId));
        driverProfileCache.set(cacheKey, profile);

        if (visibleDriverProfileId === cacheKey) {
            updateDriverProfileTooltip();
        }
    } catch (error) {
        driverProfileCache.set(cacheKey, {
            playerId: Number(driver.playerId),
            canRefresh: false,
            isAvailable: false,
            unavailableReason: error?.message || String(error)
        });

        if (visibleDriverProfileId === cacheKey) {
            updateDriverProfileTooltip();
        }
    }
}

function setHoveredDriverId(driverId) {
    const nextDriverId = driverId ? String(driverId) : null;
    if (hoveredDriverId === nextDriverId) {
        return;
    }

    hoveredDriverId = nextDriverId;
    refreshDriverHoverState();
}

function getSelectedDriverIds() {
    return new Set(selectedDriverIds);
}

function refreshSelectedDriverState() {
    document.querySelectorAll("#drivers-table tr[data-driver-id]").forEach((row) => {
        row.classList.toggle("is-selected", selectedDriverIds.has(row.dataset.driverId));
    });

    window.TrackMapController?.refreshSelection();
}

function toggleSelectedDriverId(driverId) {
    const nextDriverId = driverId ? String(driverId) : null;
    if (!nextDriverId) {
        return;
    }

    if (selectedDriverIds.has(nextDriverId)) {
        selectedDriverIds.delete(nextDriverId);
    } else {
        selectedDriverIds.add(nextDriverId);
    }

    refreshSelectedDriverState();
}

function pruneSelectedDriverIds(activeDriverIds) {
    let changed = false;
    Array.from(selectedDriverIds).forEach((driverId) => {
        if (!activeDriverIds.has(driverId)) {
            selectedDriverIds.delete(driverId);
            changed = true;
        }
    });

    if (changed) {
        refreshSelectedDriverState();
    }
}

function setStandingsViewMode(mode) {
    standingsViewMode = mode === "map" ? "map" : "table";

    const tableView = document.getElementById("standings-table-view");
    const mapView = document.getElementById("standings-map-view");
    const tableButton = document.getElementById("standings-view-table");
    const mapButton = document.getElementById("standings-view-map");

    if (tableView) {
        tableView.hidden = standingsViewMode !== "table";
    }

    if (mapView) {
        mapView.hidden = standingsViewMode !== "map";
    }

    if (tableButton) {
        tableButton.classList.toggle("is-active", standingsViewMode === "table");
        tableButton.setAttribute("aria-pressed", standingsViewMode === "table" ? "true" : "false");
    }

    if (mapButton) {
        mapButton.classList.toggle("is-active", standingsViewMode === "map");
        mapButton.setAttribute("aria-pressed", standingsViewMode === "map" ? "true" : "false");
    }

    window.TrackMapController?.setViewActive(standingsViewMode === "map");
}

function initializeStandingsViewToggle() {
    document.querySelectorAll("[data-standings-view]").forEach((button) => {
        if (button.dataset.standingsViewBound === "true") {
            return;
        }

        button.dataset.standingsViewBound = "true";
        button.addEventListener("click", () => {
            setStandingsViewMode(button.dataset.standingsView || "table");
        });
    });

    setStandingsViewMode(standingsViewMode);
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
        isLapHistoryTooltipHovered = false;
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
    tooltip.addEventListener("mouseenter", () => {
        isLapHistoryTooltipHovered = true;
        window.clearTimeout(lapHistoryHideTimer);
    });
    tooltip.addEventListener("mousemove", (event) => {
        lastPointerClientX = event.clientX;
        lastPointerClientY = event.clientY;
    });
    tooltip.addEventListener("mouseleave", (event) => {
        isLapHistoryTooltipHovered = false;
        lastPointerClientX = event.clientX;
        lastPointerClientY = event.clientY;

        const nextTrigger = getLapHistoryTrigger(event.relatedTarget);
        setLapHistoryHoverTarget(nextTrigger?.dataset.lastLapDriverId || null);
    });
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
        window.TrackMapController?.handleSessionUpdate(data);
        renderChatMessages(data);
        updateDriversTable(data);
        updateBestLaps(data);
        syncLapHistoryHoverState();
        syncDriverProfileHoverState();

        if (visibleLapHistoryDriverId) {
            const driver = getDriverById(visibleLapHistoryDriverId);
            if (driver) {
                ensureDriverLapHistory(driver);
            }
        }

        updateLapHistoryTooltip();
        updateDriverProfileTooltip();
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

function getPitStatusMeta(pitStatus) {
    switch (pitStatus) {
        case "service":
            return { label: "Service", className: "text-amber-300" };
        case "lane":
            return { label: "Pit lane", className: "text-sky-300" };
        case "drive-through":
            return { label: "Drive-through", className: "text-orange-300" };
        case "stop-go":
            return { label: "Stop-go", className: "text-rose-300" };
        case "no-purpose":
            return { label: "No purpose", className: "text-gray-400" };
        default:
            return null;
    }
}

function renderPitSummary(driver) {
    const pitStatusMeta = getPitStatusMeta(driver.pitStatus);
    const pitStopCount = Number(driver.pitStops || 0);
    const pitLaneTime = driver.pitLaneTimeMs ? formatTime(driver.pitLaneTimeMs) : "";
    const statusLine = pitStatusMeta
        ? `<span class="text-[11px] uppercase tracking-[0.18em] ${pitStatusMeta.className}">${pitStatusMeta.label}</span>`
        : "";
    const timeLine = pitLaneTime
        ? `<span class="text-[11px] text-gray-500">${pitLaneTime}</span>`
        : "";

    return `
        <div class="flex flex-col items-center leading-tight gap-1">
            <span class="font-semibold text-gray-100">${pitStopCount}</span>
            ${statusLine}
            ${timeLine}
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
    const serverNameElement = document.getElementById("server-name");

    document.getElementById("track-name").textContent = data.trackName || "Unknown";
    document.getElementById("session-type").textContent = data.sessionType || "-";
    document.getElementById("weather-type").textContent = data.weatherType || "-";
    document.getElementById("wind-type").textContent = data.windType || "-";
    document.getElementById("race-status").textContent = data.raceInProgress ? "🏁 LIVE" : "Idle";
    document.getElementById("driver-count").textContent = Array.isArray(data.players) ? data.players.length : 0;

    if (serverNameElement) {
        serverNameElement.innerHTML = data.hostNameHtml || data.hostName || "-";
    }

    const maxLaps = Math.max(0, ...data.players.map(p => p.lapsCompleted || 0));
    const displayMaxLaps = data.maxRaceLaps || maxLaps;
    document.getElementById("max-laps").textContent = `${maxLaps}/${displayMaxLaps} Laps`;
    renderEstimatedRemaining();
}

// ── Chat Panel ───────────────────────────────────────────

function formatChatTimestamp(value) {
    if (!value) {
        return "--:--:--";
    }

    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) {
        return "--:--:--";
    }

    return parsed.toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit"
    });
}

function getChatKindMeta(kind) {
    switch ((kind || "").toLowerCase()) {
        case "user":
            return { label: "User", className: "is-user" };
        case "prefix":
            return { label: "Prefix", className: "is-prefix" };
        case "local":
            return { label: "Local", className: "is-local" };
        default:
            return { label: "System", className: "is-system" };
    }
}

function isPinnedToBottom(element) {
    if (!element) {
        return true;
    }

    return element.scrollHeight - element.scrollTop - element.clientHeight < 24;
}

function renderChatMessages(data) {
    const chatPanelElement = document.getElementById("chat-panel");
    const chatCountElement = document.getElementById("chat-message-count");
    if (!chatPanelElement || !chatCountElement) {
        return;
    }

    const nextRevision = Number(data.chatRevision || 0);
    if (lastRenderedChatRevision === nextRevision) {
        return;
    }

    const chatMessages = Array.isArray(data.chatMessages) ? data.chatMessages : [];
    const keepScrollPinned = isPinnedToBottom(chatPanelElement);
    lastRenderedChatRevision = nextRevision;
    chatCountElement.textContent = `${chatMessages.length} message${chatMessages.length === 1 ? "" : "s"}`;

    if (chatMessages.length === 0) {
        chatPanelElement.innerHTML = '<div class="chat-empty-state">Waiting for chat activity...</div>';
        return;
    }

    chatPanelElement.innerHTML = chatMessages.map((message) => {
        const kindMeta = getChatKindMeta(message.kind);
        const messageHtml = message.messageHtml || message.messageText || "-";

        return `
            <article class="chat-entry ${kindMeta.className}">
                <div class="chat-entry-meta">
                    <span class="chat-entry-kind ${kindMeta.className}">${kindMeta.label}</span>
                    <span class="chat-entry-time">${formatChatTimestamp(message.receivedAtUtc)}</span>
                </div>
                <div class="chat-entry-message">${messageHtml}</div>
            </article>`;
    }).join("");

    if (keepScrollPinned) {
        chatPanelElement.scrollTop = chatPanelElement.scrollHeight;
    }
}

// ── Drivers Table Update ──────────────────────────────────

function updateDriversTable(data) {
    const tableBody = document.getElementById("drivers-table");
    const playerIds = new Set((data.players || []).map(player => String(player.playerId)));
    pruneSelectedDriverIds(playerIds);

    if (hoveredDriverId && !playerIds.has(hoveredDriverId)) {
        setHoveredDriverId(null);
    }

    if (hoveredDriverProfileId && !playerIds.has(hoveredDriverProfileId)) {
        setDriverProfileHoverTarget(null);
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
        const selectedClass = selectedDriverIds.has(String(driver.playerId)) ? " is-selected" : "";

        const driverColor = driver.driverColor || "#9CA3AF";
        const driverNameStyle = `style="background-color: ${driverColor}15; border-left: 3px solid ${driverColor}; color: #F5F5F5;"`;

        html += `
            <tr class="driver-row${hoverClass}${selectedClass}" data-driver-id="${driver.playerId}">
                <td class="px-4 py-3">
                    <div class="position-badge ${positionBadgeClass}">${position}</div>
                </td>
                <td class="px-4 py-3 font-semibold driver-name${driver.username ? ' driver-name--profile' : ''}" ${driverNameStyle}${driver.username ? ` data-driver-profile-id="${driver.playerId}"` : ''}>${renderDriverIdentity(driver)}</td>
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
                <td class="px-4 py-3 text-center">${renderPitSummary(driver)}</td>
            </tr>`;
    });

    tableBody.innerHTML = html;

    refreshDriverHoverState();
    refreshLapHistoryTriggerStyles();
    refreshDriverProfileTriggerStyles();
    syncDriverProfileHoverState();
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
    const bestSectorsTitle = document.getElementById("best-sectors-title");
    const bestSectorsGrid = document.getElementById("best-sectors-grid");
    const theoreticalBestSectorTimes = sectorNumbers.map((sectorNum) =>
        data.sessionBestSectorInfos?.[sectorNum]?.timeMs ?? data.sessionBestSectors?.[sectorNum] ?? 0);
    const theoreticalBestLapMs = theoreticalBestSectorTimes.length > 0 && theoreticalBestSectorTimes.every((sectorTime) => sectorTime > 0)
        ? theoreticalBestSectorTimes.reduce((total, sectorTime) => total + sectorTime, 0)
        : 0;

    if (bestSectorsTitle) {
        bestSectorsTitle.textContent = theoreticalBestLapMs > 0
            ? `📊 Best Sectors (${formatTime(theoreticalBestLapMs)})`
            : "📊 Best Sectors";
    }

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
    const consoleEl = document.getElementById("debug-console");
    if (!consoleEl) {
        return;
    }

    consoleEl.innerHTML = '';
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
    initializeStandingsViewToggle();
    startLocalDateTimeClock();
    loadAppMetadata();
    window.TrackMapController?.initialize({
        getLatestSessionData: () => latestSessionData,
        getHoveredDriverId: () => hoveredDriverId,
        setHoveredDriverId: (driverId) => setHoveredDriverId(driverId),
        getSelectedDriverIds: () => getSelectedDriverIds(),
        toggleSelectedDriverId: (driverId) => toggleSelectedDriverId(driverId)
    });

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
