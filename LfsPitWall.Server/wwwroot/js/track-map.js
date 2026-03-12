window.TrackMapController = (() => {
    let trackMapResizeBound = false;
    let trackMapInteractionBound = false;
    let trackMapAnimationFrameId = null;
    let lastTrackMapSnapshotAtMs = 0;
    let trackMapInterpolationDurationMs = 200;
    let trackMapStableBounds = null;
    let getLatestSessionData = () => null;
    let getHoveredDriverId = () => null;
    let setHoveredDriverId = () => {};
    let lastRenderedDriverMarkers = [];
    const trackMapMotionState = new Map();

    const TRACK_MAP_DEFAULT_SNAPSHOT_MS = 200;
    const TRACK_MAP_MIN_INTERPOLATION_MS = 180;
    const TRACK_MAP_MAX_INTERPOLATION_MS = 520;
    const TRACK_MAP_INTERPOLATION_MULTIPLIER = 2.1;
    const TRACK_MAP_EXTRAPOLATION_MULTIPLIER = 1.1;
    const TRACK_MAP_MAX_EXTRAPOLATION_MS = 240;
    const TRACK_MAP_VELOCITY_BLEND = 0.58;
    const TRACK_MAP_MIN_JUMP_LIMIT_WORLD = 60 * 65536;
    const TRACK_MAP_MAX_JUMP_RATIO = 0.12;
    const TRACK_MAP_MIN_SEGMENT_POINT_DISTANCE_WORLD = 1600;
    const TRACK_MAP_MAX_SEGMENT_POINT_DISTANCE_WORLD = 12000;
    const TRACK_MAP_LINE_SMOOTHING_PASSES = 2;

    function initialize(options = {}) {
        if (typeof options.getLatestSessionData === "function") {
            getLatestSessionData = options.getLatestSessionData;
        }

        if (typeof options.getHoveredDriverId === "function") {
            getHoveredDriverId = options.getHoveredDriverId;
        }

        if (typeof options.setHoveredDriverId === "function") {
            setHoveredDriverId = options.setHoveredDriverId;
        }

        bindTrackMapInteractions();

        if (trackMapResizeBound) {
            return;
        }

        window.addEventListener("resize", () => {
            const latestSessionData = getLatestSessionData();
            if (latestSessionData) {
                renderTrackMap(latestSessionData, performance.now());
            }
        });

        trackMapResizeBound = true;
    }

    function handleSessionUpdate(data) {
        syncTrackMapMotion(data);
        renderTrackMapLegend(data);
    }

    function bindTrackMapInteractions() {
        if (trackMapInteractionBound) {
            return;
        }

        const canvas = document.getElementById("track-map-canvas");
        if (canvas) {
            canvas.addEventListener("mousemove", (event) => {
                const rect = canvas.getBoundingClientRect();
                const x = event.clientX - rect.left;
                const y = event.clientY - rect.top;
                const hoveredMarker = [...lastRenderedDriverMarkers]
                    .reverse()
                    .find((marker) => Math.hypot(marker.x - x, marker.y - y) <= marker.hitRadius);

                setHoveredDriverId(hoveredMarker?.driverId || null);
            });

            canvas.addEventListener("mouseleave", () => {
                setHoveredDriverId(null);
            });
        }

        const legend = document.getElementById("track-map-legend");
        if (legend) {
            legend.addEventListener("mousemove", (event) => {
                const entry = event.target.closest("[data-driver-id]");
                setHoveredDriverId(entry?.dataset.driverId || null);
            });

            legend.addEventListener("mouseleave", () => {
                setHoveredDriverId(null);
            });
        }

        trackMapInteractionBound = true;
    }

    function ensureTrackMapCanvasSize(canvas, context) {
        const pixelRatio = window.devicePixelRatio || 1;
        const cssWidth = Math.max(1, Math.floor(canvas.clientWidth));
        const cssHeight = Math.max(1, Math.floor(canvas.clientHeight));
        const targetWidth = Math.max(1, Math.floor(cssWidth * pixelRatio));
        const targetHeight = Math.max(1, Math.floor(cssHeight * pixelRatio));

        if (canvas.width !== targetWidth || canvas.height !== targetHeight) {
            canvas.width = targetWidth;
            canvas.height = targetHeight;
        }

        context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);

        return { width: cssWidth, height: cssHeight }; 
    }

    function getTrackMapDiagonal(trackMap) {
        if (!trackMap) {
            return 0;
        }

        const minX = Number(trackMap.minX);
        const maxX = Number(trackMap.maxX);
        const minY = Number(trackMap.minY);
        const maxY = Number(trackMap.maxY);

        if (![minX, maxX, minY, maxY].every(Number.isFinite)) {
            return 0;
        }

        return Math.hypot(maxX - minX, maxY - minY);
    }

    function getTrackMapMotionPosition(motionState, nowMs, allowExtrapolation = true) {
        const interpolationDurationMs = Math.max(
            1,
            Number(motionState?.interpolationDurationMs) || trackMapInterpolationDurationMs || TRACK_MAP_DEFAULT_SNAPSHOT_MS
        );
        const elapsedMs = Math.max(0, nowMs - Number(motionState?.startedAtMs || nowMs));
        const progress = Math.max(0, Math.min(1, elapsedMs / interpolationDurationMs));
        const easedProgress = 1 - Math.pow(1 - progress, 3);

        let x = motionState.startX + ((motionState.targetX - motionState.startX) * easedProgress);
        let y = motionState.startY + ((motionState.targetY - motionState.startY) * easedProgress);

        if (allowExtrapolation && elapsedMs > interpolationDurationMs) {
            const snapshotIntervalMs = Math.max(
                1,
                Number(motionState?.snapshotIntervalMs) || TRACK_MAP_DEFAULT_SNAPSHOT_MS
            );
            const extrapolationLimitMs = Math.min(
                TRACK_MAP_MAX_EXTRAPOLATION_MS,
                snapshotIntervalMs * TRACK_MAP_EXTRAPOLATION_MULTIPLIER
            );
            const extrapolationMs = Math.max(0, Math.min(elapsedMs - interpolationDurationMs, extrapolationLimitMs));

            if (extrapolationMs > 0) {
                const decay = 1 - (extrapolationMs / Math.max(1, extrapolationLimitMs)) * 0.35;
                x += (motionState.velocityX || 0) * extrapolationMs * decay;
                y += (motionState.velocityY || 0) * extrapolationMs * decay;
            }
        }

        return { x, y };
    }

    function syncTrackMapMotion(data) {
        const now = performance.now();
        const timeSinceLastSnapshotMs = lastTrackMapSnapshotAtMs > 0
            ? now - lastTrackMapSnapshotAtMs
            : TRACK_MAP_DEFAULT_SNAPSHOT_MS;

        const trackDiagonal = getTrackMapDiagonal(data?.trackMap);
        const maxJumpDistance = Math.max(TRACK_MAP_MIN_JUMP_LIMIT_WORLD, trackDiagonal * TRACK_MAP_MAX_JUMP_RATIO);

        trackMapInterpolationDurationMs = Math.max(
            TRACK_MAP_MIN_INTERPOLATION_MS,
            Math.min(TRACK_MAP_MAX_INTERPOLATION_MS, timeSinceLastSnapshotMs * TRACK_MAP_INTERPOLATION_MULTIPLIER)
        );
        lastTrackMapSnapshotAtMs = now;

        const visibleDriverIds = new Set();
        const drivers = Array.isArray(data?.players) ? data.players : [];
        drivers.forEach((driver) => {
            if (!driver.hasWorldPosition || !Number.isFinite(driver.mapX) || !Number.isFinite(driver.mapY)) {
                return;
            }

            const driverId = String(driver.playerId);
            visibleDriverIds.add(driverId);

            const existingState = trackMapMotionState.get(driverId);
            const nextX = Number(driver.mapX);
            const nextY = Number(driver.mapY);

            if (!existingState) {
                trackMapMotionState.set(driverId, {
                    currentX: nextX,
                    currentY: nextY,
                    startX: nextX,
                    startY: nextY,
                    targetX: nextX,
                    targetY: nextY,
                    velocityX: 0,
                    velocityY: 0,
                    snapshotIntervalMs: timeSinceLastSnapshotMs,
                    interpolationDurationMs: trackMapInterpolationDurationMs,
                    startedAtMs: now,
                    updatedAtMs: now
                });
                return;
            }

            const interpolatedPosition = getTrackMapMotionPosition(existingState, now);
            existingState.currentX = interpolatedPosition.x;
            existingState.currentY = interpolatedPosition.y;

            let clampedTargetX = nextX;
            let clampedTargetY = nextY;
            const jumpDistance = Math.hypot(nextX - existingState.targetX, nextY - existingState.targetY);
            if (jumpDistance > maxJumpDistance && maxJumpDistance > 0) {
                const jumpScale = maxJumpDistance / jumpDistance;
                clampedTargetX = existingState.targetX + ((nextX - existingState.targetX) * jumpScale);
                clampedTargetY = existingState.targetY + ((nextY - existingState.targetY) * jumpScale);
            }

            const snapshotIntervalMs = Math.max(1, timeSinceLastSnapshotMs);
            const nextVelocityX = (clampedTargetX - existingState.targetX) / snapshotIntervalMs;
            const nextVelocityY = (clampedTargetY - existingState.targetY) / snapshotIntervalMs;

            existingState.startX = existingState.currentX;
            existingState.startY = existingState.currentY;
            existingState.targetX = clampedTargetX;
            existingState.targetY = clampedTargetY;
            existingState.velocityX = ((existingState.velocityX || 0) * (1 - TRACK_MAP_VELOCITY_BLEND)) + (nextVelocityX * TRACK_MAP_VELOCITY_BLEND);
            existingState.velocityY = ((existingState.velocityY || 0) * (1 - TRACK_MAP_VELOCITY_BLEND)) + (nextVelocityY * TRACK_MAP_VELOCITY_BLEND);
            existingState.snapshotIntervalMs = snapshotIntervalMs;
            existingState.interpolationDurationMs = trackMapInterpolationDurationMs;
            existingState.startedAtMs = now;
            existingState.updatedAtMs = now;
        });

        Array.from(trackMapMotionState.keys()).forEach((driverId) => {
            if (!visibleDriverIds.has(driverId)) {
                trackMapMotionState.delete(driverId);
            }
        });

        ensureTrackMapAnimation();
    }

    function ensureTrackMapAnimation() {
        if (trackMapAnimationFrameId !== null) {
            return;
        }

        const tick = () => {
            trackMapAnimationFrameId = null;
            const latestSessionData = getLatestSessionData();
            if (!latestSessionData) {
                return;
            }

            renderTrackMap(latestSessionData, performance.now());
            ensureTrackMapAnimation();
        };

        trackMapAnimationFrameId = window.requestAnimationFrame(tick);
    }

    function getAnimatedTrackDrivers(data, nowMs) {
        const drivers = Array.isArray(data?.players) ? data.players : [];

        return drivers
            .filter((driver) => driver.hasWorldPosition && Number.isFinite(driver.mapX) && Number.isFinite(driver.mapY))
            .map((driver) => {
                const driverId = String(driver.playerId);
                const motionState = trackMapMotionState.get(driverId);

                if (!motionState) {
                    return driver;
                }

                const animatedPosition = getTrackMapMotionPosition(motionState, nowMs);

                motionState.currentX = animatedPosition.x;
                motionState.currentY = animatedPosition.y;

                return {
                    ...driver,
                    mapX: animatedPosition.x,
                    mapY: animatedPosition.y
                };
            });
    }

    function getTrackPointDistance(left, right) {
        const deltaX = Number(right.x) - Number(left.x);
        const deltaY = Number(right.y) - Number(left.y);
        return Math.hypot(deltaX, deltaY);
    }

    function cloneTrackPoint(point) {
        return {
            node: point.node,
            x: Number(point.x),
            y: Number(point.y)
        };
    }

    function simplifyTrackSegment(segment, minimumDistance) {
        if (!Array.isArray(segment) || segment.length <= 2) {
            return Array.isArray(segment) ? segment.map(cloneTrackPoint) : [];
        }

        const simplified = [cloneTrackPoint(segment[0])];
        for (let index = 1; index < segment.length - 1; index++) {
            const currentPoint = segment[index];
            const previousPoint = simplified[simplified.length - 1];
            if (getTrackPointDistance(previousPoint, currentPoint) >= minimumDistance) {
                simplified.push(cloneTrackPoint(currentPoint));
            }
        }

        simplified.push(cloneTrackPoint(segment[segment.length - 1]));
        return simplified;
    }

    function smoothTrackSegment(segment, passes) {
        let smoothed = segment.map(cloneTrackPoint);
        for (let passIndex = 0; passIndex < passes; passIndex++) {
            if (smoothed.length < 3) {
                break;
            }

            const next = [smoothed[0]];
            for (let index = 0; index < smoothed.length - 1; index++) {
                const left = smoothed[index];
                const right = smoothed[index + 1];
                next.push({
                    node: left.node,
                    x: (left.x * 0.75) + (right.x * 0.25),
                    y: (left.y * 0.75) + (right.y * 0.25)
                });
                next.push({
                    node: right.node,
                    x: (left.x * 0.25) + (right.x * 0.75),
                    y: (left.y * 0.25) + (right.y * 0.75)
                });
            }
            next.push(smoothed[smoothed.length - 1]);
            smoothed = next;
        }

        return smoothed;
    }

    function finalizeTrackSegment(segment, medianDistance) {
        if (!Array.isArray(segment) || segment.length < 3) {
            return [];
        }

        const minimumDistance = Math.max(
            TRACK_MAP_MIN_SEGMENT_POINT_DISTANCE_WORLD,
            Math.min(TRACK_MAP_MAX_SEGMENT_POINT_DISTANCE_WORLD, medianDistance * 0.45)
        );
        const simplifiedSegment = simplifyTrackSegment(segment, minimumDistance);
        const smoothedSegment = smoothTrackSegment(simplifiedSegment, TRACK_MAP_LINE_SMOOTHING_PASSES);
        return smoothedSegment.length >= 3 ? smoothedSegment : simplifiedSegment;
    }

    function buildTrackMapGeometry(trackPoints) {
        if (!Array.isArray(trackPoints) || trackPoints.length < 2) {
            return { segments: [], boundsPoints: [], rawPointCount: 0 };
        }

        const distances = [];
        for (let index = 1; index < trackPoints.length; index++) {
            distances.push(getTrackPointDistance(trackPoints[index - 1], trackPoints[index]));
        }

        if (distances.length === 0) {
            return { segments: [], boundsPoints: [], rawPointCount: trackPoints.length };
        }

        const sortedDistances = [...distances].sort((left, right) => left - right);
        const medianDistance = sortedDistances[Math.floor(sortedDistances.length / 2)] || 1;
        const breakThreshold = Math.max(medianDistance * 3.5, 8000);
        const segments = [];
        let currentSegment = [trackPoints[0]];

        for (let index = 1; index < trackPoints.length; index++) {
            const currentPoint = trackPoints[index];
            const previousPoint = trackPoints[index - 1];
            const distance = getTrackPointDistance(previousPoint, currentPoint);

            if (distance > breakThreshold) {
                const finalizedSegment = finalizeTrackSegment(currentSegment, medianDistance);
                if (finalizedSegment.length >= 3) {
                    segments.push(finalizedSegment);
                }

                currentSegment = [currentPoint];
                continue;
            }

            currentSegment.push(currentPoint);
        }

        const finalizedSegment = finalizeTrackSegment(currentSegment, medianDistance);
        if (finalizedSegment.length >= 3) {
            segments.push(finalizedSegment);
        }

        return {
            segments,
            boundsPoints: segments.flat(),
            rawPointCount: trackPoints.length
        };
    }

    function getTrackOnlyBounds(boundsPoints) {
        if (!Array.isArray(boundsPoints) || boundsPoints.length === 0) {
            return null;
        }

        return {
            minX: Math.min(...boundsPoints.map((point) => Number(point.x))),
            maxX: Math.max(...boundsPoints.map((point) => Number(point.x))),
            minY: Math.min(...boundsPoints.map((point) => Number(point.y))),
            maxY: Math.max(...boundsPoints.map((point) => Number(point.y)))
        };
    }

    function getSmoothedTrackBounds(nextBounds) {
        if (!nextBounds) {
            return null;
        }

        if (!trackMapStableBounds) {
            trackMapStableBounds = { ...nextBounds };
            return trackMapStableBounds;
        }

        const smoothing = 0.14;
        trackMapStableBounds.minX += (nextBounds.minX - trackMapStableBounds.minX) * smoothing;
        trackMapStableBounds.maxX += (nextBounds.maxX - trackMapStableBounds.maxX) * smoothing;
        trackMapStableBounds.minY += (nextBounds.minY - trackMapStableBounds.minY) * smoothing;
        trackMapStableBounds.maxY += (nextBounds.maxY - trackMapStableBounds.maxY) * smoothing;

        return trackMapStableBounds;
    }

    function getTrackMapViewBounds(trackGeometry, drivers) {
        const trackBounds = getTrackOnlyBounds(trackGeometry?.boundsPoints || []);
        const driverBounds = (() => {
            const allX = [];
            const allY = [];

            drivers.forEach((driver) => {
                if (Number.isFinite(driver.mapX) && Number.isFinite(driver.mapY)) {
                    allX.push(Number(driver.mapX));
                    allY.push(Number(driver.mapY));
                }
            });

            if (allX.length === 0 || allY.length === 0) {
                return null;
            }

            return {
                minX: Math.min(...allX),
                maxX: Math.max(...allX),
                minY: Math.min(...allY),
                maxY: Math.max(...allY)
            };
        })();

        let minX = trackBounds?.minX ?? driverBounds?.minX;
        let maxX = trackBounds?.maxX ?? driverBounds?.maxX;
        let minY = trackBounds?.minY ?? driverBounds?.minY;
        let maxY = trackBounds?.maxY ?? driverBounds?.maxY;

        if (minX == null || maxX == null || minY == null || maxY == null) {
            return null;
        }

        if (!trackBounds && driverBounds) {
            minX = driverBounds.minX;
            maxX = driverBounds.maxX;
            minY = driverBounds.minY;
            maxY = driverBounds.maxY;
        }

        if (minX === maxX) {
            minX -= 1;
            maxX += 1;
        }

        if (minY === maxY) {
            minY -= 1;
            maxY += 1;
        }

        return { minX, maxX, minY, maxY };
    }

    function getTrackLegendStatus(driver) {
        switch (driver?.pitStatus) {
            case "service":
                return "In service";
            case "lane":
                return "Pit lane";
            case "drive-through":
                return "Drive-through";
            case "stop-go":
                return "Stop-go";
            default:
                return `Lap ${Number(driver?.lapsCompleted || 0)}`;
        }
    }

    function renderTrackMapLegend(data) {
        const legend = document.getElementById("track-map-legend");
        if (!legend) {
            return;
        }

        const drivers = Array.isArray(data?.players) ? data.players : [];
        const hoveredDriverId = getHoveredDriverId();

        if (drivers.length === 0) {
            legend.innerHTML = '<div class="track-map-legend-empty">Waiting for drivers...</div>';
            return;
        }

        legend.innerHTML = drivers.map((driver, index) => {
            const driverId = String(driver.playerId);
            const isHovered = hoveredDriverId === driverId;
            const driverColor = driver.driverColor || "#cbd5e1";
            const driverName = driver.nameHtml || driver.name || "Unknown driver";
            const meta = driver.carName || "-";
            const status = getTrackLegendStatus(driver);

            return `
                <div class="track-map-legend-entry${isHovered ? ' is-hovered' : ''}" data-driver-id="${driverId}">
                    <span class="track-map-legend-position">P${index + 1}</span>
                    <span class="track-map-legend-dot" style="background:${driverColor}"></span>
                    <div class="track-map-legend-name">
                        <span class="track-map-legend-driver">${driverName}</span>
                        <span class="track-map-legend-meta">${meta}</span>
                    </div>
                    <span class="track-map-legend-status">${status}</span>
                </div>`;
        }).join("");
    }

    function renderTrackMap(data, nowMs = performance.now()) {
        const canvas = document.getElementById("track-map-canvas");
        const emptyState = document.getElementById("track-map-empty");
        const statusElement = document.getElementById("track-map-status");
        const hintElement = document.getElementById("track-map-hint");
        if (!canvas || !emptyState || !statusElement || !hintElement) {
            return;
        }

        const context = canvas.getContext("2d");
        if (!context) {
            return;
        }

        const trackMap = data?.trackMap || null;
        const trackPoints = Array.isArray(trackMap?.points) ? trackMap.points : [];
        const trackGeometry = buildTrackMapGeometry(trackPoints);
        const drivers = getAnimatedTrackDrivers(data, nowMs);
        const hoveredDriverId = getHoveredDriverId();
        const size = ensureTrackMapCanvasSize(canvas, context);
        lastRenderedDriverMarkers = [];

        context.clearRect(0, 0, size.width, size.height);
        context.fillStyle = "rgba(15, 23, 42, 0.22)";
        context.fillRect(0, 0, size.width, size.height);

        statusElement.textContent = `${trackGeometry.rawPointCount} nodes • ${drivers.length} cars`;
        hintElement.textContent = trackGeometry.rawPointCount >= 12
            ? "Live outline is lightly smoothed from observed path nodes and split across discontinuities."
            : "Outline is still filling in as cars circulate around the track.";

        const rawViewBounds = getTrackMapViewBounds(trackGeometry, drivers);
        if (!rawViewBounds) {
            trackMapStableBounds = null;
        }

        const viewBounds = getSmoothedTrackBounds(rawViewBounds);
        if (!viewBounds || trackGeometry.segments.length === 0 || drivers.length === 0) {
            emptyState.style.display = "flex";
            return;
        }

        emptyState.style.display = "none";

        const padding = 24;
        const drawableWidth = Math.max(1, size.width - (padding * 2));
        const drawableHeight = Math.max(1, size.height - (padding * 2));
        const mapWidth = Math.max(1, viewBounds.maxX - viewBounds.minX);
        const mapHeight = Math.max(1, viewBounds.maxY - viewBounds.minY);
        const scale = Math.min(drawableWidth / mapWidth, drawableHeight / mapHeight);
        const offsetX = (size.width - (mapWidth * scale)) / 2;
        const offsetY = (size.height - (mapHeight * scale)) / 2;

        const toCanvasPoint = (x, y) => ({
            x: offsetX + ((Number(x) - viewBounds.minX) * scale),
            y: size.height - offsetY - ((Number(y) - viewBounds.minY) * scale)
        });

        context.strokeStyle = "rgba(148, 163, 184, 0.32)";
        context.lineWidth = 3;
        context.lineJoin = "round";
        context.lineCap = "round";
        trackGeometry.segments.forEach((segment) => {
            context.beginPath();
            segment.forEach((point, index) => {
                const canvasPoint = toCanvasPoint(point.x, point.y);
                if (index === 0) {
                    context.moveTo(canvasPoint.x, canvasPoint.y);
                } else {
                    context.lineTo(canvasPoint.x, canvasPoint.y);
                }
            });
            context.stroke();
        });

        const startPoint = trackGeometry.segments[0]?.[0];
        if (startPoint) {
            const canvasPoint = toCanvasPoint(startPoint.x, startPoint.y);
            context.fillStyle = "rgba(250, 204, 21, 0.95)";
            context.beginPath();
            context.arc(canvasPoint.x, canvasPoint.y, 5.5, 0, Math.PI * 2);
            context.fill();
        }

        const drawOrder = [...drivers].sort((left, right) => {
            const leftPosition = Number(left.currentRacePosition || 999);
            const rightPosition = Number(right.currentRacePosition || 999);
            return rightPosition - leftPosition;
        });

        drawOrder.forEach((driver) => {
            const point = toCanvasPoint(driver.mapX, driver.mapY);
            const isHovered = hoveredDriverId === String(driver.playerId);
            const radius = Number(driver.currentRacePosition) === 1 ? 8 : 6;
            const renderedRadius = isHovered ? radius + 2.5 : radius;
            const primaryColor = driver.driverColor || "#cbd5e1";
            const outlineColor = driver.pitStatus === "service"
                ? "#fbbf24"
                : driver.pitStatus === "lane"
                    ? "#38bdf8"
                    : "rgba(15, 23, 42, 0.95)";

            if (isHovered) {
                context.fillStyle = "rgba(248, 250, 252, 0.16)";
                context.beginPath();
                context.arc(point.x, point.y, renderedRadius + 5, 0, Math.PI * 2);
                context.fill();
            }

            context.fillStyle = primaryColor;
            context.beginPath();
            context.arc(point.x, point.y, renderedRadius, 0, Math.PI * 2);
            context.fill();

            context.lineWidth = isHovered ? 3 : 2;
            context.strokeStyle = outlineColor;
            context.stroke();

            lastRenderedDriverMarkers.push({
                driverId: String(driver.playerId),
                x: point.x,
                y: point.y,
                hitRadius: renderedRadius + 6
            });

            if (Number(driver.currentRacePosition) > 0 && Number(driver.currentRacePosition) <= 3) {
                context.fillStyle = "rgba(248, 250, 252, 0.95)";
                context.font = "11px Segoe UI";
                context.textAlign = "center";
                context.textBaseline = "bottom";
                context.fillText(`P${driver.currentRacePosition}`, point.x, point.y - renderedRadius - 6);
            }
        });

        renderTrackMapLegend(data);
    }

    return {
        initialize,
        handleSessionUpdate,
        render: renderTrackMap,
        setHoveredDriverId(driverId) {
            renderTrackMapLegend(getLatestSessionData());
            const latestSessionData = getLatestSessionData();
            if (latestSessionData) {
                renderTrackMap(latestSessionData, performance.now());
            }
        }
    };
})();