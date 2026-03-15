let archiveCatalog = null;
let archiveDetail = null;
let selectedSessionId = null;
let localClockTimerId = null;
let archiveSearchTimerId = null;
let archiveCurrentPage = 1;
const selectedComparisonDriverIds = new Set();
const chartPalette = ["#facc15", "#38bdf8", "#f87171", "#34d399", "#c084fc", "#fb923c", "#a3e635", "#f472b6"];
const archivePageSize = 12;
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

document.addEventListener("DOMContentLoaded", () => {
    applyInitialFiltersFromQuery();
    bindArchiveFilters();
    startLocalDateTimeClock();
    loadAppMetadata();
    loadArchiveCatalog();
});

function bindArchiveFilters() {
    const searchInput = document.getElementById("archive-search-input");
    const trackFilter = document.getElementById("archive-track-filter");
    const sessionTypeFilter = document.getElementById("archive-session-type-filter");

    if (searchInput) {
        searchInput.addEventListener("input", () => {
            window.clearTimeout(archiveSearchTimerId);
            archiveSearchTimerId = window.setTimeout(() => {
                archiveCurrentPage = 1;
                loadArchiveCatalog(false);
            }, 180);
        });
    }

    trackFilter?.addEventListener("change", () => {
        archiveCurrentPage = 1;
        loadArchiveCatalog(false);
    });
    sessionTypeFilter?.addEventListener("change", () => {
        archiveCurrentPage = 1;
        loadArchiveCatalog(false);
    });
}

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
    } catch (error) {
        console.warn("Archive metadata fallback", error);
    }
}

function getArchiveFilters() {
    return {
        search: document.getElementById("archive-search-input")?.value?.trim() || "",
        track: document.getElementById("archive-track-filter")?.value || "",
        sessionType: document.getElementById("archive-session-type-filter")?.value || ""
    };
}

function updateArchiveQueryString() {
    const url = new URL(window.location.href);
    const { search, track, sessionType } = getArchiveFilters();

    if (search) {
        url.searchParams.set("search", search);
    } else {
        url.searchParams.delete("search");
    }

    if (track) {
        url.searchParams.set("track", track);
    } else {
        url.searchParams.delete("track");
    }

    if (sessionType) {
        url.searchParams.set("sessionType", sessionType);
    } else {
        url.searchParams.delete("sessionType");
    }

    if (archiveCurrentPage > 1) {
        url.searchParams.set("page", String(archiveCurrentPage));
    } else {
        url.searchParams.delete("page");
    }

    if (selectedSessionId) {
        url.searchParams.set("session", selectedSessionId);
    } else {
        url.searchParams.delete("session");
    }

    window.history.replaceState({}, "", url);
}

function applyInitialFiltersFromQuery() {
    const url = new URL(window.location.href);
    const searchInput = document.getElementById("archive-search-input");
    const trackFilter = document.getElementById("archive-track-filter");
    const sessionTypeFilter = document.getElementById("archive-session-type-filter");

    if (searchInput) {
        searchInput.value = url.searchParams.get("search") || "";
    }
    if (trackFilter) {
        trackFilter.dataset.pendingValue = url.searchParams.get("track") || "";
    }
    if (sessionTypeFilter) {
        sessionTypeFilter.dataset.pendingValue = url.searchParams.get("sessionType") || "";
    }

    archiveCurrentPage = Math.max(1, Number(url.searchParams.get("page") || "1") || 1);
}

async function loadArchiveCatalog(keepSelection = true) {
    const catalogMeta = document.getElementById("archive-catalog-meta");
    if (catalogMeta) {
        catalogMeta.textContent = "Loading archive catalog...";
    }

    const filters = getArchiveFilters();
    const params = new URLSearchParams();
    if (filters.search) {
        params.set("search", filters.search);
    }
    if (filters.track) {
        params.set("track", filters.track);
    }
    if (filters.sessionType) {
        params.set("sessionType", filters.sessionType);
    }
    params.set("page", String(archiveCurrentPage));
    params.set("pageSize", String(archivePageSize));

    try {
        const response = await fetch(`/api/archive/sessions?${params.toString()}`, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        archiveCatalog = await response.json();
        archiveCurrentPage = Math.max(1, Number(archiveCatalog.page || archiveCurrentPage || 1));
        renderArchiveOverview();
        renderArchiveFilters();
        renderArchiveSessionList();
        renderArchivePagination();

        const requestedSessionId = new URL(window.location.href).searchParams.get("session");
        const availableSessionIds = new Set((archiveCatalog.items || []).map((item) => item.sessionId));
        const nextSessionId = keepSelection && selectedSessionId && availableSessionIds.has(selectedSessionId)
            ? selectedSessionId
            : requestedSessionId && availableSessionIds.has(requestedSessionId)
                ? requestedSessionId
                : archiveCatalog.items?.[0]?.sessionId || null;

        if (!nextSessionId) {
            selectedSessionId = null;
            archiveDetail = null;
            renderArchiveDetailEmpty("No archived sessions match the current filters.");
            updateArchiveQueryString();
            return;
        }

        await loadArchiveSessionDetail(nextSessionId, false);
        updateArchiveQueryString();
    } catch (error) {
        console.error(error);
        renderArchiveDetailEmpty("Failed to load archive catalog.");
        renderArchivePagination();
        if (catalogMeta) {
            catalogMeta.textContent = "Archive catalog unavailable.";
        }
    }
}

function renderArchiveOverview() {
    const overview = archiveCatalog?.overview || {};
    document.getElementById("archive-total-sessions").textContent = String(overview.totalSessions || 0);
    document.getElementById("archive-total-tracks").textContent = String(overview.totalTracks || 0);
    document.getElementById("archive-latest-session").textContent = overview.latestSessionStartedAtUtc
        ? `${overview.latestTrackName || "Unknown"} • ${overview.latestSessionType || "Unknown"} • ${formatDateTime(overview.latestSessionStartedAtUtc)}`
        : "-";
}

function renderArchiveFilters() {
    renderSelectOptions("archive-track-filter", archiveCatalog?.filters?.tracks || [], "All tracks");
    renderSelectOptions("archive-session-type-filter", archiveCatalog?.filters?.sessionTypes || [], "All session types");
}

function renderSelectOptions(elementId, values, defaultLabel) {
    const selectElement = document.getElementById(elementId);
    if (!selectElement) {
        return;
    }

    const currentValue = selectElement.value || selectElement.dataset.pendingValue || "";
    selectElement.innerHTML = `<option value="">${defaultLabel}</option>${values.map((value) => `<option value="${escapeHtml(value)}">${escapeHtml(value)}</option>`).join("")}`;

    if (Array.from(selectElement.options).some((option) => option.value === currentValue)) {
        selectElement.value = currentValue;
    }

    delete selectElement.dataset.pendingValue;
}

function renderArchiveSessionList() {
    const listElement = document.getElementById("archive-session-list");
    const metaElement = document.getElementById("archive-catalog-meta");
    const items = archiveCatalog?.items || [];

    if (metaElement) {
        const totalCount = Number(archiveCatalog?.totalCount || 0);
        const pageCount = Math.max(1, Math.ceil(totalCount / Math.max(1, Number(archiveCatalog?.pageSize || archivePageSize))));
        metaElement.textContent = `${totalCount} session${totalCount === 1 ? "" : "s"} found • page ${archiveCurrentPage}/${pageCount}`;
    }

    if (!listElement) {
        return;
    }

    if (items.length === 0) {
        listElement.innerHTML = '<div class="archive-empty-state">No archived sessions match the current filters.</div>';
        return;
    }

    listElement.innerHTML = items.map((item) => {
        const isActive = item.sessionId === selectedSessionId;
        return `
            <button type="button" class="archive-session-card${isActive ? " is-active" : ""}" data-session-id="${escapeHtml(item.sessionId)}">
                <div class="archive-session-card-top">
                    <div>
                        <p class="archive-session-track">${escapeHtml(item.trackName || "Unknown")}</p>
                        <p class="archive-session-type">${escapeHtml(item.sessionType || "Unknown")}</p>
                    </div>
                    <span class="archive-session-badge">v${Number(item.schemaVersion || 0)}</span>
                </div>
                <p class="archive-session-meta">${formatDateTime(item.sessionStartedAtUtc)}</p>
                <div class="archive-session-stats">
                    <span>${Number(item.driverCount || 0)} drivers</span>
                    <span>${Number(item.completedLaps || 0)} laps</span>
                </div>
                <div class="archive-session-highlights">
                    <div>
                        <span class="archive-session-highlight-label">Best lap</span>
                        <span class="archive-session-highlight-value">${formatLapTime(item.sessionBestLapMs)}</span>
                    </div>
                    <div>
                        <span class="archive-session-highlight-label">Winner</span>
                        <span class="archive-session-highlight-value">${formatArchiveDriverName(item.winnerName, "-")}</span>
                    </div>
                </div>
            </button>
        `;
    }).join("");

    listElement.querySelectorAll("[data-session-id]").forEach((button) => {
        button.addEventListener("click", () => {
            const sessionId = button.getAttribute("data-session-id");
            if (sessionId) {
                loadArchiveSessionDetail(sessionId, true);
            }
        });
    });
}

function renderArchivePagination() {
    const container = document.getElementById("archive-pagination");
    if (!container) {
        return;
    }

    const totalCount = Number(archiveCatalog?.totalCount || 0);
    const pageSize = Math.max(1, Number(archiveCatalog?.pageSize || archivePageSize));
    const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));

    if (!archiveCatalog || totalCount === 0) {
        container.innerHTML = "";
        return;
    }

    container.innerHTML = `
        <div class="archive-pagination-meta">Showing ${Math.min(totalCount, ((archiveCurrentPage - 1) * pageSize) + 1)}-${Math.min(totalCount, archiveCurrentPage * pageSize)} of ${totalCount}</div>
        <div class="archive-pagination-actions">
            <button type="button" class="archive-pagination-button" data-page-action="prev" ${archiveCurrentPage <= 1 ? "disabled" : ""}>Previous</button>
            <button type="button" class="archive-pagination-button" data-page-action="next" ${archiveCurrentPage >= pageCount ? "disabled" : ""}>Next</button>
        </div>
    `;

    container.querySelectorAll("[data-page-action]").forEach((button) => {
        button.addEventListener("click", () => {
            const action = button.getAttribute("data-page-action");
            if (action === "prev" && archiveCurrentPage > 1) {
                archiveCurrentPage -= 1;
                loadArchiveCatalog(false);
            }

            if (action === "next" && archiveCurrentPage < pageCount) {
                archiveCurrentPage += 1;
                loadArchiveCatalog(false);
            }
        });
    });
}

async function loadArchiveSessionDetail(sessionId, updateUrl = true) {
    selectedSessionId = sessionId;
    renderArchiveSessionList();
    renderArchiveDetailEmpty("Loading archived session...");

    try {
        const response = await fetch(`/api/archive/sessions/${encodeURIComponent(sessionId)}`, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        archiveDetail = await response.json();
        seedSelectedComparisonDrivers();
        renderArchiveDetail();
        if (updateUrl) {
            updateArchiveQueryString();
        }
    } catch (error) {
        console.error(error);
        renderArchiveDetailEmpty("Failed to load archived session details.");
    }
}

function seedSelectedComparisonDrivers() {
    selectedComparisonDriverIds.clear();
    getComparisonDrivers()
        .slice(0, 4)
        .forEach((driver) => selectedComparisonDriverIds.add(String(driver.playerId)));
}

function renderArchiveDetailEmpty(message) {
    const shell = document.getElementById("archive-detail-shell");
    if (!shell) {
        return;
    }

    shell.innerHTML = `
        <section class="session-info rounded-lg p-5 border archive-loading-panel">
            <div class="archive-empty-state archive-empty-state--large">${escapeHtml(message)}</div>
        </section>
    `;
}

function renderArchiveDetail() {
    const shell = document.getElementById("archive-detail-shell");
    if (!shell || !archiveDetail) {
        return;
    }

    const summary = archiveDetail.summary;
    const session = archiveDetail.session;

    shell.innerHTML = `
        <section class="session-info rounded-lg p-5 border archive-detail-hero mb-6">
            <div class="archive-detail-hero-top">
                <div>
                    <p class="setup-section-kicker">Session Detail</p>
                    <h2 class="archive-detail-title">${escapeHtml(summary.trackName || "Unknown")} • ${escapeHtml(summary.sessionType || "Unknown")}</h2>
                    <p class="archive-detail-meta">Started ${formatDateTime(summary.sessionStartedAtUtc)} • Archived ${formatDateTime(summary.archivedAtUtc)} • Trigger ${escapeHtml(summary.trigger || "-")}</p>
                </div>
                <div class="archive-detail-chip-row">
                    <span class="archive-detail-chip">${Number(summary.driverCount || 0)} drivers</span>
                    <span class="archive-detail-chip">${Number(summary.completedLaps || 0)} laps</span>
                    <span class="archive-detail-chip">Schema v${Number(summary.schemaVersion || 0)}</span>
                </div>
            </div>
            <div class="archive-summary-grid">
                <div class="archive-summary-card">
                    <p class="archive-summary-label">Session Best Lap</p>
                    <p class="archive-summary-value">${formatLapTime(archiveDetail.sessionBestLap?.lapTimeMs)}</p>
                    <p class="archive-summary-note">${formatArchiveDriverName(archiveDetail.sessionBestLap?.authorName, "No best lap recorded")}</p>
                </div>
                <div class="archive-summary-card">
                    <p class="archive-summary-label">Winner / Leader</p>
                    <p class="archive-summary-value">${formatArchiveDriverName(summary.winnerName, "-")}</p>
                    <p class="archive-summary-note">${escapeHtml(session.trackName || "Unknown")} • ${escapeHtml(session.weatherType || "Unknown")}</p>
                </div>
                <div class="archive-summary-card">
                    <p class="archive-summary-label">Race Distance</p>
                    <p class="archive-summary-value">${Number(summary.completedLaps || 0)}</p>
                    <p class="archive-summary-note">Qualifying mins: ${Number(session.qualifyingMins || 0)} • Max laps: ${Number(session.maxRaceLaps || 0)}</p>
                </div>
                <div class="archive-summary-card">
                    <p class="archive-summary-label">Official Results</p>
                    <p class="archive-summary-value">${Number(summary.officialResultsCount || 0)}</p>
                    <p class="archive-summary-note">${buildBonusSummaryNote(archiveDetail.officialResults || [], summary.officialResultsCount > 0)}</p>
                </div>
            </div>
        </section>

        <section class="session-info rounded-lg p-5 border mb-6">
            <div class="archive-section-header">
                <div>
                    <p class="setup-section-kicker">Sector Benchmarks</p>
                    <h3 class="setup-section-title">Session best sectors</h3>
                </div>
                <p class="setup-section-note">Fastest split in each sector across the archived session.</p>
            </div>
            <div class="archive-sector-grid" id="archive-sector-grid"></div>
        </section>

        <section class="session-info rounded-lg p-5 border mb-6">
            <div class="archive-section-header">
                <div>
                    <p class="setup-section-kicker">Comparison</p>
                    <h3 class="setup-section-title">Lap time overlay</h3>
                </div>
                <p class="setup-section-note">Select archived drivers and compare their lap times on one chart.</p>
            </div>
            <div class="archive-comparison-selector" id="archive-comparison-selector"></div>
            <div class="archive-chart-shell">
                <div class="archive-chart-scroll">
                    <svg id="archive-lap-chart" class="archive-lap-chart" viewBox="0 0 920 320" role="img" aria-label="Archived lap comparison chart"></svg>
                </div>
                <div class="archive-chart-legend" id="archive-chart-legend"></div>
            </div>
        </section>

        <section class="session-info rounded-lg p-5 border mb-6">
            <div class="archive-section-header">
                <div>
                    <p class="setup-section-kicker">Standings Snapshot</p>
                    <h3 class="setup-section-title">Archived order and pace</h3>
                </div>
                <p class="setup-section-note">Best lap, best sectors, pit stop count, top speed and points split at archive time.</p>
            </div>
            <div class="archive-table-shell">
                <table class="archive-standings-table">
                    <thead>
                        <tr>
                            <th>Pos</th>
                            <th>Driver</th>
                            <th>Car</th>
                            <th>Laps</th>
                            <th>Best Lap</th>
                            <th>Sectors</th>
                            <th>Top Speed</th>
                            <th>Pits</th>
                            ${archiveDetail.officialResults?.length ? "<th>Points</th>" : ""}
                        </tr>
                    </thead>
                    <tbody id="archive-standings-body"></tbody>
                </table>
            </div>
        </section>

        <section class="session-info rounded-lg p-5 border ${archiveDetail.officialResults?.length ? "" : "archive-results-panel--empty"}">
            <div class="archive-section-header">
                <div>
                    <p class="setup-section-kicker">Official Results</p>
                    <h3 class="setup-section-title">Points and finish order</h3>
                </div>
                <p class="setup-section-note">When present, these entries come from archived official LFS results captured from IS_RES, including base points and active bonuses.</p>
            </div>
            <div id="archive-official-results"></div>
        </section>
    `;

    renderArchiveBestSectors();
    renderComparisonSelector();
    renderComparisonChart();
    renderArchiveStandings();
    renderOfficialResults();
}

function renderArchiveBestSectors() {
    const container = document.getElementById("archive-sector-grid");
    if (!container || !archiveDetail) {
        return;
    }

    const sectors = archiveDetail.sessionBestSectors || [];
    container.innerHTML = sectors.length === 0
        ? '<div class="archive-empty-state">No sector benchmarks recorded for this session.</div>'
        : sectors.map((sector) => `
            <div class="archive-sector-card">
                <p class="archive-sector-label">Sector ${Number(sector.sectorNumber || 0)}</p>
                <p class="archive-sector-time">${formatLapTime(sector.timeMs)}</p>
                <p class="archive-sector-driver">${formatArchiveDriverName(sector.authorName, "-")}</p>
            </div>
        `).join("");
}

function getComparisonDrivers() {
    if (!archiveDetail?.drivers) {
        return [];
    }

    return [...archiveDetail.drivers].sort((left, right) => {
        const leftBest = Number(left.personalBestLap?.lapTimeMs || Number.MAX_SAFE_INTEGER);
        const rightBest = Number(right.personalBestLap?.lapTimeMs || Number.MAX_SAFE_INTEGER);
        return leftBest - rightBest || String(left.name || "").localeCompare(String(right.name || ""));
    });
}

function renderComparisonSelector() {
    const container = document.getElementById("archive-comparison-selector");
    if (!container || !archiveDetail) {
        return;
    }

    const drivers = getComparisonDrivers();
    container.innerHTML = drivers.map((driver, index) => {
        const driverId = String(driver.playerId);
        const selected = selectedComparisonDriverIds.has(driverId);
        const color = getDriverSeriesColor(driver, index);
        return `
            <button type="button" class="archive-driver-chip${selected ? " is-active" : ""}" data-driver-id="${driverId}">
                <span class="archive-driver-chip-dot" style="background:${escapeHtml(color)}"></span>
                <span>${formatArchiveDriverName(driver.name, "Unknown")}</span>
            </button>
        `;
    }).join("");

    container.querySelectorAll("[data-driver-id]").forEach((button) => {
        button.addEventListener("click", () => {
            const driverId = String(button.getAttribute("data-driver-id") || "");
            if (!driverId) {
                return;
            }

            if (selectedComparisonDriverIds.has(driverId)) {
                selectedComparisonDriverIds.delete(driverId);
            } else {
                selectedComparisonDriverIds.add(driverId);
            }

            renderComparisonSelector();
            renderComparisonChart();
        });
    });
}

function renderComparisonChart() {
    const chart = document.getElementById("archive-lap-chart");
    const legend = document.getElementById("archive-chart-legend");
    if (!chart || !legend || !archiveDetail) {
        return;
    }

    const selectedDrivers = getComparisonDrivers().filter((driver) => selectedComparisonDriverIds.has(String(driver.playerId)));
    const lapNumbers = archiveDetail.availableLapNumbers || [];
    const allLapTimes = selectedDrivers
        .flatMap((driver) => (driver.lapHistory || []).map((lap) => Number(lap.lapTimeMs || 0)))
        .filter((lapTimeMs) => lapTimeMs > 0);

    if (selectedDrivers.length === 0 || lapNumbers.length === 0 || allLapTimes.length === 0) {
        chart.innerHTML = "";
        legend.innerHTML = '<div class="archive-empty-state">Select at least one driver with archived lap history to draw the chart.</div>';
        return;
    }

    const width = 920;
    const height = 320;
    const padding = { top: 24, right: 24, bottom: 48, left: 66 };
    const chartWidth = width - padding.left - padding.right;
    const chartHeight = height - padding.top - padding.bottom;
    const minLap = Math.min(...lapNumbers.map(Number));
    const maxLap = Math.max(...lapNumbers.map(Number));
    const minTime = Math.min(...allLapTimes);
    const maxTime = Math.max(...allLapTimes);
    const yPadding = Math.max(250, Math.round((maxTime - minTime) * 0.1));
    const yMin = Math.max(0, minTime - yPadding);
    const yMax = maxTime + yPadding;

    const getX = (lapNumber) => {
        if (maxLap === minLap) {
            return padding.left + chartWidth / 2;
        }

        return padding.left + ((lapNumber - minLap) / (maxLap - minLap)) * chartWidth;
    };

    const getY = (lapTimeMs) => padding.top + chartHeight - ((lapTimeMs - yMin) / Math.max(1, yMax - yMin)) * chartHeight;

    const yTicks = 5;
    const xAxis = lapNumbers.map((lapNumber) => `
        <text x="${getX(Number(lapNumber)).toFixed(1)}" y="${height - 14}" class="archive-chart-axis-label" text-anchor="middle">Lap ${Number(lapNumber)}</text>
    `).join("");

    const yAxis = Array.from({ length: yTicks }, (_, index) => {
        const value = yMin + ((yMax - yMin) / (yTicks - 1)) * index;
        const y = getY(value);
        return `
            <line x1="${padding.left}" y1="${y.toFixed(1)}" x2="${(width - padding.right)}" y2="${y.toFixed(1)}" class="archive-chart-grid-line"></line>
            <text x="${padding.left - 10}" y="${(y + 4).toFixed(1)}" class="archive-chart-axis-label" text-anchor="end">${escapeHtml(formatLapTime(Math.round(value)))}</text>
        `;
    }).join("");

    const seriesMarkup = selectedDrivers.map((driver, index) => {
        const points = (driver.lapHistory || [])
            .filter((lap) => Number(lap.lapTimeMs || 0) > 0)
            .sort((left, right) => Number(left.lapNumber || 0) - Number(right.lapNumber || 0));

        const color = getDriverSeriesColor(driver, index);
        const pathData = points
            .map((lap, pointIndex) => `${pointIndex === 0 ? "M" : "L"} ${getX(Number(lap.lapNumber || 0)).toFixed(1)} ${getY(Number(lap.lapTimeMs || 0)).toFixed(1)}`)
            .join(" ");

        const dots = points.map((lap) => `
            <circle cx="${getX(Number(lap.lapNumber || 0)).toFixed(1)}" cy="${getY(Number(lap.lapTimeMs || 0)).toFixed(1)}" r="4.5" fill="${escapeHtml(color)}"></circle>
        `).join("");

        return `
            <path d="${pathData}" fill="none" stroke="${escapeHtml(color)}" stroke-width="3" stroke-linejoin="round" stroke-linecap="round"></path>
            ${dots}
        `;
    }).join("");

    chart.innerHTML = `
        <rect x="0" y="0" width="${width}" height="${height}" rx="24" class="archive-chart-background"></rect>
        ${yAxis}
        <line x1="${padding.left}" y1="${height - padding.bottom}" x2="${width - padding.right}" y2="${height - padding.bottom}" class="archive-chart-axis-line"></line>
        <line x1="${padding.left}" y1="${padding.top}" x2="${padding.left}" y2="${height - padding.bottom}" class="archive-chart-axis-line"></line>
        ${seriesMarkup}
        ${xAxis}
    `;

    legend.innerHTML = selectedDrivers.map((driver, index) => {
        const color = getDriverSeriesColor(driver, index);
        return `
            <div class="archive-chart-legend-item">
                <span class="archive-chart-legend-dot" style="background:${escapeHtml(color)}"></span>
                <div>
                    <p class="archive-chart-legend-name">${formatArchiveDriverName(driver.name, "Unknown")}</p>
                    <p class="archive-chart-legend-meta">Best ${formatLapTime(driver.personalBestLap?.lapTimeMs)} • ${(driver.lapHistory || []).length} laps</p>
                </div>
            </div>
        `;
    }).join("");
}

function renderArchiveStandings() {
    const body = document.getElementById("archive-standings-body");
    if (!body || !archiveDetail) {
        return;
    }

    const officialResultLookup = buildOfficialResultLookup(archiveDetail.officialResults || []);
    body.innerHTML = (archiveDetail.drivers || []).map((driver, index) => {
        const officialResult = findOfficialResultForDriver(driver, officialResultLookup);
        const displayPosition = officialResult?.finishPosition || driver.currentRacePosition || (index + 1);
        const pointsMarkup = officialResult
            ? `
                <div class="archive-points-cell">
                    <div class="archive-points-total">${officialResult.points?.totalPoints ?? 0} pts</div>
                    <div class="archive-points-note">Base ${officialResult.points?.positionPoints ?? 0}${Number(officialResult.points?.bonusPoints || 0) > 0 ? ` • Bonus +${officialResult.points?.bonusPoints ?? 0}` : ""}</div>
                    ${renderBonusPills(officialResult.points, true)}
                </div>
            `
            : "-";
        const sectorsMarkup = [1, 2, 3]
            .map((sectorNumber) => {
                const sector = (driver.personalBestSectors || []).find((entry) => Number(entry.sectorNumber) === sectorNumber);
                const sessionBest = (archiveDetail.sessionBestSectors || []).find((entry) => Number(entry.sectorNumber) === sectorNumber);
                const isSessionBest = sector && sessionBest && Number(sector.timeMs) === Number(sessionBest.timeMs);
                return `<span class="archive-sector-pill${isSessionBest ? " is-best" : ""}">S${sectorNumber} ${formatLapTime(sector?.timeMs)}</span>`;
            })
            .join("");

        return `
            <tr>
                <td>${Number(displayPosition)}</td>
                <td>
                    <div class="archive-driver-cell">
                        <span class="archive-driver-swatch" style="background:${escapeHtml(getDriverSeriesColor(driver, index))}"></span>
                        <div>
                            <div class="archive-driver-name">${formatArchiveDriverName(driver.name, "Unknown")}</div>
                            <div class="archive-driver-subtitle${driver.username ? "" : " archive-driver-subtitle--muted"}">${escapeHtml(driver.username || "No linked username")}</div>
                        </div>
                    </div>
                </td>
                <td>${escapeHtml(driver.carName || "-")}</td>
                <td>${Number(driver.lapsCompleted || 0)}</td>
                <td>${formatLapTime(driver.personalBestLap?.lapTimeMs)}</td>
                <td><div class="archive-sector-pill-row">${sectorsMarkup}</div></td>
                <td>${formatSpeed(driver.topSpeedKmh)}</td>
                <td>${Number(driver.pitStops || 0)}</td>
                ${archiveDetail.officialResults?.length ? `<td>${pointsMarkup}</td>` : ""}
            </tr>
        `;
    }).join("");
}

function renderOfficialResults() {
    const container = document.getElementById("archive-official-results");
    if (!container || !archiveDetail) {
        return;
    }

    const results = archiveDetail.officialResults || [];
    if (results.length === 0) {
        container.innerHTML = '<div class="archive-empty-state">No official results were archived for this session. Existing archive files for this session contain an empty official results list.</div>';
        return;
    }

    container.innerHTML = `
        <div class="archive-official-results-grid">
            ${results.map((result) => `
                <div class="archive-official-result-card">
                    <div class="archive-official-result-top">
                        <p class="archive-official-position">P${result.finishPosition ?? "-"}</p>
                        <p class="archive-official-points">${result.points?.totalPoints ?? 0} pts</p>
                    </div>
                    <p class="archive-official-driver">${formatArchiveDriverName(result.driverName || result.username, "Unknown")}</p>
                    <p class="archive-official-car">${escapeHtml(result.carName || "-")}</p>
                    <div class="archive-official-points-breakdown">
                        <div class="archive-points-breakdown-row">
                            <span class="archive-points-breakdown-label">Base</span>
                            <span class="archive-points-breakdown-value">${result.points?.positionPoints ?? 0} pts</span>
                            ${Number(result.points?.bonusPoints || 0) > 0 ? `<span class="archive-points-breakdown-label">Bonus</span><span class="archive-points-breakdown-value">+${result.points?.bonusPoints ?? 0}</span>` : ""}
                        </div>
                        ${renderBonusPills(result.points, false)}
                    </div>
                    <div class="archive-official-stats">
                        <span>Best ${formatLapTime(result.bestLapTimeMs)}</span>
                        <span>${Number(result.numStops || 0)} stops</span>
                        <span>${Number(result.lapsDone || 0)} laps</span>
                    </div>
                </div>
            `).join("")}
        </div>
    `;
}

function getDriverSeriesColor(driver, fallbackIndex) {
    const rawColor = String(driver?.driverColor || "").trim();
    if (rawColor && rawColor !== "#9CA3AF") {
        return rawColor;
    }

    return chartPalette[fallbackIndex % chartPalette.length];
}

function formatLapTime(value) {
    const totalMs = Number(value || 0);
    if (!Number.isFinite(totalMs) || totalMs <= 0) {
        return "-";
    }

    const minutes = Math.floor(totalMs / 60000);
    const seconds = Math.floor((totalMs % 60000) / 1000);
    const milliseconds = totalMs % 1000;
    return `${minutes}:${String(seconds).padStart(2, "0")}.${String(milliseconds).padStart(3, "0")}`;
}

function formatSpeed(value) {
    const speed = Number(value || 0);
    return speed > 0 ? `${speed.toFixed(1)} km/h` : "-";
}

function formatDateTime(value) {
    if (!value) {
        return "-";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return "-";
    }

    return date.toLocaleString([], {
        year: "numeric",
        month: "short",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit"
    });
}

function escapeHtml(value) {
    return String(value ?? "")
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

function formatArchiveDriverName(value, fallback = "-") {
    const rawValue = String(value || "").trim();
    if (!rawValue) {
        return escapeHtml(fallback);
    }

    if (/<span\b/i.test(rawValue)) {
        return rawValue;
    }

    return convertLfsTextToHtml(rawValue) || escapeHtml(rawValue);
}

function buildOfficialResultLookup(results) {
    return {
        byPlayerId: new Map(results.filter((result) => Number.isFinite(Number(result.playerId))).map((result) => [String(result.playerId), result])),
        byUsername: new Map(results
            .filter((result) => String(result.username || "").trim())
            .map((result) => [String(result.username || "").toLowerCase(), result]))
    };
}

function renderBonusPills(points, subtle) {
    const pills = [];
    if (Number(points?.polePositionBonusPoints || 0) > 0) {
        pills.push(renderBonusPill(`Pole +${points.polePositionBonusPoints}`, subtle));
    }
    if (Number(points?.fastestLapBonusPoints || 0) > 0) {
        pills.push(renderBonusPill(`Fastest Lap +${points.fastestLapBonusPoints}`, subtle));
    }
    if (Number(points?.highestClimberBonusPoints || 0) > 0) {
        pills.push(renderBonusPill(`Highest Climber +${points.highestClimberBonusPoints}`, subtle));
    }

    return pills.length
        ? `<div class="archive-bonus-pill-row">${pills.join("")}</div>`
        : "";
}

function renderBonusPill(label, subtle) {
    return `<span class="archive-bonus-pill${subtle ? " archive-bonus-pill--subtle" : ""}">${escapeHtml(label)}</span>`;
}

function buildBonusSummaryNote(results, hasOfficialResults) {
    if (!hasOfficialResults) {
        return "Session snapshot only";
    }

    const bonusAwardCount = (results || []).reduce((count, result) => {
        const points = result?.points;
        return count
            + (Number(points?.polePositionBonusPoints || 0) > 0 ? 1 : 0)
            + (Number(points?.fastestLapBonusPoints || 0) > 0 ? 1 : 0)
            + (Number(points?.highestClimberBonusPoints || 0) > 0 ? 1 : 0);
    }, 0);

    return bonusAwardCount > 0
        ? `Authoritative order available • ${bonusAwardCount} bonus award${bonusAwardCount === 1 ? "" : "s"}`
        : "Authoritative order available";
}

function findOfficialResultForDriver(driver, lookup) {
    if (!lookup) {
        return null;
    }

    const playerIdKey = String(driver?.playerId ?? "");
    if (lookup.byPlayerId.has(playerIdKey)) {
        return lookup.byPlayerId.get(playerIdKey) || null;
    }

    const usernameKey = String(driver?.username || "").toLowerCase();
    if (usernameKey && lookup.byUsername.has(usernameKey)) {
        return lookup.byUsername.get(usernameKey) || null;
    }

    return null;
}