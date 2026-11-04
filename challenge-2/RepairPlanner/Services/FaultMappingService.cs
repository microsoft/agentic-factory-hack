namespace RepairPlanner.Services;

public interface IFaultMappingService
{
    IReadOnlyList<string> GetRequiredSkills(string faultType);
    IReadOnlyList<string> GetRequiredParts(string faultType);
}

/// <summary>
/// Static fault -> skills / parts mapping. In a real system this would come from another system (CMMS/ERP).
/// </summary>
public sealed class FaultMappingService : IFaultMappingService
{
    private static readonly IReadOnlyList<string> DefaultSkills = ["general_maintenance"];
    private static readonly IReadOnlyList<string> NoParts = [];

    // StringComparer.OrdinalIgnoreCase: fault types match regardless of casing (the diagnosis agent may vary it).
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FaultToSkills =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["curing_temperature_excessive"] = ["tire_curing_press", "temperature_control", "instrumentation", "electrical_systems", "plc_troubleshooting", "mold_maintenance"],
            ["curing_cycle_time_deviation"] = ["tire_curing_press", "plc_troubleshooting", "mold_maintenance", "bladder_replacement", "hydraulic_systems", "instrumentation"],
            ["building_drum_vibration"] = ["tire_building_machine", "vibration_analysis", "bearing_replacement", "alignment", "precision_alignment", "drum_balancing", "mechanical_systems"],
            ["ply_tension_excessive"] = ["tire_building_machine", "tension_control", "servo_systems", "precision_alignment", "sensor_alignment", "plc_programming"],
            ["extruder_barrel_overheating"] = ["tire_extruder", "temperature_control", "rubber_processing", "screw_maintenance", "instrumentation", "electrical_systems", "motor_drives"],
            ["low_material_throughput"] = ["tire_extruder", "rubber_processing", "screw_maintenance", "motor_drives", "temperature_control"],
            ["high_radial_force_variation"] = ["tire_uniformity_machine", "data_analysis", "measurement_systems", "tire_building_machine", "tire_curing_press"],
            ["load_cell_drift"] = ["tire_uniformity_machine", "load_cell_calibration", "measurement_systems", "sensor_alignment", "instrumentation"],
            ["mixing_temperature_excessive"] = ["banbury_mixer", "temperature_control", "rubber_processing", "instrumentation", "electrical_systems", "mechanical_systems"],
            ["excessive_mixer_vibration"] = ["banbury_mixer", "vibration_analysis", "bearing_replacement", "alignment", "mechanical_systems", "preventive_maintenance"],
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FaultToParts =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["curing_temperature_excessive"] = ["TCP-HTR-4KW", "GEN-TS-K400"],
            ["curing_cycle_time_deviation"] = ["TCP-BLD-800", "TCP-SEAL-200"],
            ["building_drum_vibration"] = ["TBM-BRG-6220"],
            ["ply_tension_excessive"] = ["TBM-LS-500N", "TBM-SRV-5KW"],
            ["extruder_barrel_overheating"] = ["EXT-HTR-BAND", "GEN-TS-K400"],
            ["low_material_throughput"] = ["EXT-SCR-250", "EXT-DIE-TR"],
            ["high_radial_force_variation"] = [],
            ["load_cell_drift"] = ["TUM-LC-2KN", "TUM-ENC-5000"],
            ["mixing_temperature_excessive"] = ["BMX-TIP-500", "GEN-TS-K400"],
            ["excessive_mixer_vibration"] = ["BMX-BRG-22320", "BMX-SEAL-DP"],
        };

    public IReadOnlyList<string> GetRequiredSkills(string faultType) =>
        FaultToSkills.TryGetValue(faultType, out var skills) ? skills : DefaultSkills;

    public IReadOnlyList<string> GetRequiredParts(string faultType) =>
        FaultToParts.TryGetValue(faultType, out var parts) ? parts : NoParts;
}
