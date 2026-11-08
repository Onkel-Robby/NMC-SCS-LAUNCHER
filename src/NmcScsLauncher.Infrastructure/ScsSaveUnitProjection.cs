using System.Globalization;
using System.Text;

namespace NmcScsLauncher.Infrastructure;

/// <summary>
/// Validates every field of a save unit while retaining only bounded truck-related fields.
/// This is deliberately a one-field-per-line save grammar, matching the save writer.
/// It does not relax the separate definition-document parser's limits.
/// </summary>
internal sealed class ScsSaveUnitProjection
{
    private const int MaximumFieldFamilies = 4096;
    private const int MaximumArrayEntries = 2_000_000;
    private const int MaximumBitmapSlots = 8_000_000;
    private const int MaximumBodyFields = 4_000_000;
    private const int MaximumProjectionCharacters = 4 * 1024 * 1024;
    private const int MaximumCapturedArrayEntries = 10_000;
    private const int MaximumTupleDepth = 8;
    private const int MaximumTupleElements = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly ScsSiiUnit _unit;
    private readonly bool _capture;
    private readonly Dictionary<string, FieldState> _fields = new(StringComparer.Ordinal);
    private readonly StringBuilder? _projection;
    private readonly Decoder _stringDecoder = StrictUtf8.GetDecoder();
    private int _bodyFields;
    private int _bitmapSlots;
    private bool _complete;

    internal ScsSaveUnitProjection(ScsSiiUnit unit, bool capture)
    {
        _unit = unit;
        _capture = capture;
        if (capture)
        {
            _projection = new StringBuilder();
            AppendProjection("SiiNunit\n{\n");
            AppendProjection(unit.Type);
            AppendProjection(" : ");
            AppendProjection(unit.Id);
            AppendProjection(" {\n");
        }
    }

    internal bool HasAccessories { get; private set; }

    internal void AddLine(ReadOnlySpan<char> line)
    {
        if (_complete) throw new InvalidOperationException("Die Unit-Prüfung ist bereits abgeschlossen.");
        if (line.Length > 0 && line[^1] == '\r') line = line[..^1];
        var position = 0;
        SkipWhitespace(line, ref position);
        if (position == line.Length || IsLineComment(line, position)) return;
        if (++_bodyFields > MaximumBodyFields) throw Invalid("SII-Unit überschreitet das Feldlimit.");

        var fieldStart = position;
        if (!IsIdentifierStart(line[position])) throw Invalid("Ungültiger SII-Feldname.");
        position++;
        while (position < line.Length && IsIdentifierPart(line[position])) position++;
        var key = line[fieldStart..position];
        if (key.Length > 128) throw Invalid("SII-Feldname überschreitet das Größenlimit.");
        SkipWhitespace(line, ref position);

        var append = false;
        int? arrayIndex = null;
        if (At(line, position, '['))
        {
            position++;
            SkipWhitespace(line, ref position);
            if (At(line, position, ']')) append = true;
            else
            {
                var indexStart = position;
                while (position < line.Length && char.IsAsciiDigit(line[position])) position++;
                if (indexStart == position ||
                    !int.TryParse(line[indexStart..position], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    index >= MaximumArrayEntries)
                    throw Invalid("Ungültiger oder zu großer SII-Arrayindex.");
                arrayIndex = index;
                SkipWhitespace(line, ref position);
            }
            Consume(line, ref position, ']');
            SkipWhitespace(line, ref position);
        }
        Consume(line, ref position, ':');
        SkipWhitespace(line, ref position);
        var valueStart = position;
        var valueKind = ReadValue(line, ref position, 0);
        var valueEnd = position;
        SkipWhitespace(line, ref position);
        if (valueKind == ValueKind.Tuple && At(line, position, '('))
        {
            _ = ReadValue(line, ref position, 0);
            valueKind = ValueKind.Placement;
            valueEnd = position;
            SkipWhitespace(line, ref position);
        }
        if (position != line.Length && !IsLineComment(line, position))
            throw Invalid("Eine SII-Zeile muss genau ein vollständiges Feld enthalten.");

        var lookup = _fields.GetAlternateLookup<ReadOnlySpan<char>>();
        if (!lookup.TryGetValue(key, out var field))
        {
            if (_fields.Count >= MaximumFieldFamilies) throw Invalid("SII-Unit überschreitet das Feldfamilienlimit.");
            var name = key.ToString();
            field = new FieldState(name, _capture && CaptureField(_unit.Type, name));
            _fields.Add(name, field);
            HasAccessories |= name == "accessories";
        }
        int? scalarCount = valueKind == ValueKind.Token &&
            int.TryParse(line[valueStart..valueEnd], NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;
        field.Add(this, append, arrayIndex, scalarCount);
        if (field.Capture)
        {
            AppendProjection(line[fieldStart..valueEnd]);
            AppendProjection("\n");
        }
    }

    internal void Complete()
    {
        if (_complete) return;
        foreach (var field in _fields.Values)
        {
            field.Complete();
            // Attribute names remain visible to unsupported-accessory checks. Irrelevant
            // values have already been validated and are intentionally not materialized.
            if (_capture && !field.Capture)
            {
                AppendProjection(field.Name);
                AppendProjection(": 0\n");
            }
        }
        if (_capture) AppendProjection("}\n}");
        _complete = true;
    }

    internal ScsDefinitionUnit ToDefinition()
    {
        if (!_capture || !_complete) throw new InvalidOperationException("Es liegt keine abgeschlossene SII-Projektion vor.");
        try { return ScsDefinitionDocument.Parse(_projection!.ToString()).Units.Single(); }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            // The detail parser's errors may include raw unit IDs; keep this diagnostic content-free.
            throw Invalid("Benötigte SII-Felder können nicht eindeutig interpretiert werden.");
        }
    }

    private static bool CaptureField(string type, string field) => field == "accessories" || (type switch
    {
        "economy" => field == "player",
        "player" => field is "trucks" or "assigned_truck" or "assigned_vehicles",
        "player_vehicles" => field is "vehicle" or "trailer",
        _ => field is "data_path" or "slot_name" or "slot_hookup"
    });

    private void AppendProjection(ReadOnlySpan<char> text)
    {
        if (_projection!.Length > MaximumProjectionCharacters - text.Length)
            throw Invalid("Benötigte SII-Felder überschreiten das Projektionslimit.");
        _projection.Append(text);
    }

    private ValueKind ReadValue(ReadOnlySpan<char> line, ref int position, int depth)
    {
        if (position >= line.Length) throw Invalid("Unvollständiger SII-Feldwert.");
        if (line[position] == '"')
        {
            ReadQuoted(line, ref position);
            return ValueKind.String;
        }
        if (line[position] == '(')
        {
            if (depth >= MaximumTupleDepth) throw Invalid("SII-Tupel überschreitet das Verschachtelungslimit.");
            position++;
            SkipWhitespace(line, ref position);
            _ = ReadValue(line, ref position, depth + 1);
            var elements = 1;
            SkipWhitespace(line, ref position);
            while (!At(line, position, ')'))
            {
                if (position >= line.Length || line[position] is not (',' or ';'))
                    throw Invalid("Unvollständiges oder ungültiges SII-Tupel.");
                if (++elements > MaximumTupleElements) throw Invalid("SII-Tupel überschreitet das Elementlimit.");
                position++;
                SkipWhitespace(line, ref position);
                _ = ReadValue(line, ref position, depth + 1);
                SkipWhitespace(line, ref position);
            }
            position++;
            return ValueKind.Tuple;
        }
        var start = position;
        while (position < line.Length)
        {
            var character = line[position];
            if (character is ' ' or '\t' || "{}:[](),;\"#@".Contains(character) ||
                character == '/' && position + 1 < line.Length && line[position + 1] is '/' or '*') break;
            if (char.IsControl(character) || char.IsWhiteSpace(character) || character == '\uFEFF')
                throw Invalid("Nicht unterstütztes Steuer- oder Leerzeichen im SII-Wert.");
            if (char.IsHighSurrogate(character))
            {
                if (position + 1 >= line.Length || !char.IsLowSurrogate(line[position + 1]))
                    throw Invalid("Ungültiges Unicode im SII-Wert.");
                position += 2;
            }
            else
            {
                if (char.IsLowSurrogate(character)) throw Invalid("Ungültiges Unicode im SII-Wert.");
                position++;
            }
        }
        if (position == start) throw Invalid("SII-Wert enthält eine nicht unterstützte Struktur.");
        return ValueKind.Token;
    }

    private void ReadQuoted(ReadOnlySpan<char> line, ref int position)
    {
        _stringDecoder.Reset();
        Span<byte> bytes = stackalloc byte[512];
        // A buffered UTF-8 prefix can emit a surrogate pair before this batch's ASCII bytes.
        Span<char> characters = stackalloc char[514];
        var used = 0;
        position++;
        try
        {
            while (position < line.Length)
            {
                if (used > bytes.Length - 4)
                {
                    Decode(bytes[..used], characters, flush: false);
                    used = 0;
                }
                var character = line[position++];
                if (character == '"')
                {
                    Decode(bytes[..used], characters, flush: true);
                    return;
                }
                if (char.IsControl(character)) throw Invalid("Nicht escaptes Steuerzeichen im SII-String.");
                if (character == '\\')
                {
                    if (position >= line.Length) throw Invalid("Unvollständige SII-Escape-Sequenz.");
                    var escape = line[position++];
                    switch (escape)
                    {
                        case '\\': bytes[used++] = (byte)'\\'; break;
                        case '"': bytes[used++] = (byte)'"'; break;
                        case 'n': bytes[used++] = (byte)'\n'; break;
                        case 'r': bytes[used++] = (byte)'\r'; break;
                        case 't': bytes[used++] = (byte)'\t'; break;
                        case 'x':
                            if (position + 1 >= line.Length || !byte.TryParse(line.Slice(position, 2), NumberStyles.AllowHexSpecifier,
                                    CultureInfo.InvariantCulture, out var value))
                                throw Invalid("Ungültige SII-Byte-Escape-Sequenz.");
                            bytes[used++] = value;
                            position += 2;
                            break;
                        default: throw Invalid("Nicht unterstützte SII-Escape-Sequenz.");
                    }
                }
                else if (character <= 0x7f) bytes[used++] = (byte)character;
                else
                {
                    var length = char.IsHighSurrogate(character) && position < line.Length && char.IsLowSurrogate(line[position]) ? 2 : 1;
                    var start = position - 1;
                    if (length == 2) position++;
                    used += StrictUtf8.GetBytes(line.Slice(start, length), bytes[used..]);
                }
            }
        }
        catch (Exception exception) when (exception is EncoderFallbackException or DecoderFallbackException)
        {
            throw Invalid("Ungültiges UTF-8 im SII-String.");
        }
        throw Invalid("Unvollständiger oder mehrzeiliger SII-String.");
    }

    private void Decode(ReadOnlySpan<byte> bytes, Span<char> characters, bool flush)
    {
        _stringDecoder.Convert(bytes, characters, flush, out var consumed, out _, out var complete);
        if (consumed != bytes.Length || !complete) throw Invalid("SII-String kann nicht begrenzt validiert werden.");
    }

    private static bool At(ReadOnlySpan<char> line, int position, char expected) => position < line.Length && line[position] == expected;
    private static bool IsLineComment(ReadOnlySpan<char> line, int position) => At(line, position, '#') ||
        At(line, position, '/') && At(line, position + 1, '/');
    private static void SkipWhitespace(ReadOnlySpan<char> line, ref int position)
    { while (position < line.Length && line[position] is ' ' or '\t') position++; }
    private static void Consume(ReadOnlySpan<char> line, ref int position, char expected)
    {
        if (!At(line, position, expected)) throw Invalid("Ungültige oder unvollständige SII-Feldstruktur.");
        position++;
    }
    private static bool IsIdentifierStart(char value) => char.IsAsciiLetter(value) || value == '_';
    private static bool IsIdentifierPart(char value) => char.IsAsciiLetterOrDigit(value) || value == '_';
    private static InvalidDataException Invalid(string message) => new(message);
    private enum ValueKind { Token, String, Tuple, Placement }

    private sealed class FieldState(string name, bool capture)
    {
        private bool _hasScalar;
        private int? _declaredCount;
        private bool _hasAppend;
        private bool _hasIndexed;
        private int _entries;
        private int _highestIndex = -1;
        private uint[] _indices = [];
        internal string Name { get; } = name;
        internal bool Capture { get; } = capture;

        internal void Add(ScsSaveUnitProjection owner, bool append, int? index, int? scalarCount)
        {
            if (!append && index is null)
            {
                if (_hasScalar) throw Invalid("SII-Skalar ist mehrfach vorhanden.");
                _hasScalar = true;
                _declaredCount = scalarCount;
                return;
            }
            if (Capture && (index >= MaximumCapturedArrayEntries || _entries >= MaximumCapturedArrayEntries))
                throw Invalid("Benötigte SII-Referenzliste überschreitet das Leselimit.");
            if (_entries >= MaximumArrayEntries) throw Invalid("SII-Array überschreitet das Elementlimit.");
            if (append)
            {
                if (_hasIndexed) throw Invalid("Gemischte SII-Arraydarstellung.");
                _hasAppend = true;
            }
            else
            {
                if (_hasAppend) throw Invalid("Gemischte SII-Arraydarstellung.");
                _hasIndexed = true;
                var entry = index!.Value;
                var requiredWords = entry / 32 + 1;
                if (requiredWords > _indices.Length)
                {
                    var words = Math.Min(MaximumArrayEntries / 32, Math.Max(requiredWords, Math.Max(4, _indices.Length * 2)));
                    var extraSlots = (words - _indices.Length) * 32;
                    if (owner._bitmapSlots > MaximumBitmapSlots - extraSlots)
                        throw Invalid("SII-Unit überschreitet das Speicherlimit der Arrayprüfung.");
                    owner._bitmapSlots += extraSlots;
                    Array.Resize(ref _indices, words);
                }
                var mask = 1u << (entry % 32);
                if ((_indices[entry / 32] & mask) != 0) throw Invalid("SII-Arrayindex ist mehrfach vorhanden.");
                _indices[entry / 32] |= mask;
                _highestIndex = Math.Max(_highestIndex, entry);
            }
            _entries++;
        }

        internal void Complete()
        {
            if (_hasIndexed && _highestIndex + 1 != _entries) throw Invalid("SII-Array enthält Indexlücken.");
            if ((_hasAppend || _hasIndexed) && _hasScalar && _declaredCount != _entries)
                throw Invalid("SII-Array stimmt nicht mit seiner deklarierten Anzahl überein.");
        }
    }
}
