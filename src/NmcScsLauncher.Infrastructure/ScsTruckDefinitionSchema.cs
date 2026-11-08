using System.Globalization;

namespace NmcScsLauncher.Infrastructure;

/// <summary>
/// Explicit supported SCS accessory schema. Unknown fields/owner units fail closed.
/// Sources: modding.scssoft.com/wiki/Documentation/Engine/Units/{unit type}.
/// Render properties are type checked; accessory interactions are evaluated by the rule graph.
/// </summary>
internal static class ScsTruckDefinitionSchema
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Schemas = new(CreateSchemas);

    public static void Validate(ScsDefinitionDocument document, ScsDefinitionUnit accessory, string gameVersion)
    {
        foreach (var unit in document.Units)
        {
            if (!Schemas.Value.TryGetValue(unit.Type, out var schema))
                throw new InvalidDataException($"Nicht unterstützter Definitionstyp '{unit.Type}'.");
            foreach (var attribute in unit.AttributeNames)
            {
                if (!schema.TryGetValue(attribute, out var type))
                    throw new InvalidDataException($"Unbekanntes Attribut '{attribute}' in {unit.Type}; seine Wirkung ist nicht geprüft.");
                if (type.EndsWith("[]", StringComparison.Ordinal))
                    foreach (var value in unit.GetValues(attribute)) ValidateValue(value, type[..^2], attribute);
                else if (unit.GetValue(attribute) is { } value) ValidateValue(value, type, attribute);
                if (unit.Type.StartsWith("accessory_", StringComparison.Ordinal) && attribute is "suitable_for" or "conflict_with" or "require" or "defaults" or "overrides" && unit.GetValues(attribute).Count > 256)
                    throw new InvalidDataException("Die Definition enthält zu viele Zubehörregeln.");
            }
        }
        if (!Version.TryParse(gameVersion, out var version)) throw new InvalidDataException("Spielversion ist nicht bekannt.");
        foreach (var (field, minimum) in new[] { ("part_type", 44), ("reverse_crawls", 60), ("power", 60), ("secondary_power", 60),
            ("cold_start_rpm", 61), ("flywheel_inertia", 61), ("rear_wiper_anim", 61), ("wiper_main_cycle", 61), ("use_non_wheel_pin_placement_from_model", 61) })
            if (accessory.HasAttribute(field) && version.Minor < minimum)
                throw new InvalidDataException($"Attribut '{field}' benötigt mindestens Spielversion 1.{minimum}.");
        if (accessory.HasAttribute("steam_inventory_id") && Integer(accessory.GetToken("steam_inventory_id")!) != 0 ||
            !string.IsNullOrEmpty(accessory.GetString("academy_reward")))
            throw new InvalidDataException("Die erforderliche Inventar-/Belohnungsberechtigung ist nicht bestätigt.");
        if (accessory.GetToken("electric_type") is { } electricType && (electricType != "vehicle_interior" || version.Minor < 47))
            throw new InvalidDataException("Dieser elektrische Zubehör-Modus ist nicht bestätigt.");
        ValidateOwners(document, accessory, new HashSet<string>(StringComparer.Ordinal));
        if (accessory.Type == "accessory_engine_data")
        {
            if (!accessory.Id.EndsWith(".engine", StringComparison.Ordinal)) throw new InvalidDataException("Die Motor-Unit besitzt nicht den erforderlichen Suffix .engine.");
            if (Number(accessory.GetToken("torque") ?? "0") <= 0) throw new InvalidDataException("Ein positiver Motor-Drehmomentwert fehlt.");
            if ((accessory.GetToken("type") ?? "diesel") != "diesel")
                throw new InvalidDataException("Dieser Motortyp benötigt eine zusätzliche, nicht unterstützte Zustandsprüfung.");
            if (accessory.HasAttribute("engine_aspiration") && accessory.GetToken("engine_aspiration") is not ("natural" or "turbocharger" or "supercharger"))
                throw new InvalidDataException("Unbekannte Motoraufladung.");
            ValidateBrakeCount(accessory, "engine_brake_positions", 3);
            if (accessory.GetToken("rpm_idle") is { } idle && Number(idle) is < 500 or > 850)
                throw new InvalidDataException("rpm_idle liegt außerhalb des dokumentierten Bereichs 500–850.");
            if (accessory.GetToken("volume") is { } volume && Number(volume) < 0.8)
                throw new InvalidDataException("Das angegebene Motorvolumen unterschreitet das dokumentierte Minimum 0,8 Liter.");
            foreach (var name in new[] { "torque_curve", "secondary_torque_curve" })
            {
                var last = -1d;
                foreach (var point in accessory.GetValues(name))
                {
                    var rpm = Number(point.Elements[0].Text);
                    if (rpm <= last || Number(point.Elements[1].Text) is < 0 or > 1) throw new InvalidDataException("Drehmomentkurve ist nicht eindeutig aufsteigend oder verlässt den dokumentierten relativen Bereich 0–1.");
                    last = rpm;
                }
            }
        }
        if (accessory.Type == "accessory_transmission_data")
        {
            if (!accessory.Id.EndsWith(".transmission", StringComparison.Ordinal)) throw new InvalidDataException("Die Getriebe-Unit besitzt nicht den erforderlichen Suffix .transmission.");
            var forwards = accessory.GetTokens("ratios_forward").Select(Number).ToArray();
            var reverse = accessory.GetTokens("ratios_reverse").Select(Number).ToArray();
            if (forwards.Length is < 1 or > 64 || reverse.Length is < 1 or > 16 || forwards.Any(v => v <= 0) || reverse.Any(v => v >= 0) ||
                Number(accessory.GetToken("differential_ratio") ?? "1") <= 0)
                throw new InvalidDataException("Getriebeübersetzungen fehlen oder haben ungültige Vorzeichen/Werte.");
            if (Integer(accessory.GetToken("crawls") ?? "0") > forwards.Length || Integer(accessory.GetToken("reverse_crawls") ?? "0") > reverse.Length)
                throw new InvalidDataException("Die Anzahl der Kriechgänge überschreitet die vorhandenen Gänge.");
            ValidateBrakeCount(accessory, "retarder", 0);
            var pointer = accessory.GetToken("transmission_names");
            if (pointer is not null and not "null")
            {
                var names = document.Units.Single(unit => unit.Id == pointer);
                if (names.HasAttribute("forward") && names.GetStrings("forward").Count != forwards.Length ||
                    names.HasAttribute("reverse") && names.GetStrings("reverse").Count != reverse.Length)
                    throw new InvalidDataException("Gangnamen stimmen nicht mit der Anzahl der Übersetzungen überein.");
            }
        }
        if (accessory.Type == "accessory_truck_data")
            foreach (var fallback in accessory.GetStrings("fallback"))
            {
                var parts = fallback.Split('|');
                if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || parts[0].Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
                    throw new InvalidDataException("Nicht unterstützte Truck-Fallback-Regel.");
                _ = ScsTruckPowertrainRules.Normalize(parts[1], "/def/vehicle/truck/model/" + parts[0] + "/fallback.sii");
            }
    }

    private static void ValidateOwners(ScsDefinitionDocument document, ScsDefinitionUnit root, HashSet<string> visited)
    {
        if (!visited.Add(root.Id)) return;
        var owners = root.Type switch
        {
            "accessory_transmission_data" => new[] { ("transmission_names", "transmission_names", false) },
            "accessory_interior_data" => new[] { ("glass_panes", "glass_pane_data", true) }
                .Concat(InteriorSoundOwners.Select(owner => (owner.Key, owner.Value, false))).ToArray(),
            "glass_pane_data" => new[] { ("wipers", "wiper_data", true) },
            "accessory_addon_trailer_cables_data" => new[] { ("cables", "trailer_cable_data", true) },
            _ => Array.Empty<(string, string, bool)>()
        };
        foreach (var (attribute, expected, array) in owners)
        {
            var targets = array ? root.GetTokens(attribute) : root.GetToken(attribute) is { } target ? new[] { target } : Array.Empty<string>();
            foreach (var id in targets.Where(id => id != "null"))
            {
                var match = document.Units.SingleOrDefault(unit => unit.Id == id);
                if (match is null || match.Type != expected) throw new InvalidDataException($"Owner-Referenz '{id}' ist nicht als {expected} aufgelöst.");
                ValidateOwners(document, match, visited);
            }
        }
        // Every additional unit must have a known, checked owner; unused units can otherwise hide unsupported behavior.
        if (ReferenceEquals(root, document.Units.FirstOrDefault(unit => unit.Type.StartsWith("accessory_", StringComparison.Ordinal))) &&
            document.Units.Any(unit => !visited.Contains(unit.Id) && !(root.Type == "accessory_paint_job_data" && unit.Type == "simple_paint_job_data")))
            throw new InvalidDataException("Zusätzliche Units sind keinem unterstützten Owner eindeutig zugeordnet.");
    }

    private static void ValidateBrakeCount(ScsDefinitionUnit unit, string countAttribute, int defaultCount)
    {
        var brakes = unit.GetValues("auxiliary_brakes");
        if (brakes.Count > 0 && brakes.Count != Integer(unit.GetToken(countAttribute) ?? defaultCount.ToString(CultureInfo.InvariantCulture)))
            throw new InvalidDataException("auxiliary_brakes stimmt nicht mit der Anzahl der Bremsstufen überein.");
        if (brakes.Any(value => value.Elements.Select(element => Number(element.Text)).Any(number => number < 0 || number > 1)))
            throw new InvalidDataException("Ungültiger relativer Wert in auxiliary_brakes.");
    }

    private static void ValidateValue(ScsDefinitionValue value, string type, string attribute)
    {
        bool valid;
        switch (type)
        {
            case "string": valid = value.Kind == ScsDefinitionValueKind.String; break;
            case "atom": valid = value.Kind is ScsDefinitionValueKind.String or ScsDefinitionValueKind.Token; break;
            case "token": valid = value.Kind == ScsDefinitionValueKind.Token; break;
            case "float": valid = value.Kind == ScsDefinitionValueKind.Token; if (valid) _ = Number(value.Text); break;
            case "uint": valid = value.Kind == ScsDefinitionValueKind.Token && uint.TryParse(value.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _); break;
            case "flags":
                valid = value.Kind == ScsDefinitionValueKind.Token && (uint.TryParse(value.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                    value.Text.StartsWith("0x", StringComparison.Ordinal) && value.Text.Length is > 2 and <= 10 &&
                    uint.TryParse(value.Text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _));
                break;
            case "int": valid = value.Kind == ScsDefinitionValueKind.Token && long.TryParse(value.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _); break;
            case "bool": valid = value.Kind == ScsDefinitionValueKind.Token && value.Text is "true" or "false"; break;
            case "float2": case "float3": case "float4":
                valid = value.Kind == ScsDefinitionValueKind.Tuple && value.Elements.Count == type[^1] - '0' && value.Separators.All(c => c == ',') && value.Elements.All(v => v.Kind == ScsDefinitionValueKind.Token);
                if (valid) foreach (var element in value.Elements) _ = Number(element.Text);
                break;
            default: throw new InvalidDataException("Unbekanntes Prüfschema.");
        }
        if (!valid) throw new InvalidDataException($"Attribut '{attribute}' hat nicht den erwarteten Typ {type}.");
    }

    internal static double Number(string value)
    {
        double result;
        if (value.Length == 9 && value[0] == '&' && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bits))
            result = BitConverter.Int32BitsToSingle(unchecked((int)bits));
        else if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            throw new InvalidDataException("Ungültiger numerischer Definitionswert.");
        if (!double.IsFinite(result) || !float.IsFinite((float)result)) throw new InvalidDataException("Nicht endlicher oder nicht als SCS-float darstellbarer Definitionswert.");
        // SCS float fields are IEEE-754 single precision, including decimal literals and underflow.
        return (float)result;
    }
    private static long Integer(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= 0
        ? result : throw new InvalidDataException("Ungültiger nichtnegativer ganzzahliger Wert.");

    private static Dictionary<string, Dictionary<string, string>> CreateSchemas()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var common = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(common, "string", "name short_name icon"); Add(common, "string[]", "info suitable_for conflict_with defaults overrides");
        Add(common, "int", "price"); Add(common, "uint", "unlock steam_inventory_id"); Add(common, "atom", "part_type"); Add(common, "token[]", "require"); Add(common, "string", "academy_reward");
        Dictionary<string, string> Accessory(string name) { var schema = new Dictionary<string, string>(common, StringComparer.Ordinal); result.Add(name, schema); return schema; }
        var engine = Accessory("accessory_engine_data");
        Add(engine, "token", "type engine_aspiration sound_location_engine sound_location_exhaust");
        Add(engine, "float", "torque secondary_torque secondary_torque_gear_ratio power secondary_power resistance_torque rpm_idle rpm_limit rpm_limit_hysteresis rpm_limit_neutral rpm_limit_neutral_hysteresis engine_brake volume consumption_coef cold_start_rpm flywheel_inertia adblue_consumption no_adblue_power_limit");
        Add(engine, "float2[]", "torque_curve secondary_torque_curve");
        Add(engine, "float2", "rpm_range_low_gear rpm_range_high_gear rpm_range_power rpm_range_power_boost rpm_range_engine_brake");
        Add(engine, "bool", "engine_brake_downshift"); Add(engine, "uint", "engine_brake_positions"); Add(engine, "float3[]", "auxiliary_brakes"); Add(engine, "string[]", "sounds");
        var transmission = Accessory("accessory_transmission_data");
        Add(transmission, "float", "stall_torque_ratio differential_ratio shift_time"); Add(transmission, "uint", "torque_converter_lock_gear retarder crawls reverse_crawls");
        Add(transmission, "token", "transmission_class transmission_names"); Add(transmission, "bool", "torque_converter_crawl");
        Add(transmission, "float3[]", "auxiliary_brakes"); Add(transmission, "float[]", "ratios_forward ratios_reverse"); Add(transmission, "string", "auto_mode_name manual_mode_name"); Add(transmission, "string[]", "sounds");
        var truck = Accessory("accessory_truck_data");
        Add(truck, "token", "behind_camera interior_camera interior_camera_oculus window_camera bumper_camera cabin_camera wheel_camera top_camera top_fixed_camera tv_camera predefined_tv_camera side_camera wander_camera debug_camera");
        Add(truck, "string[]", "fallback"); Add(truck, "token[]", "pin_group pin_look"); Add(truck, "float3[]", "pin_offset pin_dir pin_dir_uk"); Add(truck, "bool[]", "double_sided");
        Add(truck, "bool", "use_non_wheel_pin_placement_from_model");
        var cabin = Accessory("accessory_cabin_data");
        Add(cabin, "string", "detail_model model collision wiper_model wiper_anim rear_wiper_anim wiper_model_uk wiper_anim_uk windows_model left_window_anim right_window_anim");
        Add(cabin, "string[]", "lods sounds"); Add(cabin, "token", "variant variant_uk look suspension_type"); Add(cabin, "float", "drl_with_headlights"); Add(cabin, "bool", "suspension");
        var chassis = Accessory("accessory_chassis_data");
        Add(chassis, "string", "detail_model model collision shadow_texture extended_shadow_texture ui_shadow");
        Add(chassis, "string[]", "lods pendle_axle_model brake_caliper_model brake_rotor_model solid_axle_suspension_model sounds");
        Add(chassis, "token", "variant variant_uk look extended_shadow_shared_model_variant suspension_class");
        Add(chassis, "float", "shadow_intensity extended_shadow_intensity extended_shadow_fadeout_start extended_shadow_fadeout_length tank_size adblue_tank_size air_tank_pressure nominal_voltage air_resistance caster_angle sway_bar_stiffness steering_pivot_point_offset cog_height cog_cargo_mass_min cog_cargo_mass_max");
        Add(chassis, "float[]", "residual_travel kerb_weight brake_rotor_diameter suspension_damping_factor");
        Add(chassis, "bool[]", "powered_axle disc_brake liftable_axle"); Add(chassis, "token[]", "axle_suspension_type steerable_axle");
        Add(chassis, "bool", "steerable_lifted_axles"); Add(chassis, "uint", "max_steering_angle"); Add(chassis, "float3", "cog_cargo_offset_min cog_cargo_offset_max");
        var interior = Accessory("accessory_interior_data");
        Add(interior, "string", "model animated_model collision ext_model dashboard_path gps_path"); Add(interior, "token", "variant look ext_variant ext_look");
        Add(interior, "float2[]", "wiper_duration_and_delay"); Add(interior, "float2", "wiper_main_cycle");
        Add(interior, "bool", "show_chassis_rear use_mile_units left_hand_traffic backlight_with_lights air_brake_safeguard");
        Add(interior, "float", "blinker_delay panel_intensity_min panel_intensity_max seat_left_limit seat_right_limit seat_up_limit seat_down_limit seat_front_limit seat_back_limit");
        Add(interior, "token[]", "glass_panes"); Add(interior, "float3[]", "dashboard_color"); Add(interior, "string[]", "sounds");
        Add(interior, "token", "active_mirrors_part passive_mirrors_part"); Add(interior, "string", "shadow_proxy_csv");
        // Explicit rendering/animation fields observed in the original game, never wildcard-accept unknown attributes.
        Add(interior, "string", "ad_blue_anim air_pressure_anim blinker_anim button_battery_warning button_high_beam_lights button_left_blinker button_low_fuel button_parking_brake button_right_blinker differential_lock_stick_anim flasher_4way_anim fuel_anim generic_anim high_beam_anim indicator_axle_lift indicator_differential_lock indicator_low_adblue indicator_low_air_pressure indicator_low_oil_pressure left_window_anim left_window_stick_anim lift_axle_stick_anim light_horn_stick_anim lights_anim oil_pressure_anim parking_stick_anim retarder_stick_anim right_window_anim right_window_stick_anim speedometer_anim tachometer_anim turbo_anim water_temp_anim wheel_frontback_anim wheel_updown_anim wiper_anim wiper_stick_anim");
        Add(interior, "float", "air_pressure_anim_max air_pressure_anim_min generic_anim_max oil_pressure_anim_max speedometer_anim_max speedometer_anim_min tachometer_anim_max tachometer_anim_min water_temp_anim_max water_temp_anim_min wheel_frontback_anim_default wheel_updown_anim_default");
        foreach (var field in InteriorSoundOwners.Keys) interior.Add(field, "token");
        foreach (var type in new[] { "accessory_addon_data", "accessory_addon_int_data", "accessory_addon_patch_data", "accessory_addon_trailer_cables_data" })
        {
            var addon = Accessory(type); Add(addon, "string", "exterior_model exterior_model_uk interior_model interior_model_uk coll");
            Add(addon, "atom", "variant look"); Add(addon, "flags", "hide_in"); Add(addon, "token", "electric_type");
            if (type == "accessory_addon_trailer_cables_data") Add(addon, "token[]", "cables");
        }
        foreach (var type in new[] { "accessory_wheel_data", "accessory_rim_data" })
        {
            var wheel = Accessory(type); Add(wheel, "string", "model"); Add(wheel, "token", "look"); Add(wheel, "bool", "paintable"); Add(wheel, "float3", "default_color");
            if (type == "accessory_wheel_data") { Add(wheel, "float", "roll_resistance wet_grip grip_factor noise_volume wheel_shift"); Add(wheel, "string[]", "sounds"); }
            else Add(wheel, "float", "tire_shift");
        }
        var paint = Accessory("accessory_paint_job_data");
        Add(paint, "float3", "mask_r_color mask_g_color mask_b_color base_color flip_color flake_color");
        Add(paint, "bool", "mask_r_locked mask_g_locked mask_b_locked base_color_locked flip_color_locked flake_color_locked flipflake airbrush alternate_uvset stock");
        Add(paint, "float", "flip_strength flake_uvscale flake_density flake_shininess flake_clearcoat_rolloff"); Add(paint, "string", "flake_noise paint_job_mask base_texture_override");
        Add(paint, "float3[]", "color_variant");
        var simplePaint = result["simple_paint_job_data"] = new(StringComparer.Ordinal);
        Add(simplePaint, "string[]", "acc_list"); Add(simplePaint, "string", "paint_job_mask"); Add(simplePaint, "float", "flake_uvscale flake_vratio");
        var lights = Accessory("accessory_head_lights_data");
        Add(lights, "float", "reflectors_distance"); Add(lights, "float3", "reflectors_offset left_rot right_rot"); Add(lights, "bool", "no_parking_light");
        foreach (var prefix in new[] { "front_beam", "front_roof_beam", "hi_beam", "low_beam", "roof_beam" })
        {
            foreach (var field in new[] { "angle", "aspect", "bias", "range", "refracted_angle", "refracted_aspect", "refracted_bias", "refracted_color_fraction", "refracted_pitch", "refracted_range" }) lights.Add(prefix + "_" + field, "float");
            foreach (var field in new[] { "color", "color_ambient", "color_specular", "left_rot", "right_rot" }) lights.Add(prefix + "_" + field, "float3");
            lights.Add(prefix + "_refracted_offset_yz", "float2");
            foreach (var field in new[] { "mask", "refracted_mask" }) lights.Add(prefix + "_" + field, "string");
        }
        var names = result["transmission_names"] = new(StringComparer.Ordinal); Add(names, "string", "neutral"); Add(names, "string[]", "forward reverse");
        var glass = result["glass_pane_data"] = new(StringComparer.Ordinal); Add(glass, "atom", "locator openable_window_bone"); Add(glass, "string", "mask_texture"); Add(glass, "float2", "size"); Add(glass, "float", "drop_size_minimum drop_size_maximum edge_angle"); Add(glass, "token[]", "wipers");
        var wiper = result["wiper_data"] = new(StringComparer.Ordinal); Add(wiper, "atom", "center_bone"); Add(wiper, "float", "length");
        var reverb = result["sound_reverb_data"] = new(StringComparer.Ordinal);
        Add(reverb, "float", "decay_time density diffusion dry_level early_delay early_late_mix hf_decay_ratio hf_reference high_cut late_delay low_shelf_frequency low_shelf_gain wet_level");
        var eq = result["sound_three_eq_data"] = new(StringComparer.Ordinal); Add(eq, "float", "crossover_slope high_crossover high_gain low_crossover low_gain mid_gain");
        var nodes = result["sound_multiband_eq_node_data"] = new(StringComparer.Ordinal); Add(nodes, "token", "node_a node_b node_c node_d node_e");
        var parameters = result["sound_multiband_eq_param_data"] = new(StringComparer.Ordinal);
        foreach (var band in new[] { "a", "b", "c", "d", "e" }) foreach (var field in new[] { "frequency", "gain", "quality" }) parameters.Add(band + "_" + field, "float");
        var cable = result["trailer_cable_data"] = new(StringComparer.Ordinal);
        Add(cable, "token", "locator_prefix rest_locator_prefix vehicle_plug_model_variant vehicle_plug_model_look trailer_plug_model_variant trailer_plug_model_look");
        Add(cable, "string", "vehicle_plug_model trailer_plug_model material"); Add(cable, "float3", "vehicle_plug_model_offset trailer_plug_model_offset color");
        Add(cable, "uint", "ring_count ring_resolution resolution"); Add(cable, "float", "diameter width straight_cable_length"); Add(cable, "bool", "back_faces");
        return result;
    }
    private static IReadOnlyDictionary<string, string> InteriorSoundOwners { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["effect_reverb_window_closed"] = "sound_reverb_data", ["effect_reverb_window_opened"] = "sound_reverb_data",
        ["effect_equalizer_cabin_inside"] = "sound_three_eq_data", ["effect_equalizer_cabin_outside"] = "sound_three_eq_data",
        ["effect_add_window_eq_closed"] = "sound_three_eq_data", ["effect_add_window_eq_opened"] = "sound_three_eq_data",
        ["effect_multiband_eq_cabin_nodes"] = "sound_multiband_eq_node_data", ["effect_add_window_multiband_eq_nodes"] = "sound_multiband_eq_node_data",
        ["effect_multiband_eq_cabin_inside"] = "sound_multiband_eq_param_data", ["effect_multiband_eq_cabin_outside"] = "sound_multiband_eq_param_data",
        ["effect_add_window_multiband_eq_closed"] = "sound_multiband_eq_param_data", ["effect_add_window_multiband_eq_opened"] = "sound_multiband_eq_param_data"
    };
    private static void Add(Dictionary<string, string> target, string type, string names)
    { foreach (var name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries)) target.Add(name, type); }
}
