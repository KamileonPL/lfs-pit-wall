const setupEditorState = {
    originalBytes: null,
    currentValues: {},
    fileName: "",
    signature: "",
    internalVersion: 0,
    formatVersion: 0,
    isPatchX: false,
    isDirty: false,
    selectedCarCode: "",
    gearTargetRpm: 7000
};

const tyreBrandOptions = ["Cromo Plain", "Cromo", "Torro", "Michelin", "Evostar"];
const tyreCompoundOptions = ["R1", "R2", "R3", "R4", "Road Super", "Road Normal", "Hybrid", "Knobbly"];
const centreDiffTypeOptions = ["Open", "Viscous"];
const axleDiffTypeOptions = ["Open", "Locked", "Viscous", "Clutch Pack"];
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

const setupGroups = [
    {
        id: "general",
        title: "General Balance",
        description: "High-level switches and controls that change how the whole car behaves.",
        fields: [
            createBitField({ id: "absEnabled", offset: 12, bit: 2, label: "ABS", description: "Helps keep braking stable under heavy pedal pressure.", impact: "Braking stability and lock-up resistance." }),
            createBitField({ id: "tcEnabled", offset: 12, bit: 1, label: "Traction Control", description: "Helps stop wheelspin when power is applied too aggressively.", impact: "Exit traction and throttle confidence." }),
            createBitField({ id: "asymmetricalSetup", offset: 12, bit: 0, label: "Asymmetrical Setup", description: "Allows left and right sides of the setup to differ.", impact: "Track-specific balance options." }),
            createByteField({ id: "tyreBrand", offset: 15, kind: "select", options: tyreBrandOptions, min: 0, max: tyreBrandOptions.length - 1, label: "Tyre Brand", description: "The selected tyre manufacturer for this setup file.", impact: "Base tyre family and compatibility." }),
            createFloatField({ id: "brakeStrength", offset: 16, min: 0, max: 10000, step: 1, label: "Brake Strength", unit: "Nm", description: "Overall brake torque available at full pedal input.", impact: "Stopping power versus lock-up sensitivity." }),
            createByteField({ id: "rearWing", offset: 20, min: 0, max: 60, label: "Rear Wing", description: "Higher wing gives more rear stability but more drag.", impact: "Rear grip, confidence and top speed." }),
            createByteField({ id: "frontWing", offset: 21, min: 0, max: 60, label: "Front Wing", description: "Higher front wing gives more front bite but more drag.", impact: "Turn-in response, front grip and top speed." }),
            createByteField({ id: "voluntaryMass", offset: 22, min: 0, max: 255, label: "Voluntary Mass", unit: "kg", description: "Adds ballast voluntarily. Usually used only for restrictions or experiments.", impact: "Acceleration, braking and tyre load." }),
            createByteField({ id: "intakeRestriction", offset: 23, min: 0, max: 100, label: "Intake Restriction", unit: "%", description: "Artificially reduces engine breathing and power.", impact: "Top speed and acceleration." }),
            createByteField({ id: "steeringLock", offset: 24, min: 0, max: 90, label: "Max Steering Lock", unit: "deg", description: "Maximum steering angle available to the front wheels.", impact: "Hairpins, rotation and steering precision." }),
            createByteField({ id: "parallelSteering", offset: 25, min: 0, max: 100, label: "Parallel Steering", unit: "%", description: "Adjusts how both front wheels steer relative to each other.", impact: "Turn-in feel and tyre scrub." }),
            createByteField({ id: "brakeBalance", offset: 26, min: 0, max: 100, label: "Brake Balance", unit: "%F", description: "Moves braking effort toward the front or rear.", impact: "Braking stability versus rotation." }),
            createByteField({ id: "engineBrakeReduction", offset: 27, min: 0, max: 100, label: "Engine Brake Reduction", unit: "%", description: "Reduces engine drag on corner entry.", impact: "Entry stability and lift-off behaviour." })
        ]
    },
    {
        id: "drivetrain",
        title: "Drivetrain & Gearing",
        description: "Controls how power is delivered and how quickly the car runs through the gears.",
        fields: [
            createByteField({ id: "centreDiffType", offset: 28, kind: "select", options: centreDiffTypeOptions, min: 0, max: centreDiffTypeOptions.length - 1, label: "Centre Diff Type", description: "Sets the coupling style between front and rear axles.", impact: "4WD balance and driveline feel." }),
            createByteField({ id: "centreDiffViscousTorque", offset: 29, min: 0, max: 255, label: "Centre Viscous Torque", description: "How strongly a viscous centre diff resists speed difference.", impact: "Mid-corner driveline binding and traction." }),
            createByteField({ id: "centreDiffTorqueSplit", offset: 31, min: 0, max: 100, label: "Centre Torque Split", unit: "%F", description: "Moves static torque distribution frontward or rearward.", impact: "Corner balance under power." }),
            createWordField({ id: "gearFinal", offset: 34, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Final Drive", description: "Shorter final drive boosts acceleration, longer improves top speed.", impact: "Acceleration versus maximum speed." }),
            createWordField({ id: "gear1", offset: 36, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 1", description: "First gear ratio.", impact: "Launch and hairpin exits." }),
            createWordField({ id: "gear2", offset: 38, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 2", description: "Second gear ratio.", impact: "Low-speed acceleration." }),
            createWordField({ id: "gear3", offset: 40, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 3", description: "Third gear ratio.", impact: "Medium-speed acceleration." }),
            createWordField({ id: "gear4", offset: 42, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 4", description: "Fourth gear ratio.", impact: "Mid-speed acceleration and flexibility." }),
            createWordField({ id: "gear5", offset: 44, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 5", description: "Fifth gear ratio.", impact: "Fast sections and overtakes." }),
            createWordField({ id: "gear6", offset: 46, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 6", description: "Sixth gear ratio.", impact: "Top-end speed and efficiency." }),
            createWordField({ id: "gear7", offset: 32, min: 0.5, max: 7.5, step: 0.001, decode: formatRatio, encode: encodeRatio, label: "Gear 7", description: "Seventh gear ratio where the car supports it.", impact: "Longest top-speed gearing." }),
            createByteField({ id: "tcSlip", offset: 50, min: 0, max: 25.5, step: 0.1, decode: (raw) => raw / 10, encode: (value) => value * 10, label: "TC Slip", unit: "%", description: "How much wheel slip is allowed before traction control intervenes.", impact: "Power delivery sharpness." }),
            createByteField({ id: "tcEngageSpeed", offset: 51, min: 0, max: 255, label: "TC Engage Speed", unit: "km/h", description: "Vehicle speed above which traction control starts acting.", impact: "Low-speed traction control behaviour." })
        ]
    },
    {
        id: "rear",
        title: "Rear Suspension & Diff",
        description: "Rear-end support, traction and stability under braking and throttle.",
        fields: [
            createFloatField({ id: "rearRideHeight", offset: 52, min: 0, max: 300, step: 0.1, label: "Rear Ride Height", unit: "mm", description: "Rear body height. Higher usually adds bump clearance but raises the centre of mass.", impact: "Weight transfer, rake and kerb clearance." }),
            createFloatField({ id: "rearSpring", offset: 56, min: 0, max: 500, step: 0.1, label: "Rear Spring", unit: "N/mm", description: "Main rear spring stiffness.", impact: "Rear support, traction and responsiveness." }),
            createFloatField({ id: "rearBump", offset: 60, min: 0, max: 500, step: 0.1, label: "Rear Bump Damping", unit: "N/mm", description: "Rear compression damping when the suspension compresses.", impact: "Kerb control and transient rear support." }),
            createFloatField({ id: "rearRebound", offset: 64, min: 0, max: 500, step: 0.1, label: "Rear Rebound Damping", unit: "N/mm", description: "Rear rebound damping when the suspension extends.", impact: "Exit traction and platform recovery." }),
            createFloatField({ id: "rearArb", offset: 68, min: 0, max: 500, step: 0.1, label: "Rear Anti-roll Bar", unit: "N/mm", description: "Connects rear wheels in roll. Stiffer often rotates the car more.", impact: "Mid-corner balance and exit traction." }),
            createByteField({ id: "rearToe", offset: 76, min: -0.9, max: 0.9, step: 0.1, decode: formatToe, encode: encodeToe, label: "Rear Toe", unit: "deg", description: "Toe-in calms the rear, toe-out makes it more lively.", impact: "Straight-line stability and rotation." }),
            createByteField({ id: "rearTyreType", offset: 78, kind: "select", options: tyreCompoundOptions, min: 0, max: tyreCompoundOptions.length - 1, label: "Rear Tyre", description: "Compound used on the rear axle.", impact: "Grip, temperature and wear." }),
            createByteField({ id: "rearCamberLeft", offset: 80, min: -4.5, max: 4.5, step: 0.1, decode: formatCamber, encode: encodeCamber, label: "Rear Left Camber", unit: "deg", description: "Rear left wheel camber angle.", impact: "Loaded cornering grip versus traction." }),
            createByteField({ id: "rearCamberRight", offset: 81, min: -4.5, max: 4.5, step: 0.1, decode: formatCamber, encode: encodeCamber, label: "Rear Right Camber", unit: "deg", description: "Rear right wheel camber angle.", impact: "Loaded cornering grip versus traction." }),
            createByteField({ id: "rearDiffPreload", offset: 83, min: 0, max: 2550, step: 10, decode: (raw) => raw * 10, encode: (value) => value / 10, label: "Rear Diff Preload", unit: "Nm", description: "Baseline locking before power or coast ramps act.", impact: "Initial rotation and throttle pickup." }),
            createByteField({ id: "rearDiffType", offset: 84, kind: "select", options: axleDiffTypeOptions, min: 0, max: axleDiffTypeOptions.length - 1, label: "Rear Diff Type", description: "Rear axle differential design.", impact: "Power delivery character." }),
            createByteField({ id: "rearViscousTorque", offset: 85, min: 0, max: 255, label: "Rear Viscous Torque", description: "Viscous locking strength when that diff type is used.", impact: "Rear axle coupling." }),
            createByteField({ id: "rearPowerLock", offset: 86, min: 0, max: 100, label: "Rear Power Lock", unit: "%", description: "Locking under power.", impact: "Exit traction versus push or oversteer." }),
            createByteField({ id: "rearCoastLock", offset: 87, min: 0, max: 100, label: "Rear Coast Lock", unit: "%", description: "Locking off-throttle and under braking.", impact: "Entry stability versus rotation." }),
            createWordField({ id: "rearLeftPressure", offset: 88, min: 0, max: 400, step: 1, label: "Rear Left Pressure", unit: "kPa", description: "Tyre pressure on the rear-left wheel.", impact: "Temperature, response and grip footprint." }),
            createWordField({ id: "rearRightPressure", offset: 90, min: 0, max: 400, step: 1, label: "Rear Right Pressure", unit: "kPa", description: "Tyre pressure on the rear-right wheel.", impact: "Temperature, response and grip footprint." })
        ]
    },
    {
        id: "front",
        title: "Front Suspension & Diff",
        description: "Front-end bite, direction change and braking support.",
        fields: [
            createFloatField({ id: "frontRideHeight", offset: 92, min: 0, max: 300, step: 0.1, label: "Front Ride Height", unit: "mm", description: "Front body height. Lower can help aero and response if the car has enough clearance.", impact: "Turn-in, rake and kerb clearance." }),
            createFloatField({ id: "frontSpring", offset: 96, min: 0, max: 500, step: 0.1, label: "Front Spring", unit: "N/mm", description: "Main front spring stiffness.", impact: "Front support and responsiveness." }),
            createFloatField({ id: "frontBump", offset: 100, min: 0, max: 500, step: 0.1, label: "Front Bump Damping", unit: "N/mm", description: "Front compression damping.", impact: "Kerb support and entry weight transfer." }),
            createFloatField({ id: "frontRebound", offset: 104, min: 0, max: 500, step: 0.1, label: "Front Rebound Damping", unit: "N/mm", description: "Front rebound damping.", impact: "Steering response and platform recovery." }),
            createFloatField({ id: "frontArb", offset: 108, min: 0, max: 500, step: 0.1, label: "Front Anti-roll Bar", unit: "N/mm", description: "Connects front wheels in roll. Stiffer often sharpens the front but can reduce grip on exit.", impact: "Turn-in precision versus front grip." }),
            createByteField({ id: "frontToe", offset: 116, min: -0.9, max: 0.9, step: 0.1, decode: formatToe, encode: encodeToe, label: "Front Toe", unit: "deg", description: "Front toe angle. Toe-out sharpens entry, toe-in calms the car.", impact: "Turn-in bite and tyre scrub." }),
            createByteField({ id: "frontCaster", offset: 117, min: 0, max: 12, step: 0.1, decode: (raw) => raw / 10, encode: (value) => value * 10, label: "Front Caster", unit: "deg", description: "Caster angle helps steering weight and camber gain while turning.", impact: "Steering feel and loaded front grip." }),
            createByteField({ id: "frontTyreType", offset: 118, kind: "select", options: tyreCompoundOptions, min: 0, max: tyreCompoundOptions.length - 1, label: "Front Tyre", description: "Compound used on the front axle.", impact: "Grip, temperature and wear." }),
            createByteField({ id: "frontCamberLeft", offset: 120, min: -4.5, max: 4.5, step: 0.1, decode: formatCamber, encode: encodeCamber, label: "Front Left Camber", unit: "deg", description: "Front left wheel camber angle.", impact: "Turn-in grip versus braking footprint." }),
            createByteField({ id: "frontCamberRight", offset: 121, min: -4.5, max: 4.5, step: 0.1, decode: formatCamber, encode: encodeCamber, label: "Front Right Camber", unit: "deg", description: "Front right wheel camber angle.", impact: "Turn-in grip versus braking footprint." }),
            createByteField({ id: "frontDiffPreload", offset: 123, min: 0, max: 2550, step: 10, decode: (raw) => raw * 10, encode: (value) => value / 10, label: "Front Diff Preload", unit: "Nm", description: "Baseline front diff locking before ramps act.", impact: "Initial traction and entry pull." }),
            createByteField({ id: "frontDiffType", offset: 124, kind: "select", options: axleDiffTypeOptions, min: 0, max: axleDiffTypeOptions.length - 1, label: "Front Diff Type", description: "Front axle differential design.", impact: "Power-on front axle behaviour." }),
            createByteField({ id: "frontViscousTorque", offset: 125, min: 0, max: 255, label: "Front Viscous Torque", description: "Viscous locking strength for the front diff.", impact: "Front axle coupling." }),
            createByteField({ id: "frontPowerLock", offset: 126, min: 0, max: 100, label: "Front Power Lock", unit: "%", description: "Front diff locking under power.", impact: "Front traction versus understeer." }),
            createByteField({ id: "frontCoastLock", offset: 127, min: 0, max: 100, label: "Front Coast Lock", unit: "%", description: "Front diff locking off-throttle.", impact: "Entry pull and braking behaviour." }),
            createWordField({ id: "frontLeftPressure", offset: 128, min: 0, max: 400, step: 1, label: "Front Left Pressure", unit: "kPa", description: "Tyre pressure on the front-left wheel.", impact: "Response, temperature and grip footprint." }),
            createWordField({ id: "frontRightPressure", offset: 130, min: 0, max: 400, step: 1, label: "Front Right Pressure", unit: "kPa", description: "Response, temperature and grip footprint.", impact: "Response, temperature and grip footprint." })
        ]
    }
];

const allSetupFields = setupGroups.flatMap((group) => group.fields);

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
    if (gearSpeedComment) {
        return gearSpeedComment;
    }

    return field.impact || field.description || "";
}

function getGearHelperMarkup(group) {
    if (group.id !== "drivetrain") {
        return "";
    }

    const profileOptions = supportedCarCodes.map((code) => {
        const profile = supportedCarProfiles[code];
        return `<option value="${code}" ${setupEditorState.selectedCarCode === code ? "selected" : ""}>${code} · ${profile.name}</option>`;
    }).join("");

    return `
        <div class="setup-gear-helper">
            <div class="setup-gear-helper-copy-block">
                <p class="setup-gear-helper-title">Gear Speed Helper</p>
                <p class="setup-gear-helper-copy">Theoretical speed from selected tyre size, final drive and gear ratio.</p>
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
            <p class="setup-gear-helper-meta" id="setup-gear-helper-meta"></p>
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

function getSummaryCardsMarkup() {
    return `
        <div class="setup-summary-card">
            <p class="setup-summary-label">Format</p>
            <p class="setup-summary-value">${setupEditorState.signature || "-"} / v${setupEditorState.formatVersion || "-"}</p>
        </div>
        <div class="setup-summary-card">
            <p class="setup-summary-label">Internal Version</p>
            <p class="setup-summary-value">${setupEditorState.internalVersion || "-"}</p>
        </div>
        <div class="setup-summary-card">
            <p class="setup-summary-label">Patch X Setup</p>
            <p class="setup-summary-value">${setupEditorState.isPatchX ? "Yes" : "No"}</p>
        </div>
        <div class="setup-summary-card">
            <p class="setup-summary-label">Known Parameters</p>
            <p class="setup-summary-value">${allSetupFields.length}</p>
        </div>`;
}

function renderFieldMarkup(field) {
    const value = setupEditorState.currentValues[field.id];
    const disabledAttribute = setupEditorState.originalBytes ? "" : "disabled";
    const inlineComment = getFieldInlineComment(field);
    const unitMarkup = field.unit ? `<span class="setup-field-unit">${field.unit}</span>` : "";
    const safeDescription = escapeHtml(field.description);

    if (field.kind === "boolean") {
        return `
            <label class="setup-field-row setup-field-row--boolean" title="${safeDescription}">
                <div class="setup-field-title-block">
                    <p class="setup-field-label">${field.label}</p>
                </div>
                <div class="setup-field-control setup-field-control--toggle">
                    <span class="setup-bool-state">${value ? "On" : "Off"}</span>
                    <input type="checkbox" class="setup-field-checkbox" data-setup-field="${field.id}" ${value ? "checked" : ""} ${disabledAttribute}>
                </div>
                <p class="setup-field-comment" data-setup-comment="${field.id}">${inlineComment}</p>
            </label>`;
    }

    if (field.kind === "select") {
        return `
            <label class="setup-field-row" title="${safeDescription}">
                <div class="setup-field-title-block">
                    <p class="setup-field-label">${field.label}</p>
                </div>
                <div class="setup-field-control">
                    <select class="setup-field-input" data-setup-field="${field.id}" ${disabledAttribute}>
                        ${field.options.map((option, index) => `<option value="${index}" ${Number(value) === index ? "selected" : ""}>${option}</option>`).join("")}
                    </select>
                </div>
                <p class="setup-field-comment" data-setup-comment="${field.id}">${inlineComment}</p>
            </label>`;
    }

    return `
        <label class="setup-field-row" title="${safeDescription}">
            <div class="setup-field-title-block">
                <p class="setup-field-label">${field.label}${unitMarkup}</p>
            </div>
            <div class="setup-field-control">
                <input
                    type="number"
                    class="setup-field-input"
                    data-setup-field="${field.id}"
                    value="${value ?? ""}"
                    min="${field.min ?? ""}"
                    max="${field.max ?? ""}"
                    step="${field.step ?? "1"}"
                    ${disabledAttribute}>
            </div>
                    <p class="setup-field-comment" data-setup-comment="${field.id}">${inlineComment}</p>
        </label>`;
}

function renderSetupEditorGroups() {
    const groupsElement = document.getElementById("setup-editor-groups");
    if (!groupsElement) {
        return;
    }

    groupsElement.innerHTML = setupGroups.map((group) => `
        <section class="setup-group-card">
            <div class="setup-group-header">
                <div>
                    <p class="setup-group-title">${group.title}</p>
                    <p class="setup-group-copy">${group.description}</p>
                </div>
            </div>
            ${getGearHelperMarkup(group)}
            <div class="setup-field-list">
                ${group.fields.map((field) => renderFieldMarkup(field)).join("")}
            </div>
        </section>`).join("");

    document.querySelectorAll("[data-setup-field]").forEach((input) => {
        input.addEventListener("input", handleFieldInput);
        input.addEventListener("change", handleFieldInput);
    });

    bindGearHelperInputs();
    updateGearHelperMeta();
}

function bindGearHelperInputs() {
    const carProfileInput = document.getElementById("setup-car-profile");
    const rpmInput = document.getElementById("setup-gear-rpm");

    carProfileInput?.addEventListener("change", (event) => {
        setupEditorState.selectedCarCode = event.target.value;
        updateGearHelperMeta();
        updateDynamicFieldComments();
        updateAiExport();
    });

    rpmInput?.addEventListener("input", (event) => {
        setupEditorState.gearTargetRpm = clamp(sanitizeNumber(event.target.value, 7000), 1000, 25000);
        event.target.value = Math.round(setupEditorState.gearTargetRpm);
        updateGearHelperMeta();
        updateDynamicFieldComments();
        updateAiExport();
    });
}

function updateDynamicFieldComments() {
    allSetupFields.forEach((field) => {
        const commentElement = document.querySelector(`[data-setup-comment="${field.id}"]`);
        if (!commentElement) {
            return;
        }

        commentElement.textContent = getFieldInlineComment(field);
    });
}

function updateGearHelperMeta() {
    const metaElement = document.getElementById("setup-gear-helper-meta");
    if (!metaElement) {
        return;
    }

    const profile = getSelectedCarProfile();
    if (!profile) {
        metaElement.textContent = "Select the car to show theoretical speed for each gear.";
        return;
    }

    const drivenTyreSpec = getDrivenTyreSpec(profile);
    const driveLabel = profile.drive === "front" ? "FWD" : profile.drive === "rear" ? "RWD" : "AWD";
    metaElement.textContent = `${profile.name} · ${driveLabel} · driven tyre ${drivenTyreSpec}`;
}

function handleFieldInput(event) {
    const fieldId = event.target.dataset.setupField;
    const field = allSetupFields.find((candidate) => candidate.id === fieldId);
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
        event.target.value = nextValue;
    }

    setupEditorState.currentValues[field.id] = nextValue;
    setupEditorState.isDirty = true;
    updateSetupActionState();
    updateDynamicFieldComments();
    updateGearHelperMeta();
    updateAiExport();
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

function updateAiExport() {
    const aiExportElement = document.getElementById("setup-ai-export");
    if (!aiExportElement) {
        return;
    }

    if (!setupEditorState.originalBytes) {
        aiExportElement.value = "Load a setup file to generate the AI summary.";
        return;
    }

    const lines = [
        "LFS setup summary",
        `File: ${setupEditorState.fileName}`,
        `Format: ${setupEditorState.signature} / version ${setupEditorState.formatVersion}`,
        `Patch X setup: ${setupEditorState.isPatchX ? "yes" : "no"}`,
        "",
        "Key parameters:"
    ];

    setupGroups.forEach((group) => {
        lines.push("");
        lines.push(`[${group.title}]`);
        group.fields.forEach((field) => {
            const value = setupEditorState.currentValues[field.id];
            const displayValue = field.kind === "boolean"
                ? (value ? "On" : "Off")
                : field.kind === "select"
                    ? field.options?.[value] ?? String(value)
                    : String(value);
            lines.push(`- ${field.label}: ${displayValue}${field.unit ? ` ${field.unit}` : ""}`);
            lines.push(`  Notes: ${field.description}`);
            lines.push(`  Affects: ${getFieldInlineComment(field)}`);
        });
    });

    aiExportElement.value = lines.join("\n");
}

function refreshSetupUi() {
    const fileNameElement = document.getElementById("setup-file-name");
    const summaryGrid = document.getElementById("setup-summary-grid");

    if (fileNameElement) {
        fileNameElement.textContent = setupEditorState.fileName || "No file loaded";
    }

    if (summaryGrid) {
        summaryGrid.innerHTML = getSummaryCardsMarkup();
    }

    renderSetupEditorGroups();
    updateAiExport();
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

    updateStatus("Setup loaded. You can edit the values locally, save the file, or copy the AI brief.");
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
    setupEditorState.isDirty = false;
    updateSetupActionState();
    updateStatus("Edited setup saved to disk.");
}

async function copySetupForAi() {
    const aiExportElement = document.getElementById("setup-ai-export");
    const copyButton = document.getElementById("setup-copy-ai-button");
    if (!aiExportElement || !copyButton || !setupEditorState.originalBytes) {
        return;
    }

    try {
        await navigator.clipboard.writeText(aiExportElement.value);
        const originalText = copyButton.textContent;
        copyButton.textContent = "Copied for AI";
        window.setTimeout(() => {
            copyButton.textContent = originalText;
        }, 1400);
    } catch {
        aiExportElement.focus();
        aiExportElement.select();
        document.execCommand("copy");
    }
}

function resetSetupValues() {
    if (!setupEditorState.originalBytes) {
        return;
    }

    const view = new DataView(setupEditorState.originalBytes.buffer.slice(0));
    setupEditorState.currentValues = decodeSetupValues(view);
    setupEditorState.isDirty = false;
    updateStatus("Changes reverted to the last loaded file.");
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