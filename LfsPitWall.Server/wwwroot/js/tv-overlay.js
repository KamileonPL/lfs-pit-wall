const overlayElements = {
    progressTitle: document.getElementById("overlay-progress-title"),
    progressValue: document.getElementById("overlay-progress-value"),
    progressContext: document.getElementById("overlay-progress-context"),
    progressDetail: document.getElementById("overlay-progress-detail"),
    progressBar: document.getElementById("overlay-progress-bar"),
    rotationLabel: document.getElementById("overlay-rotation-label"),
    windowLabel: document.getElementById("overlay-window-label"),
    standingsList: document.getElementById("overlay-standings-list"),
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

        const flags = [];
        if (entry.isBattling) {
            flags.push('<span class="tv-overlay-flag is-battle">FIGHT</span>');
        }
        if (entry.isInPit) {
            flags.push('<span class="tv-overlay-flag is-pit">PIT</span>');
        }

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
                <div class="tv-overlay-flags">${flags.join("")}</div>
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