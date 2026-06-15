const setupEditorState = {
    originalBytes: null,
    currentValues: {},
    fileName: "",
    signature: "",
    internalVersion: 0,
    formatVersion: 0,
    isPatchX: false,
    isDirty: false,
    activeSectionId: "brakes",
    selectedCarCode: "",
    gearTargetRpm: 7000
};

const tyreBrandOptions = ["Cromo Plain", "Cromo", "Torro", "Michelin", "Evostar"];
const tyreCompoundOptions = ["R1", "R2", "R3", "R4", "Road Super", "Road Normal", "Hybrid", "Knobbly"];
const centreDiffTypeOptions = ["Open", "Viscous"];
const axleDiffTypeOptions = ["Open", "Locked", "Viscous", "Clutch Pack"];
const passengerOptions = ["None", "Male", "Female", "Reserved"];
const bodyConfigOptions = ["Config 0", "Config 1", "Config 2", "Config 3"];
const tyreSizeIndexOptions = Array.from({ length: 10 }, (_, index) => `Index ${index}`);
const supportedCarProfiles = {
    UF1: { name: "UF 1000", drive: "front", frontTyreSpec: "160/50 R12", rearTyreSpec: "160/50 R12" },
    XFG: { name: "XF GTI", drive: "front", frontTyreSpec: "185/50 R15", rearTyreSpec: "185/50 R15" },
    XRG: { name: "XR GT", drive: "rear", frontTyreSpec: "185/55 R16", rearTyreSpec: "220/50 R16" },
    LX4: { name: "LX4", drive: "rear", frontTyreSpec: "195/60 R13", rearTyreSpec: "215/55 R13" },
    LX6: { name: "LX6", drive: "rear", frontTyreSpec: "205/55 R14", rearTyreSpec: "245/45 R14" },
    RB4: { name: "RB4 GT", drive: "all", frontTyreSpec: "215/40 R17", rearTyreSpec: "215/40 R17" },
    FXO: { name: "FXO Turbo", drive: "front", frontTyreSpec: "225/35 R17", rearTyreSpec: "225/35 R17" },
    XRT: { name: "XR GT Turbo", drive: "rear", frontTyreSpec: "225/45 R17", rearTyreSpec: "245/40 R17" },
    RAC: { name: "RaceAbout", drive: "rear", frontTyreSpec: "205/45 R17", rearTyreSpec: "225/45 R17" },
    FZ5: { name: "FZ50", drive: "rear", frontTyreSpec: "235/40 R18", rearTyreSpec: "295/30 R18" },
    UFR: { name: "UF GTR", drive: "front", frontTyreSpec: "215/45 R11", rearTyreSpec: "215/45 R11" },
    XFR: { name: "XF GTR", drive: "front", frontTyreSpec: "215/40 R15", rearTyreSpec: "215/40 R15" },
    FXR: { name: "FXO GTR", drive: "all", frontTyreSpec: "335/35 R18", rearTyreSpec: "335/35 R18" },
    XRR: { name: "XR GTR", drive: "rear", frontTyreSpec: "335/30 R19", rearTyreSpec: "335/35 R18" },
    FZR: { name: "FZ50 GTR", drive: "rear", frontTyreSpec: "300/35 R18", rearTyreSpec: "370/30 R18" },
    MRT: { name: "MRT5", drive: "rear", frontTyreSpec: "190/47 R13", rearTyreSpec: "190/47 R13" },
    FBM: { name: "Formula BMW FB02", drive: "rear", frontTyreSpec: "210/50 R13", rearTyreSpec: "260/40 R13" },
    FOX: { name: "Formula XR", drive: "rear", frontTyreSpec: "225/50 R13", rearTyreSpec: "285/45 R13" },
    FO8: { name: "Formula V8", drive: "rear", frontTyreSpec: "255/55 R13", rearTyreSpec: "360/40 R13" },
    BF1: { name: "BMW Sauber F1.06", drive: "rear", frontTyreSpec: "335/45 R13", rearTyreSpec: "375/40 R13" }
};
const supportedCarCodes = Object.keys(supportedCarProfiles);
const gearFieldIds = new Set(["gear1", "gear2", "gear3", "gear4", "gear5", "gear6", "gear7"]);

function clamp(value, min, max) {
    return Math.min(max, Math.max(min, value));
}

function roundToStep(value, step) {
    if (!step) {
        return value;
    }

    return Math.round(value / step) * step;
}

function sanitizeNumber(value, fallback = 0) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
}

function getStepPrecision(step) {
    if (!step || Number.isInteger(step)) {
        return 0;
    }

    const stepText = String(step);
    return stepText.includes(".") ? stepText.split(".")[1].length : 0;
}

function formatNumericValue(value, step = 1) {
    if (!Number.isFinite(value)) {
        return "";
    }

    const rounded = roundToStep(value, step);
    const precision = Math.min(getStepPrecision(step), 3);

    if (!precision) {
        return String(Math.round(rounded));
    }

    return rounded.toFixed(precision)
        .replace(/(\.\d*?[1-9])0+$/u, "$1")
        .replace(/\.0+$/u, "")
        .replace(/\.$/, "");
}

function createByteField(config) {
    const min = config.min ?? 0;
    const max = config.max ?? 255;
    const step = config.step ?? 1;
    const decode = config.decode ?? ((raw) => raw);
    const encode = config.encode ?? ((value) => value);

    return {
        ...config,
        kind: config.kind ?? "number",
        read(view) {
            return decode(view.getUint8(config.offset));
        },
        write(view, value) {
            const normalized = clamp(roundToStep(sanitizeNumber(value), step), min, max);
            const encoded = encode(normalized);
            view.setUint8(config.offset, clamp(Math.round(encoded), 0, 255));
        },
        normalize(value) {
            return clamp(roundToStep(sanitizeNumber(value), step), min, max);
        }
    };
}

function createWordField(config) {
    const min = config.min ?? 0;
    const max = config.max ?? 65535;
    const step = config.step ?? 1;
    const decode = config.decode ?? ((raw) => raw);
    const encode = config.encode ?? ((value) => value);

    return {
        ...config,
        kind: config.kind ?? "number",
        read(view) {
            return decode(view.getUint16(config.offset, true));
        },
        write(view, value) {
            const normalized = clamp(roundToStep(sanitizeNumber(value), step), min, max);
            const encoded = encode(normalized);
            view.setUint16(config.offset, clamp(Math.round(encoded), 0, 65535), true);
        },
        normalize(value) {
            return clamp(roundToStep(sanitizeNumber(value), step), min, max);
        }
    };
}

function createFloatField(config) {
    const min = config.min ?? 0;
    const max = config.max ?? 999999;
    const step = config.step ?? 0.1;

    return {
        ...config,
        kind: config.kind ?? "number",
        read(view) {
            return view.getFloat32(config.offset, true);
        },
        write(view, value) {
            const normalized = clamp(roundToStep(sanitizeNumber(value), step), min, max);
            view.setFloat32(config.offset, normalized, true);
        },
        normalize(value) {
            return clamp(roundToStep(sanitizeNumber(value), step), min, max);
        }
    };
}

function createBitField(config) {
    return {
        ...config,
        kind: "boolean",
        read(view) {
            return (view.getUint8(config.offset) & (1 << config.bit)) !== 0;
        },
        write(view, value) {
            const current = view.getUint8(config.offset);
            const bitMask = 1 << config.bit;
            const nextValue = value ? (current | bitMask) : (current & ~bitMask);
            view.setUint8(config.offset, nextValue);
        },
        normalize(value) {
            return Boolean(value);
        }
    };
}

function createPackedSelectField(config) {
    const min = config.min ?? 0;
    const max = config.max ?? ((config.options?.length ?? 1) - 1);
    const valueMask = config.valueMask ?? 0b11;

    return {
        ...config,
        kind: "select",
        read(view) {
            return (view.getUint8(config.offset) >> config.shift) & valueMask;
        },
        write(view, value) {
            const normalized = clamp(Math.round(sanitizeNumber(value)), min, max);
            const current = view.getUint8(config.offset);
            const shiftedMask = valueMask << config.shift;
            const encoded = (normalized & valueMask) << config.shift;
            view.setUint8(config.offset, (current & ~shiftedMask) | encoded);
        },
        normalize(value) {
            return clamp(Math.round(sanitizeNumber(value)), min, max);
        }
    };
}

function formatRatio(raw) {
    return 0.5 + ((raw / 65534) * 7);
}

function encodeRatio(value) {
    return ((value - 0.5) / 7) * 65534;
}

function formatToe(raw) {
    return (raw - 9) / 10;
}

function encodeToe(value) {
    return (value * 10) + 9;
}

function formatCamber(raw) {
    return (raw - 45) / 10;
}

function encodeCamber(value) {
    return (value * 10) + 45;
}

const allSetupFields = [
    createBitField({ id: "absEnabled", offset: 12, bit: 2, label: "ABS", description: "Brake anti-lock flag.", impact: "On: more lock control. Off: more direct braking." }),
    createBitField({ id: "tcEnabled", offset: 12, bit: 1, label: "Traction Control", description: "Traction control flag.", impact: "On: calmer exits. Off: sharper throttle response." }),
    createBitField({ id: "asymmetricalSetup", offset: 12, bit: 0, label: "Asymmetrical", description: "Allows left-right tuning differences.", impact: "On: side-specific tuning. Off: mirrored values." }),
    createByteField({ id: "massPosition", offset: 14, min: 0, max: 100, label: "Mass Position", unit: "%F", description: "Handicap mass position.", impact: "More front: steadier braking. More rear: better traction bias." }),
    createByteField({ id: "tyreBrand", offset: 15, kind: "select", options: tyreBrandOptions, min: 0, max: tyreBrandOptions.length - 1, label: "Tyre Brand", description: "Tyre manufacturer family.", impact: "Changes the tyre family stored in the file." }),
    createFloatField({ id: "brakeStrength", offset: 16, min: 0, max: 10000, step: 1, label: "Brake Strength", unit: "Nm", description: "Base brake torque.", impact: "More: shorter stops, more lock risk. Less: easier modulation." }),
    createByteField({ id: "rearWing", offset: 20, min: 0, max: 60, label: "Rear Wing", description: "Rear wing angle.", impact: "More: rear stability. Less: lower drag and weaker rear grip." }),
    createByteField({ id: "frontWing", offset: 21, min: 0, max: 60, label: "Front Wing", description: "Front wing angle.", impact: "More: front bite. Less: lower drag and calmer front axle." }),
    createByteField({ id: "voluntaryMass", offset: 22, min: 0, max: 255, label: "Voluntary Mass", unit: "kg", description: "Added ballast.", impact: "More: slower acceleration and longer braking." }),
    createByteField({ id: "intakeRestriction", offset: 23, min: 0, max: 100, label: "Intake Restriction", unit: "%", description: "Artificial engine restriction.", impact: "More: less power and top speed. Less: freer engine output." }),
    createByteField({ id: "steeringLock", offset: 24, min: 0, max: 90, label: "Max Steering Lock", unit: "deg", description: "Maximum front wheel angle.", impact: "More: tighter rotation. Less: calmer steering at speed." }),
    createByteField({ id: "parallelSteering", offset: 25, min: 0, max: 100, label: "Parallel Steering", unit: "%", description: "Ackermann relation.", impact: "More: wheels steer more equally. Less: stronger inside-wheel angle." }),
    createByteField({ id: "brakeBalance", offset: 26, min: 0, max: 100, label: "Brake Balance", unit: "%F", description: "Front brake bias.", impact: "More front: safer entry. More rear: more rotation, more rear lock risk." }),
    createByteField({ id: "centreDiffType", offset: 28, kind: "select", options: centreDiffTypeOptions, min: 0, max: centreDiffTypeOptions.length - 1, label: "Centre Diff Type", description: "Centre differential mode.", impact: "Changes front-rear axle coupling." }),
    createByteField({ id: "centreDiffViscousTorque", offset: 29, min: 0, max: 255, label: "Centre Viscous Torque", description: "Centre viscous locking.", impact: "More: stronger axle coupling. Less: freer front-rear speed split." }),
    createByteField({ id: "centreDiffTorqueSplit", offset: 31, min: 0, max: 100, label: "Centre Torque Split", unit: "%F", description: "Static front torque share.", impact: "More front: safer on throttle. More rear: stronger power rotation." }),
    createWordField({ id: "gear7", offset: 32, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 7", description: "Seventh ratio.", impact: "Longer: more speed. Shorter: more drive at the top end." }),
    createWordField({ id: "gearFinal", offset: 34, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Final Drive", description: "Final drive ratio.", impact: "Shorter: stronger acceleration. Longer: higher top speed." }),
    createWordField({ id: "gear1", offset: 36, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 1", description: "First ratio.", impact: "Shorter: stronger launch. Longer: less wheelspin." }),
    createWordField({ id: "gear2", offset: 38, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 2", description: "Second ratio.", impact: "Shorter: stronger low-speed drive. Longer: fewer shifts." }),
    createWordField({ id: "gear3", offset: 40, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 3", description: "Third ratio.", impact: "Shorter: more punch in medium-speed corners. Longer: wider range." }),
    createWordField({ id: "gear4", offset: 42, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 4", description: "Fourth ratio.", impact: "Shorter: stronger acceleration. Longer: lower rpm drop." }),
    createWordField({ id: "gear5", offset: 44, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 5", description: "Fifth ratio.", impact: "Shorter: stronger pull. Longer: more end-of-straight speed." }),
    createWordField({ id: "gear6", offset: 46, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 6", description: "Sixth ratio.", impact: "Shorter: better acceleration. Longer: lower drag-limited rpm." }),
    createPackedSelectField({ id: "passengerFrontRight", offset: 48, shift: 0, options: passengerOptions, label: "Front Right", description: "Front-right passenger slot.", impact: "Seat occupancy stored in the file." }),
    createPackedSelectField({ id: "passengerRearLeft", offset: 48, shift: 2, options: passengerOptions, label: "Rear Left", description: "Rear-left passenger slot.", impact: "Seat occupancy stored in the file." }),
    createPackedSelectField({ id: "passengerRearCenter", offset: 48, shift: 4, options: passengerOptions, label: "Rear Centre", description: "Rear-centre passenger slot.", impact: "Seat occupancy stored in the file." }),
    createPackedSelectField({ id: "passengerRearRight", offset: 48, shift: 6, options: passengerOptions, label: "Rear Right", description: "Rear-right passenger slot.", impact: "Seat occupancy stored in the file." }),
    createByteField({ id: "bodyConfig", offset: 49, kind: "select", options: bodyConfigOptions, min: 0, max: bodyConfigOptions.length - 1, label: "Body Config", description: "Vehicle-specific body option.", impact: "Changes roof or body variant on supported cars." }),
    createByteField({ id: "tcSlip", offset: 50, min: 0, max: 25.5, step: 0.1, decode: (raw) => raw / 10, encode: (value) => value * 10, label: "TC Slip", unit: "%", description: "Allowed slip before TC acts.", impact: "More: later TC intervention. Less: earlier traction control." }),
    createByteField({ id: "tcEngageSpeed", offset: 51, min: 0, max: 255, label: "TC Engage Speed", unit: "km/h", description: "Minimum speed for TC action.", impact: "Higher: TC waits longer. Lower: TC works earlier." }),
    createFloatField({ id: "rearRideHeight", offset: 52, min: 0, max: 300, step: 0.1, label: "Rear Ride Height", unit: "mm", description: "Rear chassis height.", impact: "Higher: more clearance. Lower: more response, more floor-strike risk." }),
    createFloatField({ id: "rearSpring", offset: 56, min: 0, max: 500, step: 0.1, label: "Rear Spring", unit: "N/mm", description: "Rear spring stiffness.", impact: "Stiffer: sharper platform, less traction. Softer: more compliance." }),
    createFloatField({ id: "rearBump", offset: 60, min: 0, max: 500, step: 0.1, label: "Rear Bump", unit: "N/mm", description: "Rear compression damping.", impact: "More: more support on compression. Less: more kerb compliance." }),
    createFloatField({ id: "rearRebound", offset: 64, min: 0, max: 500, step: 0.1, label: "Rear Rebound", unit: "N/mm", description: "Rear rebound damping.", impact: "More: slower rear extension. Less: freer traction recovery." }),
    createFloatField({ id: "rearArb", offset: 68, min: 0, max: 500, step: 0.1, label: "Rear ARB", unit: "N/mm", description: "Rear anti-roll bar.", impact: "Stiffer: more rotation. Softer: more rear grip on exit." }),
    createFloatField({ id: "handbrakeStrength", offset: 72, min: 0, max: 10000, step: 1, label: "Handbrake Strength", unit: "Nm", description: "Handbrake torque.", impact: "More: stronger rear lock. Less: weaker rotation aid." }),
    createByteField({ id: "rearToe", offset: 76, min: -0.9, max: 0.9, step: 0.1, decode: formatToe, encode: encodeToe, label: "Rear Toe", unit: "deg", description: "Rear toe setting.", impact: "More toe-in: steadier rear. More toe-out: more rotation." }),
    createByteField({ id: "rearTyreType", offset: 78, kind: "select", options: tyreCompoundOptions, min: 0, max: tyreCompoundOptions.length - 1, label: "Rear Compound", description: "Rear tyre compound.", impact: "Softer compounds grip more, wear and heat more." }),
    createByteField({ id: "rearCamberLeft", offset: 80, min: -4.5, max: 4.5, step: 0.1, decode: formatCamber, encode: encodeCamber, label: "Rear Left Camber", unit: "deg", description: "Rear-left camber.", impact: "More negative: more loaded-corner grip, less traction footprint." }),
    createByteField({ id: "rearCamberRight", offset: 81, min: -4.5, max: 4.5, step: 0.1, decode: formatCamber, encode: encodeCamber, label: "Rear Right Camber", unit: "deg", description: "Rear-right camber.", impact: "More negative: more loaded-corner grip, less traction footprint." }),
    createByteField({ id: "rearTyreSize", offset: 82, kind: "select", options: tyreSizeIndexOptions, min: 0, max: tyreSizeIndexOptions.length - 1, label: "Rear Size Index", description: "Rear tyre size index.", impact: "Higher: larger alternate tyre where supported." }),
    createByteField({ id: "rearDiffPreload", offset: 83, min: 0, max: 2550, step: 10, decode: (raw) => raw * 10, encode: (value) => value / 10, label: "Rear Preload", unit: "Nm", description: "Rear diff preload.", impact: "More: stronger initial locking. Less: freer off-throttle rotation." }),
    createByteField({ id: "rearDiffType", offset: 84, kind: "select", options: axleDiffTypeOptions, min: 0, max: axleDiffTypeOptions.length - 1, label: "Rear Diff Type", description: "Rear differential type.", impact: "Changes the base locking mechanism on the rear axle." }),
    createByteField({ id: "rearViscousTorque", offset: 85, min: 0, max: 255, label: "Rear Viscous Torque", description: "Rear viscous locking.", impact: "More: stronger rear coupling. Less: freer wheel speed split." }),
    createByteField({ id: "rearPowerLock", offset: 86, min: 0, max: 100, label: "Rear Power Lock", unit: "%", description: "Rear lock on throttle.", impact: "More: stronger exit lock. Less: freer rear on power." }),
    createByteField({ id: "rearCoastLock", offset: 87, min: 0, max: 100, label: "Rear Coast Lock", unit: "%", description: "Rear lock off-throttle.", impact: "More: steadier entry. Less: freer trail-brake rotation." }),
    createWordField({ id: "rearLeftPressure", offset: 88, min: 0, max: 400, step: 1, label: "Rear Left Pressure", unit: "kPa", description: "Rear-left pressure.", impact: "More: sharper response, smaller footprint. Less: more compliance." }),
    createWordField({ id: "rearRightPressure", offset: 90, min: 0, max: 400, step: 1, label: "Rear Right Pressure", unit: "kPa", description: "Rear-right pressure.", impact: "More: sharper response, smaller footprint. Less: more compliance." }),
    createFloatField({ id: "frontRideHeight", offset: 92, min: 0, max: 300, step: 0.1, label: "Front Ride Height", unit: "mm", description: "Front chassis height.", impact: "Higher: more clearance. Lower: more front response, more scrape risk." }),
    createFloatField({ id: "frontSpring", offset: 96, min: 0, max: 500, step: 0.1, label: "Front Spring", unit: "N/mm", description: "Front spring stiffness.", impact: "Stiffer: sharper direction change. Softer: more front compliance." }),
    createFloatField({ id: "frontBump", offset: 100, min: 0, max: 500, step: 0.1, label: "Front Bump", unit: "N/mm", description: "Front compression damping.", impact: "More: firmer support on entry load. Less: more kerb absorption." }),
    createFloatField({ id: "frontRebound", offset: 104, min: 0, max: 500, step: 0.1, label: "Front Rebound", unit: "N/mm", description: "Front rebound damping.", impact: "More: slower front extension. Less: faster front recovery." }),
    createFloatField({ id: "frontArb", offset: 108, min: 0, max: 500, step: 0.1, label: "Front ARB", unit: "N/mm", description: "Front anti-roll bar.", impact: "Stiffer: sharper turn-in. Softer: more front mechanical grip." }),
    createByteField({ id: "frontToe", offset: 116, min: -0.9, max: 0.9, step: 0.1, decode: formatToe, encode: encodeToe, label: "Front Toe", unit: "deg", description: "Front toe setting.", impact: "More toe-out: stronger entry. More toe-in: calmer straight-line feel." }),
    createByteField({ id: "frontCaster", offset: 117, min: 0, max: 12, step: 0.1, decode: (raw) => raw / 10, encode: (value) => value * 10, label: "Front Caster", unit: "deg", description: "Front caster.", impact: "More: heavier steering and more camber gain. Less: lighter steering." }),
    createByteField({ id: "frontTyreType", offset: 118, kind: "select", options: tyreCompoundOptions, min: 0, max: tyreCompoundOptions.length - 1, label: "Front Compound", description: "Front tyre compound.", impact: "Softer compounds grip more, wear and heat more." }),
    createByteField({ id: "frontCamberLeft", offset: 120, min: -4.5, max: 4.5, step: 0.1, decode: formatCamber, encode: encodeCamber, label: "Front Left Camber", unit: "deg", description: "Front-left camber.", impact: "More negative: more cornering grip, less braking footprint." }),
    createByteField({ id: "frontCamberRight", offset: 121, min: -4.5, max: 4.5, step: 0.1, decode: formatCamber, encode: encodeCamber, label: "Front Right Camber", unit: "deg", description: "Front-right camber.", impact: "More negative: more cornering grip, less braking footprint." }),
    createByteField({ id: "frontTyreSize", offset: 122, kind: "select", options: tyreSizeIndexOptions, min: 0, max: tyreSizeIndexOptions.length - 1, label: "Front Size Index", description: "Front tyre size index.", impact: "Higher: larger alternate tyre where supported." }),
    createByteField({ id: "frontDiffPreload", offset: 123, min: 0, max: 2550, step: 10, decode: (raw) => raw * 10, encode: (value) => value / 10, label: "Front Preload", unit: "Nm", description: "Front diff preload.", impact: "More: stronger initial locking. Less: freer front axle." }),
    createByteField({ id: "frontDiffType", offset: 124, kind: "select", options: axleDiffTypeOptions, min: 0, max: axleDiffTypeOptions.length - 1, label: "Front Diff Type", description: "Front differential type.", impact: "Changes the base locking mechanism on the front axle." }),
    createByteField({ id: "frontViscousTorque", offset: 125, min: 0, max: 255, label: "Front Viscous Torque", description: "Front viscous locking.", impact: "More: stronger front coupling. Less: freer wheel speed split." }),
    createByteField({ id: "frontPowerLock", offset: 126, min: 0, max: 100, label: "Front Power Lock", unit: "%", description: "Front lock on throttle.", impact: "More: stronger front pull, more understeer risk. Less: freer front axle." }),
    createByteField({ id: "frontCoastLock", offset: 127, min: 0, max: 100, label: "Front Coast Lock", unit: "%", description: "Front lock off-throttle.", impact: "More: stronger front pull on entry. Less: freer rotation." }),
    createWordField({ id: "frontLeftPressure", offset: 128, min: 0, max: 400, step: 1, label: "Front Left Pressure", unit: "kPa", description: "Front-left pressure.", impact: "More: sharper response, smaller footprint. Less: more compliance." }),
    createWordField({ id: "frontRightPressure", offset: 130, min: 0, max: 400, step: 1, label: "Front Right Pressure", unit: "kPa", description: "Front-right pressure.", impact: "More: sharper response, smaller footprint. Less: more compliance." })
];

const fieldById = new Map(allSetupFields.map((field) => [field.id, field]));
const setupSections = [
    {
        id: "brakes",
        title: "Brakes",
        subsections: [
            { fields: ["brakeStrength", "brakeBalance", "handbrakeStrength", "absEnabled"] }
        ]
    },
    {
        id: "suspension",
        title: "Suspension",
        layout: "paired-table",
        rows: [
            { label: "Ride Height", frontField: "frontRideHeight", rearField: "rearRideHeight" },
            { label: "Stiffness", frontField: "frontSpring", rearField: "rearSpring" },
            { label: "Bump Damping", frontField: "frontBump", rearField: "rearBump" },
            { label: "Rebound", frontField: "frontRebound", rearField: "rearRebound" },
            { label: "Anti-Roll", frontField: "frontArb", rearField: "rearArb" }
        ]
    },
    {
        id: "steering",
        title: "Steering",
        subsections: [
            { title: "Geometry", fields: ["steeringLock", "parallelSteering", "frontToe", "frontCaster", "rearToe", "asymmetricalSetup"] }
        ]
    },
    {
        id: "drivetrain",
        title: "Final Drive, Diff & TC",
        layout: "wide",
        subsections: [
            { title: "Centre", fields: ["centreDiffType", "centreDiffViscousTorque", "centreDiffTorqueSplit", "tcEnabled", "tcSlip", "tcEngageSpeed"] },
            { title: "Front Diff", fields: ["frontDiffType", "frontDiffPreload", "frontViscousTorque", "frontPowerLock", "frontCoastLock"] },
            { title: "Rear Diff", fields: ["rearDiffType", "rearDiffPreload", "rearViscousTorque", "rearPowerLock", "rearCoastLock"] },
            { title: "Ratios", helper: "gear", fields: ["gearFinal", "gear1", "gear2", "gear3", "gear4", "gear5", "gear6", "gear7"] }
        ]
    },
    {
        id: "tyres",
        title: "Tyres",
        subsections: [
            { title: "Front", fields: ["tyreBrand", "frontTyreType", "frontTyreSize", "frontLeftPressure", "frontRightPressure", "frontCamberLeft", "frontCamberRight"] },
            { title: "Rear", fields: ["rearTyreType", "rearTyreSize", "rearLeftPressure", "rearRightPressure", "rearCamberLeft", "rearCamberRight"] }
        ]
    },
    {
        id: "aero",
        title: "Downforce & Balance",
        subsections: [
            { title: "Aero", fields: ["frontWing", "rearWing"] },
            { title: "Vehicle", fields: ["voluntaryMass", "massPosition", "intakeRestriction", "bodyConfig"] }
        ]
    },
    {
        id: "passengers",
        title: "Passengers",
        subsections: [
            { title: "Cabin", fields: ["passengerFrontRight", "passengerRearLeft", "passengerRearCenter", "passengerRearRight"] }
        ]
    }
];

function escapeHtml(value) {
    return String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function detectCarCodeFromFileName(fileName) {
    if (!fileName) {
        return "";
    }

    const upperName = fileName.toUpperCase();
    return supportedCarCodes.find((code) => upperName.includes(code)) ?? "";
}

function parseTyreSpecification(tyreSpec) {
    const match = tyreSpec?.match(/(\d+)\/(\d+)\s*R(\d+)/i);
    if (!match) {
        return null;
    }

    return {
        widthMm: Number(match[1]),
        aspectRatio: Number(match[2]),
        rimInches: Number(match[3])
    };
}

function getTyreCircumferenceMeters(tyreSpec) {
    const parsedSpec = parseTyreSpecification(tyreSpec);
    if (!parsedSpec) {
        return null;
    }

    const sidewallMm = parsedSpec.widthMm * (parsedSpec.aspectRatio / 100);
    const diameterMm = (parsedSpec.rimInches * 25.4) + (sidewallMm * 2);
    return (Math.PI * diameterMm) / 1000;
}

function getSelectedCarProfile() {
    return supportedCarProfiles[setupEditorState.selectedCarCode] ?? null;
}

function getDrivenTyreSpec(profile) {
    if (!profile) {
        return null;
    }

    if (profile.drive === "front") {
        return profile.frontTyreSpec;
    }

    if (profile.drive === "rear") {
        return profile.rearTyreSpec;
    }

    return profile.rearTyreSpec || profile.frontTyreSpec;
}

function calculateGearSpeedKmh(gearRatio, finalDrive, targetRpm, tyreSpec) {
    const circumferenceMeters = getTyreCircumferenceMeters(tyreSpec);
    if (!circumferenceMeters || !gearRatio || !finalDrive || !targetRpm) {
        return null;
    }

    const wheelRpm = targetRpm / (gearRatio * finalDrive);
    return (wheelRpm * circumferenceMeters * 60) / 1000;
}

function isGearField(fieldId) {
    return gearFieldIds.has(fieldId);
}

function getGearSpeedComment(field) {
    if (!isGearField(field.id)) {
        return null;
    }

    const profile = getSelectedCarProfile();
    const tyreSpec = getDrivenTyreSpec(profile);
    const gearRatio = setupEditorState.currentValues[field.id];
    const finalDrive = setupEditorState.currentValues.gearFinal;
    const speedKmh = calculateGearSpeedKmh(gearRatio, finalDrive, setupEditorState.gearTargetRpm, tyreSpec);

    if (!profile || !tyreSpec || !speedKmh) {
        return null;
    }

    return `${speedKmh.toFixed(1)} km/h @ ${Math.round(setupEditorState.gearTargetRpm)} rpm`;
}

function getFieldInlineComment(field) {
    const gearSpeedComment = getGearSpeedComment(field);
    if (gearSpeedComment && field.impact) {
        return `${field.impact} · ${gearSpeedComment}`;
    }

    return gearSpeedComment || field.impact || field.description || "";
}

function getGearHelperMarkup() {
    const profileOptions = supportedCarCodes.map((code) => {
        const profile = supportedCarProfiles[code];
        return `<option value="${code}" ${setupEditorState.selectedCarCode === code ? "selected" : ""}>${code} · ${escapeHtml(profile.name)}</option>`;
    }).join("");

    return `
        <div class="setup-gear-helper">
            <div class="setup-gear-helper-head">
                <p class="setup-gear-helper-title">Speed Helper</p>
                <p class="setup-gear-helper-meta" id="setup-gear-helper-meta"></p>
            </div>
            <div class="setup-gear-helper-controls">
                <label class="setup-gear-helper-field">
                    <span class="setup-gear-helper-label">Car</span>
                    <select id="setup-car-profile" class="setup-field-input setup-field-input--wide">
                        <option value="">Select car</option>
                        ${profileOptions}
                    </select>
                </label>
                <label class="setup-gear-helper-field">
                    <span class="setup-gear-helper-label">RPM</span>
                    <input id="setup-gear-rpm" class="setup-field-input" type="number" min="1000" max="25000" step="100" value="${Math.round(setupEditorState.gearTargetRpm)}">
                </label>
            </div>
        </div>`;
}

function formatLocalDateTime() {
    const timeElement = document.getElementById("live-local-time");
    const dateElement = document.getElementById("live-local-date");
    if (!timeElement || !dateElement) {
        return;
    }

    const now = new Date();
    timeElement.textContent = now.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
    dateElement.textContent = now.toLocaleDateString([], { day: "2-digit", month: "short", year: "numeric" });
}

async function loadSetupEditorMetadata() {
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
    } catch {
        // Keep defaults on the extra page.
    }
}

function decodeSetupValues(view) {
    const decoded = {};
    allSetupFields.forEach((field) => {
        decoded[field.id] = field.read(view);
    });

    return decoded;
}

function getFieldTitle(field, labelOverride = "") {
    return getFieldInlineComment(field) || field.description || labelOverride || field.label;
}

function getSectionFieldIds(section) {
    if (Array.isArray(section.rows)) {
        return section.rows.flatMap((row) => [row.frontField, row.rearField]);
    }

    if (!Array.isArray(section.subsections)) {
        return [];
    }

    return section.subsections.flatMap((subsection) => subsection.fields.map((fieldEntry) => typeof fieldEntry === "string" ? fieldEntry : fieldEntry.id));
}

function getActiveSetupSection() {
    return setupSections.find((section) => section.id === setupEditorState.activeSectionId) ?? setupSections[0];
}

function resolveFieldEntry(fieldEntry) {
    if (typeof fieldEntry === "string") {
        return { field: fieldById.get(fieldEntry), labelOverride: "" };
    }

    return {
        field: fieldById.get(fieldEntry.id),
        labelOverride: fieldEntry.label || ""
    };
}

function renderFieldControlMarkup(field, labelOverride = "") {
    const value = setupEditorState.currentValues[field.id];
    const disabledAttribute = setupEditorState.originalBytes ? "" : "disabled";
    const safeLabel = escapeHtml(labelOverride || field.label);

    if (field.kind === "boolean") {
        return `
            <div class="setup-field-control setup-field-control--toggle">
                <span class="setup-bool-state">${value ? "On" : "Off"}</span>
                <input type="checkbox" class="setup-field-checkbox" data-setup-field="${field.id}" ${value ? "checked" : ""} ${disabledAttribute}>
            </div>`;
    }

    if (field.kind === "select") {
        return `
            <div class="setup-field-control">
                <select class="setup-field-input" data-setup-field="${field.id}" ${disabledAttribute}>
                    ${field.options.map((option, index) => `<option value="${index}" ${Number(value) === index ? "selected" : ""}>${escapeHtml(option)}</option>`).join("")}
                </select>
            </div>`;
    }

    return `
        <div class="setup-field-control">
            <div class="setup-field-stepper">
                <button type="button" class="setup-stepper-button" data-setup-step="-1" aria-label="Decrease ${safeLabel}" ${disabledAttribute}>-</button>
                <input
                    type="number"
                    inputmode="decimal"
                    class="setup-field-input setup-field-input--number"
                    data-setup-field="${field.id}"
                    value="${escapeHtml(formatNumericValue(value, field.step ?? 1))}"
                    min="${field.min ?? ""}"
                    max="${field.max ?? ""}"
                    step="${field.step ?? "1"}"
                    ${disabledAttribute}>
                <button type="button" class="setup-stepper-button" data-setup-step="1" aria-label="Increase ${safeLabel}" ${disabledAttribute}>+</button>
            </div>
        </div>`;
}

function renderFieldMarkup(field, labelOverride = "") {
    const unitMarkup = field.unit ? `<span class="setup-field-unit">${escapeHtml(field.unit)}</span>` : "";
    const displayLabel = labelOverride || field.label;
    const safeDescription = escapeHtml(getFieldTitle(field, displayLabel));
    const safeLabel = escapeHtml(displayLabel);

    return `
        <label class="setup-field-row${field.kind === "boolean" ? " setup-field-row--boolean" : ""}" title="${safeDescription}" data-setup-title="${field.id}">
            <div class="setup-field-main">
                <div class="setup-field-title-block">
                    <p class="setup-field-label">${safeLabel}</p>
                    ${unitMarkup}
                </div>
            </div>
            ${renderFieldControlMarkup(field, displayLabel)}
        </label>`;
}

function renderSubsectionMarkup(subsection) {
    const fieldsMarkup = subsection.fields
        .map(resolveFieldEntry)
        .filter(({ field }) => Boolean(field))
        .map(({ field, labelOverride }) => renderFieldMarkup(field, labelOverride))
        .join("");

    const titleMarkup = subsection.title
        ? `<p class="setup-subsection-title">${escapeHtml(subsection.title)}</p>`
        : "";

    return `
        <section class="setup-subsection">
            ${titleMarkup}
            ${subsection.helper === "gear" ? getGearHelperMarkup() : ""}
            <div class="setup-field-list">
                ${fieldsMarkup}
            </div>
        </section>`;
}

function renderPairedTableContentMarkup(section) {
    const rowsMarkup = section.rows.map((row) => {
        const frontField = fieldById.get(row.frontField);
        const rearField = fieldById.get(row.rearField);
        if (!frontField || !rearField) {
            return "";
        }

        const unit = frontField.unit || rearField.unit || "";

        return `
            <div class="setup-paired-row">
                <div class="setup-paired-parameter">
                    <p class="setup-paired-label">${escapeHtml(row.label)}</p>
                    ${unit ? `<span class="setup-paired-unit">${escapeHtml(unit)}</span>` : ""}
                </div>
                <div class="setup-paired-cell" data-setup-title="${frontField.id}" title="${escapeHtml(getFieldTitle(frontField, `Front ${row.label}`))}">
                    ${renderFieldControlMarkup(frontField, `Front ${row.label}`)}
                </div>
                <div class="setup-paired-cell" data-setup-title="${rearField.id}" title="${escapeHtml(getFieldTitle(rearField, `Rear ${row.label}`))}">
                    ${renderFieldControlMarkup(rearField, `Rear ${row.label}`)}
                </div>
            </div>`;
    }).join("");

    return `
        <div class="setup-paired-table">
            <div class="setup-paired-head">
                <p class="setup-paired-head-cell">Parameter</p>
                <p class="setup-paired-head-cell">Front</p>
                <p class="setup-paired-head-cell">Rear</p>
            </div>
            ${rowsMarkup}
        </div>`;
}

function renderStandardSectionContentMarkup(section) {
    return `
        <div class="setup-subsection-grid${section.layout === "wide" ? " setup-subsection-grid--wide" : ""}">
            ${section.subsections.map((subsection) => renderSubsectionMarkup(subsection)).join("")}
        </div>`;
}

function renderSectionContentMarkup(section) {
    if (section.layout === "paired-table") {
        return renderPairedTableContentMarkup(section);
    }

    return renderStandardSectionContentMarkup(section);
}

function renderTabbedSetupPanelMarkup() {
    const activeSection = getActiveSetupSection();

    return `
        <section class="setup-group-card setup-tabbed-panel">
            <div class="setup-tab-strip" role="tablist" aria-label="Setup sections">
                ${setupSections.map((section) => `
                    <button
                        type="button"
                        class="setup-tab-button${section.id === activeSection.id ? " is-active" : ""}"
                        data-setup-tab="${section.id}"
                        role="tab"
                        aria-selected="${section.id === activeSection.id ? "true" : "false"}">
                        ${escapeHtml(section.title)}
                    </button>`).join("")}
            </div>
            <div class="setup-tab-panel-content setup-group-card--${activeSection.id}" role="tabpanel" aria-label="${escapeHtml(activeSection.title)}">
                ${renderSectionContentMarkup(activeSection)}
            </div>
        </section>`;
}

function renderSetupEditorGroups() {
    const groupsElement = document.getElementById("setup-editor-groups");
    if (!groupsElement) {
        return;
    }

    groupsElement.innerHTML = renderTabbedSetupPanelMarkup();

    document.querySelectorAll("[data-setup-tab]").forEach((button) => {
        button.addEventListener("click", handleSectionTabClick);
    });

    document.querySelectorAll("[data-setup-field]").forEach((input) => {
        input.addEventListener("input", handleFieldInput);
        input.addEventListener("change", handleFieldInput);
    });

    document.querySelectorAll("[data-setup-step]").forEach((button) => {
        button.addEventListener("click", handleStepperButtonClick);
        button.addEventListener("contextmenu", handleStepperButtonContextMenu);
    });

    bindGearHelperInputs();
    updateGearHelperMeta();
}

function handleStepperButtonClick(event) {
    const button = event.currentTarget;
    adjustStepperValue(button, 1);
}

function handleStepperButtonContextMenu(event) {
    event.preventDefault();
    const button = event.currentTarget;
    adjustStepperValue(button, 10);
}

function handleSectionTabClick(event) {
    const sectionId = event.currentTarget.dataset.setupTab;
    if (!sectionId || sectionId === setupEditorState.activeSectionId) {
        return;
    }

    setupEditorState.activeSectionId = sectionId;
    renderSetupEditorGroups();
}

function adjustStepperValue(button, multiplier) {
    const direction = Number(button.dataset.setupStep);
    if (!Number.isFinite(direction) || direction === 0) {
        return;
    }

    const stepperElement = button.closest(".setup-field-stepper");
    const input = stepperElement?.querySelector("[data-setup-field]");
    if (!(input instanceof HTMLInputElement) || input.disabled) {
        return;
    }

    const stepCount = Math.max(1, Math.trunc(multiplier));

    if (direction > 0) {
        input.stepUp(stepCount);
    } else {
        input.stepDown(stepCount);
    }

    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
    input.focus();
}

function bindGearHelperInputs() {
    const carProfileInput = document.getElementById("setup-car-profile");
    const rpmInput = document.getElementById("setup-gear-rpm");

    carProfileInput?.addEventListener("change", (event) => {
        setupEditorState.selectedCarCode = event.target.value;
        updateGearHelperMeta();
        updateDynamicFieldComments();
        refreshSetupSummary();
    });

    rpmInput?.addEventListener("input", (event) => {
        setupEditorState.gearTargetRpm = clamp(sanitizeNumber(event.target.value, 7000), 1000, 25000);
        event.target.value = Math.round(setupEditorState.gearTargetRpm);
        updateGearHelperMeta();
        updateDynamicFieldComments();
    });
}

function updateDynamicFieldComments() {
    allSetupFields.forEach((field) => {
        document.querySelectorAll(`[data-setup-title="${field.id}"]`).forEach((element) => {
            element.setAttribute("title", getFieldTitle(field));
        });
    });
}

function updateGearHelperMeta() {
    const metaElement = document.getElementById("setup-gear-helper-meta");
    if (!metaElement) {
        return;
    }

    const profile = getSelectedCarProfile();
    if (!profile) {
        metaElement.textContent = "Select the car to calculate theoretical gear speed.";
        return;
    }

    const drivenTyreSpec = getDrivenTyreSpec(profile);
    const driveLabel = profile.drive === "front" ? "FWD" : profile.drive === "rear" ? "RWD" : "AWD";
    metaElement.textContent = `${profile.name} · ${driveLabel} · driven tyre ${drivenTyreSpec}`;
}

function handleFieldInput(event) {
    const fieldId = event.target.dataset.setupField;
    const field = fieldById.get(fieldId);
    if (!field) {
        return;
    }

    let nextValue;
    if (field.kind === "boolean") {
        nextValue = field.normalize(event.target.checked);
    } else if (field.kind === "select") {
        nextValue = field.normalize(Number(event.target.value));
    } else {
        nextValue = field.normalize(event.target.value);
        event.target.value = formatNumericValue(nextValue, field.step ?? 1);
    }

    setupEditorState.currentValues[field.id] = nextValue;
    setupEditorState.isDirty = true;
    updateSetupActionState();
    updateDynamicFieldComments();
    updateGearHelperMeta();
}

function updateStatus(message, isError = false) {
    const statusElement = document.getElementById("setup-file-status");
    if (!statusElement) {
        return;
    }

    statusElement.textContent = message;
    statusElement.classList.toggle("is-error", isError);
}

function updateSetupActionState() {
    const isLoaded = Boolean(setupEditorState.originalBytes);
    const saveButton = document.getElementById("setup-save-button");
    const copyButton = document.getElementById("setup-copy-ai-button");
    const resetButton = document.getElementById("setup-reset-button");

    if (saveButton) {
        saveButton.disabled = !isLoaded;
    }

    if (copyButton) {
        copyButton.disabled = !isLoaded;
    }

    if (resetButton) {
        resetButton.disabled = !isLoaded || !setupEditorState.isDirty;
    }
}

function formatFieldValue(field, value) {
    if (field.kind === "boolean") {
        return value ? "On" : "Off";
    }

    if (field.kind === "select") {
        return field.options?.[value] ?? String(value ?? "-");
    }

    return formatNumericValue(value, field.step ?? 1) || "-";
}

function buildSetupTextBrief() {
    if (!setupEditorState.originalBytes) {
        return "";
    }

    const lines = [
        `${setupEditorState.fileName || "setup.set"} | ${setupEditorState.signature || "-"} v${setupEditorState.formatVersion || "-"} | internal ${setupEditorState.internalVersion || "-"} | patch X ${setupEditorState.isPatchX ? "yes" : "no"}`
    ];

    const profile = getSelectedCarProfile();
    if (profile && setupEditorState.selectedCarCode) {
        lines.push(`Car: ${setupEditorState.selectedCarCode} · ${profile.name}`);
    }

    setupSections.forEach((section) => {
        const parts = [];

        getSectionFieldIds(section).forEach((fieldId) => {
            const field = fieldById.get(fieldId);
            if (!field) {
                return;
            }

            const value = formatFieldValue(field, setupEditorState.currentValues[field.id]);
            const unitSuffix = field.unit ? ` ${field.unit}` : "";
            parts.push(`${field.label}: ${value}${unitSuffix}`);
        });

        lines.push(`${section.title}: ${parts.join("; ")}`);
    });

    return lines.join("\n");
}

function refreshSetupUi() {
    const fileNameElement = document.getElementById("setup-file-name");

    if (fileNameElement) {
        fileNameElement.textContent = setupEditorState.fileName || "No file loaded";
    }

    renderSetupEditorGroups();
    updateSetupActionState();
}

async function loadSetupFile(file) {
    if (!file) {
        return;
    }

    const buffer = await file.arrayBuffer();
    const bytes = new Uint8Array(buffer);
    const view = new DataView(buffer);
    const signature = new TextDecoder("ascii").decode(bytes.slice(0, 6));

    if (signature !== "SRSETT") {
        updateStatus("This file is not a supported LFS .set file.", true);
        return;
    }

    if (bytes.length < 132) {
        updateStatus("The file is too short to contain the documented setup format.", true);
        return;
    }

    setupEditorState.originalBytes = bytes;
    setupEditorState.currentValues = decodeSetupValues(view);
    setupEditorState.fileName = file.name;
    setupEditorState.signature = signature;
    setupEditorState.internalVersion = view.getUint8(7);
    setupEditorState.formatVersion = view.getUint8(8);
    setupEditorState.isPatchX = (view.getUint8(12) & 0x80) !== 0;
    setupEditorState.isDirty = false;
    setupEditorState.selectedCarCode = detectCarCodeFromFileName(file.name);

    updateStatus("Setup loaded. Edit values, save the file, or copy the text brief.");
    refreshSetupUi();
}

function saveSetupFile() {
    if (!setupEditorState.originalBytes) {
        return;
    }

    const nextBytes = new Uint8Array(setupEditorState.originalBytes);
    const view = new DataView(nextBytes.buffer);
    allSetupFields.forEach((field) => {
        field.write(view, setupEditorState.currentValues[field.id]);
    });

    const blob = new Blob([nextBytes], { type: "application/octet-stream" });
    const anchor = document.createElement("a");
    const safeName = setupEditorState.fileName || "edited-setup.set";
    const downloadUrl = URL.createObjectURL(blob);
    anchor.href = downloadUrl;
    anchor.download = safeName.endsWith(".set") ? safeName : `${safeName}.set`;
    anchor.click();
    window.setTimeout(() => URL.revokeObjectURL(downloadUrl), 0);

    setupEditorState.originalBytes = nextBytes;
    setupEditorState.isDirty = false;
    updateSetupActionState();
    updateStatus("Edited setup saved to disk.");
}

async function copySetupForAi() {
    const copyButton = document.getElementById("setup-copy-ai-button");
    if (!copyButton || !setupEditorState.originalBytes) {
        return;
    }

    const textBrief = buildSetupTextBrief();

    try {
        await navigator.clipboard.writeText(textBrief);
        const originalText = copyButton.textContent;
        copyButton.textContent = "Copied";
        window.setTimeout(() => {
            copyButton.textContent = originalText;
        }, 1400);
    } catch {
        const fallbackElement = document.createElement("textarea");
        fallbackElement.value = textBrief;
        fallbackElement.setAttribute("readonly", "readonly");
        fallbackElement.style.position = "fixed";
        fallbackElement.style.opacity = "0";
        document.body.appendChild(fallbackElement);
        fallbackElement.focus();
        fallbackElement.select();
        document.execCommand("copy");
        fallbackElement.remove();
    }
}

function resetSetupValues() {
    if (!setupEditorState.originalBytes) {
        return;
    }

    const view = new DataView(setupEditorState.originalBytes.slice().buffer);
    setupEditorState.currentValues = decodeSetupValues(view);
    setupEditorState.isDirty = false;
    updateStatus("Changes reverted to the last loaded state.");
    refreshSetupUi();
}

function bindSetupEditorActions() {
    const fileInput = document.getElementById("setup-file-input");
    const saveButton = document.getElementById("setup-save-button");
    const copyButton = document.getElementById("setup-copy-ai-button");
    const resetButton = document.getElementById("setup-reset-button");

    fileInput?.addEventListener("change", (event) => {
        const [file] = event.target.files || [];
        loadSetupFile(file);
    });

    saveButton?.addEventListener("click", saveSetupFile);
    copyButton?.addEventListener("click", copySetupForAi);
    resetButton?.addEventListener("click", resetSetupValues);
}

document.addEventListener("DOMContentLoaded", () => {
    formatLocalDateTime();
    window.setInterval(formatLocalDateTime, 1000);
    loadSetupEditorMetadata();
    bindSetupEditorActions();
    refreshSetupUi();
});
