namespace Rezrov.Glulx.Glk;

/// <summary>
/// One Glk function as the dispatch layer knows it: its selector, its
/// name without the glk_ prefix, and its argument prototype.
/// </summary>
public sealed record GlkSelector(uint Selector, string Name, string Prototype);

/// <summary>
/// [glk #selectors] The table of selectors, with [glk
/// #dispatching_func] the prototype string of each function as the
/// dispatch layer publishes it. Both are copied from the reference
/// dispatch layer, gi_dispa.c, which every Glk library compiles in
/// unchanged, so this list is the library's own description of itself.
/// </summary>
public static class GlkSelectors
{
    private static readonly GlkSelector[] Entries =
    [
        new(0x0001, "exit", "0:"),
        new(0x0002, "set_interrupt_handler", "0:"),
        new(0x0003, "tick", "0:"),
        new(0x0004, "gestalt", "3IuIu:Iu"),
        new(0x0005, "gestalt_ext", "4IuIu&#Iu:Iu"),
        new(0x0020, "window_iterate", "3Qa<Iu:Qa"),
        new(0x0021, "window_get_rock", "2Qa:Iu"),
        new(0x0022, "window_get_root", "1:Qa"),
        new(0x0023, "window_open", "6QaIuIuIuIu:Qa"),
        new(0x0024, "window_close", "2Qa<[2IuIu]:"),
        new(0x0025, "window_get_size", "3Qa<Iu<Iu:"),
        new(0x0026, "window_set_arrangement", "4QaIuIuQa:"),
        new(0x0027, "window_get_arrangement", "4Qa<Iu<Iu<Qa:"),
        new(0x0028, "window_get_type", "2Qa:Iu"),
        new(0x0029, "window_get_parent", "2Qa:Qa"),
        new(0x002A, "window_clear", "1Qa:"),
        new(0x002B, "window_move_cursor", "3QaIuIu:"),
        new(0x002C, "window_get_stream", "2Qa:Qb"),
        new(0x002D, "window_set_echo_stream", "2QaQb:"),
        new(0x002E, "window_get_echo_stream", "2Qa:Qb"),
        new(0x002F, "set_window", "1Qa:"),
        new(0x0030, "window_get_sibling", "2Qa:Qa"),
        new(0x0040, "stream_iterate", "3Qb<Iu:Qb"),
        new(0x0041, "stream_get_rock", "2Qb:Iu"),
        new(0x0042, "stream_open_file", "4QcIuIu:Qb"),
        new(0x0043, "stream_open_memory", "4&#!CnIuIu:Qb"),
        new(0x0044, "stream_close", "2Qb<[2IuIu]:"),
        new(0x0045, "stream_set_position", "3QbIsIu:"),
        new(0x0046, "stream_get_position", "2Qb:Iu"),
        new(0x0047, "stream_set_current", "1Qb:"),
        new(0x0048, "stream_get_current", "1:Qb"),
        new(0x0049, "stream_open_resource", "3IuIu:Qb"),
        new(0x0060, "fileref_create_temp", "3IuIu:Qc"),
        new(0x0061, "fileref_create_by_name", "4IuSIu:Qc"),
        new(0x0062, "fileref_create_by_prompt", "4IuIuIu:Qc"),
        new(0x0063, "fileref_destroy", "1Qc:"),
        new(0x0064, "fileref_iterate", "3Qc<Iu:Qc"),
        new(0x0065, "fileref_get_rock", "2Qc:Iu"),
        new(0x0066, "fileref_delete_file", "1Qc:"),
        new(0x0067, "fileref_does_file_exist", "2Qc:Iu"),
        new(0x0068, "fileref_create_from_fileref", "4IuQcIu:Qc"),
        new(0x0080, "put_char", "1Cu:"),
        new(0x0081, "put_char_stream", "2QbCu:"),
        new(0x0082, "put_string", "1S:"),
        new(0x0083, "put_string_stream", "2QbS:"),
        new(0x0084, "put_buffer", "1>+#Cn:"),
        new(0x0085, "put_buffer_stream", "2Qb>+#Cn:"),
        new(0x0086, "set_style", "1Iu:"),
        new(0x0087, "set_style_stream", "2QbIu:"),
        new(0x0090, "get_char_stream", "2Qb:Is"),
        new(0x0091, "get_line_stream", "3Qb<+#Cn:Iu"),
        new(0x0092, "get_buffer_stream", "3Qb<+#Cn:Iu"),
        new(0x00A0, "char_to_lower", "2Cu:Cu"),
        new(0x00A1, "char_to_upper", "2Cu:Cu"),
        new(0x00B0, "stylehint_set", "4IuIuIuIs:"),
        new(0x00B1, "stylehint_clear", "3IuIuIu:"),
        new(0x00B2, "style_distinguish", "4QaIuIu:Iu"),
        new(0x00B3, "style_measure", "5QaIuIu<Iu:Iu"),
        new(0x00C0, "select", "1<+[4IuQaIuIu]:"),
        new(0x00C1, "select_poll", "1<+[4IuQaIuIu]:"),
        new(0x00D0, "request_line_event", "3Qa&+#!CnIu:"),
        new(0x00D1, "cancel_line_event", "2Qa<[4IuQaIuIu]:"),
        new(0x00D2, "request_char_event", "1Qa:"),
        new(0x00D3, "cancel_char_event", "1Qa:"),
        new(0x00D4, "request_mouse_event", "1Qa:"),
        new(0x00D5, "cancel_mouse_event", "1Qa:"),
        new(0x00D6, "request_timer_events", "1Iu:"),
        new(0x00E0, "image_get_info", "4Iu<Iu<Iu:Iu"),
        new(0x00E1, "image_draw", "5QaIuIsIs:Iu"),
        new(0x00E2, "image_draw_scaled", "7QaIuIsIsIuIu:Iu"),
        new(0x00E8, "window_flow_break", "1Qa:"),
        new(0x00E9, "window_erase_rect", "5QaIsIsIuIu:"),
        new(0x00EA, "window_fill_rect", "6QaIuIsIsIuIu:"),
        new(0x00EB, "window_set_background_color", "2QaIu:"),
        new(0x00EC, "image_draw_scaled_ext", "9QaIuIsIsIuIuIuIu:Iu"),
        new(0x00F0, "schannel_iterate", "3Qd<Iu:Qd"),
        new(0x00F1, "schannel_get_rock", "2Qd:Iu"),
        new(0x00F2, "schannel_create", "2Iu:Qd"),
        new(0x00F3, "schannel_destroy", "1Qd:"),
        new(0x00F4, "schannel_create_ext", "3IuIu:Qd"),
        new(0x00F7, "schannel_play_multi", "4>+#Qd>+#IuIu:Iu"),
        new(0x00F8, "schannel_play", "3QdIu:Iu"),
        new(0x00F9, "schannel_play_ext", "5QdIuIuIu:Iu"),
        new(0x00FA, "schannel_stop", "1Qd:"),
        new(0x00FB, "schannel_set_volume", "2QdIu:"),
        new(0x00FC, "sound_load_hint", "2IuIu:"),
        new(0x00FD, "schannel_set_volume_ext", "4QdIuIuIu:"),
        new(0x00FE, "schannel_pause", "1Qd:"),
        new(0x00FF, "schannel_unpause", "1Qd:"),
        new(0x0100, "set_hyperlink", "1Iu:"),
        new(0x0101, "set_hyperlink_stream", "2QbIu:"),
        new(0x0102, "request_hyperlink_event", "1Qa:"),
        new(0x0103, "cancel_hyperlink_event", "1Qa:"),
        new(0x0120, "buffer_to_lower_case_uni", "3&+#IuIu:Iu"),
        new(0x0121, "buffer_to_upper_case_uni", "3&+#IuIu:Iu"),
        new(0x0122, "buffer_to_title_case_uni", "4&+#IuIuIu:Iu"),
        new(0x0123, "buffer_canon_decompose_uni", "3&+#IuIu:Iu"),
        new(0x0124, "buffer_canon_normalize_uni", "3&+#IuIu:Iu"),
        new(0x0128, "put_char_uni", "1Iu:"),
        new(0x0129, "put_string_uni", "1U:"),
        new(0x012A, "put_buffer_uni", "1>+#Iu:"),
        new(0x012B, "put_char_stream_uni", "2QbIu:"),
        new(0x012C, "put_string_stream_uni", "2QbU:"),
        new(0x012D, "put_buffer_stream_uni", "2Qb>+#Iu:"),
        new(0x0130, "get_char_stream_uni", "2Qb:Is"),
        new(0x0131, "get_buffer_stream_uni", "3Qb<+#Iu:Iu"),
        new(0x0132, "get_line_stream_uni", "3Qb<+#Iu:Iu"),
        new(0x0138, "stream_open_file_uni", "4QcIuIu:Qb"),
        new(0x0139, "stream_open_memory_uni", "4&#!IuIuIu:Qb"),
        new(0x013A, "stream_open_resource_uni", "3IuIu:Qb"),
        new(0x0140, "request_char_event_uni", "1Qa:"),
        new(0x0141, "request_line_event_uni", "3Qa&+#!IuIu:"),
        new(0x0150, "set_echo_line_event", "2QaIu:"),
        new(0x0151, "set_terminators_line_event", "2Qa>#Iu:"),
        new(0x0160, "current_time", "1<+[3IsIuIs]:"),
        new(0x0161, "current_simple_time", "2Iu:Is"),
        new(0x0168, "time_to_date_utc", "2>+[3IsIuIs]<+[8IsIsIsIsIsIsIsIs]:"),
        new(0x0169, "time_to_date_local", "2>+[3IsIuIs]<+[8IsIsIsIsIsIsIsIs]:"),
        new(0x016A, "simple_time_to_date_utc", "3IsIu<+[8IsIsIsIsIsIsIsIs]:"),
        new(0x016B, "simple_time_to_date_local", "3IsIu<+[8IsIsIsIsIsIsIsIs]:"),
        new(0x016C, "date_to_time_utc", "2>+[8IsIsIsIsIsIsIsIs]<+[3IsIuIs]:"),
        new(0x016D, "date_to_time_local", "2>+[8IsIsIsIsIsIsIsIs]<+[3IsIuIs]:"),
        new(0x016E, "date_to_simple_time_utc", "3>+[8IsIsIsIsIsIsIsIs]Iu:Is"),
        new(0x016F, "date_to_simple_time_local", "3>+[8IsIsIsIsIsIsIsIs]Iu:Is"),
    ];

    private static readonly Dictionary<uint, GlkSelector> BySelector =
        Entries.ToDictionary(entry => entry.Selector);

    /// <summary>Every function, in selector order.</summary>
    public static IReadOnlyList<GlkSelector> All => Entries;

    /// <summary>
    /// Finds a function by selector, or null if there is none.
    /// </summary>
    public static GlkSelector? Find(uint selector) =>
        BySelector.TryGetValue(selector, out var entry) ? entry : null;
}
