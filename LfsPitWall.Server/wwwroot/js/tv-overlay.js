const overlayElements = {
    progressTitle: document.getElementById("overlay-progress-title"),
    progressValue: document.getElementById("overlay-progress-value"),
    progressContext: document.getElementById("overlay-progress-context"),
    progressDetail: document.getElementById("overlay-progress-detail"),
    progressBar: document.getElementById("overlay-progress-bar"),
    rotationLabel: document.getElementById("overlay-rotation-label"),
    windowLabel: document.getElementById("overlay-window-label"),
    standingsList: document.getElementById("overlay-standings-list"),
    focusPanel: document.getElementById("overlay-focus-panel"),
    focusPosition: document.getElementById("overlay-focus-position"),
    focusLap: document.getElementById("overlay-focus-lap"),
    focusName: document.getElementById("overlay-focus-name"),
    focusBest: document.getElementById("overlay-focus-best"),
    sectorGrid: document.getElementById("overlay-sector-grid"),
    popupStack: document.getElementById("overlay-popup-stack"),
    connectionState: document.getElementById("overlay-connection-state")
};

function escapeHtml(value) {
    return String(value ?? "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/\"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

function setConnectionState(text, tone) {
    if (!overlayElements.connectionState) {
        return;
    }

    overlayElements.connectionState.textContent = text;
    overlayElements.connectionState.classList.remove("is-warning", "is-error");
    if (tone) {
        overlayElements.connectionState.classList.add(tone);
    }
}

function renderStandings(entries) {
    if (!overlayElements.standingsList) {
        return;
    }

    if (!entries?.length) {
        overlayElements.standingsList.innerHTML = '<div class="tv-overlay-empty-state">Waiting for live timing data...</div>';
        return;
    }

    const fragments = [];

    entries.forEach((entry, index) => {
        if (index === 3 && entries.some((candidate) => candidate.isFocused) && entries.length > 3) {
            fragments.push(`
                <div class="tv-overlay-divider" aria-hidden="true">
                    <span class="tv-overlay-divider-line"></span>
                    <span class="tv-overlay-divider-label">FIELD WINDOW</span>
                    <span class="tv-overlay-divider-line"></span>
                </div>`);
        }

        const pitFlags = [];
        if (entry.isInPit) {
            pitFlags.push('<span class="tv-overlay-flag is-pit">PIT</span>');
        }

        const battleBadge = entry.isBattling
            ? '<span class="tv-overlay-battle-badge">FIGHT</span>'
            : "";

        const deltaClass = entry.deltaText.startsWith("+")
            ? "is-positive"
            : entry.deltaText.startsWith("-")
                ? "is-negative"
                : "";

        fragments.push(`
            <article class="tv-overlay-entry${entry.isLeader ? " is-leader" : ""}${entry.isFocused ? " is-focused" : ""}">
                <div class="tv-overlay-pos">${entry.position}</div>
                <div class="tv-overlay-delta ${deltaClass}">${escapeHtml(entry.deltaText)}</div>
                <div class="tv-overlay-driver">
                    <div class="tv-overlay-driver-name">${entry.nameHtml}</div>
                    <div class="tv-overlay-driver-meta">${escapeHtml(entry.metaText)}</div>
                </div>
                <div class="tv-overlay-metric">${escapeHtml(entry.metricText)}</div>
                <div class="tv-overlay-flags">${pitFlags.join("")}</div>
                ${battleBadge}
            </article>`);
    });

    overlayElements.standingsList.innerHTML = fragments.join("");
}

function renderPopups(popups) {
    if (!overlayElements.popupStack) {
        return;
    }

    overlayElements.popupStack.innerHTML = (popups ?? []).map((popup) => `
        <article class="tv-overlay-popup is-${escapeHtml(popup.accentClass)}" data-popup-id="${escapeHtml(popup.id)}">
            <p class="tv-overlay-popup-title">${escapeHtml(popup.title)}</p>
            <p class="tv-overlay-popup-subject">${popup.subjectHtml}</p>
            <p class="tv-overlay-popup-detail">${popup.detailHtml || ""}</p>
        </article>`).join("");
}

function renderViewedDriver(viewedDriver) {
    if (!overlayElements.focusPanel || !overlayElements.focusPosition || !overlayElements.focusLap || !overlayElements.focusName || !overlayElements.focusBest || !overlayElements.sectorGrid) {
        return;
    }

    if (!viewedDriver?.sectors?.length) {
        overlayElements.focusPanel.hidden = true;
        overlayElements.focusPanel.classList.remove("is-tv-camera");
        overlayElements.sectorGrid.innerHTML = "";
        return;
    }

    overlayElements.focusPanel.hidden = false;
    overlayElements.focusPanel.classList.toggle("is-tv-camera", !!viewedDriver.isTvCamera);
    overlayElements.focusPosition.textContent = viewedDriver.positionText || "P-";
    overlayElements.focusLap.textContent = viewedDriver.currentLapText || "LAP -";
    overlayElements.focusName.innerHTML = viewedDriver.nameHtml || "-";
    overlayElements.focusBest.textContent = `BEST ${viewedDriver.bestLapText || "-"}`;
    overlayElements.sectorGrid.innerHTML = viewedDriver.sectors.map((sector) => `
        <article class="tv-overlay-sector-card is-${escapeHtml(sector.accentClass || "pending")}">
            <p class="tv-overlay-sector-heading">S${Number(sector.sectorNumber || 0)}${sector.referenceText ? ` <span class="tv-overlay-sector-reference">(${escapeHtml(sector.referenceText)})</span>` : ""}</p>
            <p class="tv-overlay-sector-value">${escapeHtml(sector.currentText || "--.---")}</p>
        </article>`).join("");
}

function renderOverlay(snapshot) {
    if (!snapshot) {
        return;
    }

    overlayElements.progressTitle.textContent = snapshot.progressTitle || "RACE PROGRESS";
    overlayElements.progressValue.textContent = snapshot.progressValue || "-";
    overlayElements.progressContext.textContent = `${snapshot.trackName || "Unknown"} • ${snapshot.sessionType || "Live"}`;
    overlayElements.progressDetail.textContent = snapshot.progressDetail || "Waiting for data";
    overlayElements.progressBar.style.width = `${Math.max(0, Math.min(100, Number(snapshot.progressRatio || 0) * 100))}%`;
    overlayElements.rotationLabel.textContent = snapshot.rotationLabel || "INTERVAL";
    overlayElements.windowLabel.textContent = snapshot.standingsWindowLabel || "FULL FIELD";
    renderViewedDriver(snapshot.viewedDriver);
    renderStandings(snapshot.entries);
    renderPopups(snapshot.popups);
}

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/timing")
    .withAutomaticReconnect()
    .build();

connection.on("ReceiveTvOverlayUpdate", renderOverlay);
connection.onreconnecting(() => setConnectionState("RECONNECTING", "is-warning"));
connection.onreconnected(async () => {
    setConnectionState("LIVE", "");
    await connection.invoke("JoinTvOverlay");
});
connection.onclose(() => setConnectionState("OFFLINE", "is-error"));

async function startOverlay() {
    try {
        setConnectionState("CONNECTING", "is-warning");
        await connection.start();
        await connection.invoke("JoinTvOverlay");
        setConnectionState("LIVE", "");
    } catch (error) {
        setConnectionState("RETRYING", "is-warning");
        window.setTimeout(startOverlay, 2000);
    }
}

startOverlay();