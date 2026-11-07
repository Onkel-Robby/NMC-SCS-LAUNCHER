using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace NmcScsLauncher.Infrastructure;

public enum ScsDefinitionValueKind { Token, String, Tuple, Placement }

/// <summary>A typed, immutable SII value. String Text is decoded; token Text is retained verbatim.</summary>
public sealed class ScsDefinitionValue
{
    internal ScsDefinitionValue(ScsDefinitionValueKind kind, string text,
        IReadOnlyList<ScsDefinitionValue>? elements = null, IReadOnlyList<char>? separators = null)
    {
        Kind = kind;
        Text = text;
        Elements = elements ?? Array.Empty<ScsDefinitionValue>();
        Separators = separators ?? Array.Empty<char>();
    }

    public ScsDefinitionValueKind Kind { get; }
    public string Text { get; }
    public IReadOnlyList<ScsDefinitionValue> Elements { get; }
    public IReadOnlyList<char> Separators { get; }
}

public sealed class ScsDefinitionUnit
{
    private readonly IReadOnlyDictionary<string, Attribute> _attributes;

    internal ScsDefinitionUnit(string type, string id, Dictionary<string, Attribute> attributes)
    {
        Type = type;
        Id = id;
        _attributes = new ReadOnlyDictionary<string, Attribute>(attributes);
        AttributeNames = Array.AsReadOnly(attributes.Keys.ToArray());
    }

    public string Type { get; }
    public string Id { get; }
    public IReadOnlyCollection<string> AttributeNames { get; }
    public bool HasAttribute(string key) => _attributes.ContainsKey(key);

    public ScsDefinitionValue? GetValue(string key)
    {
        if (!_attributes.TryGetValue(key, out var attribute)) return null;
        if (attribute.Array is not null) throw WrongType(key, "Skalar");
        return attribute.Scalar;
    }

    public string? GetString(string key) => GetScalarText(key, ScsDefinitionValueKind.String, "String");
    public string? GetToken(string key) => GetScalarText(key, ScsDefinitionValueKind.Token, "unquotierter Skalar");

    public int? GetDeclaredArrayCount(string key)
    {
        if (!_attributes.TryGetValue(key, out var attribute)) return null;
        if (attribute.Array is not null) return attribute.DeclaredCount;
        if (attribute.Scalar is not { Kind: ScsDefinitionValueKind.Token } scalar ||
            !int.TryParse(scalar.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0)
            throw WrongType(key, "Array-Anzahl");
        return count;
    }

    /// <summary>
    /// Returns append or indexed array values. A bare zero is accepted as an empty array;
    /// a nonzero declaration without entries is rejected when read as an array.
    /// </summary>
    public IReadOnlyList<ScsDefinitionValue> GetValues(string key)
    {
        if (!_attributes.TryGetValue(key, out var attribute)) return Array.Empty<ScsDefinitionValue>();
        if (attribute.Array is not null) return attribute.Array;
        if (attribute.Scalar is { Kind: ScsDefinitionValueKind.Token, Text: "0" }) return Array.Empty<ScsDefinitionValue>();
        throw WrongType(key, "vollständiges Array");
    }

    public IReadOnlyList<string> GetStrings(string key) => GetArrayText(key, ScsDefinitionValueKind.String, "String-Array");
    public IReadOnlyList<string> GetTokens(string key) => GetArrayText(key, ScsDefinitionValueKind.Token, "Token-Array");

    private string? GetScalarText(string key, ScsDefinitionValueKind kind, string description)
    {
        var value = GetValue(key);
        if (value is null) return null;
        if (value.Kind != kind) throw WrongType(key, description);
        return value.Text;
    }

    private IReadOnlyList<string> GetArrayText(string key, ScsDefinitionValueKind kind, string description)
    {
        var values = GetValues(key);
        if (values.Any(value => value.Kind != kind)) throw WrongType(key, description);
        return Array.AsReadOnly(values.Select(value => value.Text).ToArray());
    }

    private InvalidDataException WrongType(string key, string expected) =>
        new($"SII-Attribut '{key}' in Unit '{Id}' ist kein {expected}.");

    internal sealed record Attribute(ScsDefinitionValue? Scalar, IReadOnlyList<ScsDefinitionValue>? Array, int? DeclaredCount = null);
}

/// <summary>
/// Bounded SII metadata parser. Attribute names do not need a schema; callers use typed
/// accessors for fields they understand. Includes must be resolved by the effective VFS first.
/// </summary>
public sealed class ScsDefinitionDocument
{
    private const int MaximumCharacters = 8 * 1024 * 1024;
    private const int MaximumTokens = 750_000;
    private const int MaximumStringBytes = 1024 * 1024;
    private const int MaximumUnits = 25_000;
    private const int MaximumArrayEntries = 65_536;
    private const int MaximumTupleDepth = 8;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private ScsDefinitionDocument(IReadOnlyList<ScsDefinitionUnit> units) => Units = units;
    public IReadOnlyList<ScsDefinitionUnit> Units { get; }

    public static ScsDefinitionDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaximumCharacters) throw new InvalidDataException("SII-Metadaten überschreiten das Textlimit.");
        return new Parser(Lex(content)).ReadDocument();
    }

    private enum TokenKind { Word, String, Punctuation, End }
    private sealed record Lexeme(TokenKind Kind, string Text, int Offset);

    private static IReadOnlyList<Lexeme> Lex(string content)
    {
        var result = new List<Lexeme>();
        var position = content.Length > 0 && content[0] == '\uFEFF' ? 1 : 0;
        while (position < content.Length)
        {
            var character = content[position];
            if (char.IsWhiteSpace(character)) { position++; continue; }
            if (char.IsControl(character) || character == '\uFEFF') throw Error(position, "Nicht unterstütztes Steuerzeichen.");
            if (character == '#' || character == '/' && position + 1 < content.Length && content[position + 1] == '/')
            {
                while (position < content.Length && content[position] is not ('\n' or '\r')) position++;
                continue;
            }
            if (character == '/' && position + 1 < content.Length && content[position + 1] == '*')
            {
                var commentStart = position;
                position += 2;
                while (position + 1 < content.Length && !(content[position] == '*' && content[position + 1] == '/')) position++;
                if (position + 1 >= content.Length) throw Error(commentStart, "Unvollständiger Blockkommentar.");
                position += 2;
                continue;
            }
            if (character == '@') throw Error(position, "Nicht aufgelöste oder nicht unterstützte SII-Direktive.");
            var start = position;
            if (character == '"') result.Add(new(TokenKind.String, ReadQuoted(content, ref position), start));
            else if ("{}:[](),;".Contains(character)) result.Add(new(TokenKind.Punctuation, content[position++].ToString(), start));
            else
            {
                while (position < content.Length && !char.IsWhiteSpace(content[position]) &&
                       !"{}:[](),;\"#@".Contains(content[position]) &&
                       !(content[position] == '/' && position + 1 < content.Length && content[position + 1] is '/' or '*'))
                {
                    if (char.IsControl(content[position]) || content[position] == '\uFEFF') throw Error(position, "Nicht unterstütztes Steuerzeichen.");
                    position++;
                }
                if (position == start) throw Error(position, "Nicht unterstütztes SII-Zeichen.");
                result.Add(new(TokenKind.Word, content[start..position], start));
            }
            if (result.Count > MaximumTokens) throw Error(position, "SII-Metadaten überschreiten das Tokenlimit.");
        }
        result.Add(new(TokenKind.End, string.Empty, content.Length));
        return result.AsReadOnly();
    }

    private static string ReadQuoted(string content, ref int position)
    {
        var start = position++;
        using var bytes = new MemoryStream();
        Span<byte> encoded = stackalloc byte[4];
        try
        {
            while (position < content.Length)
            {
                var character = content[position++];
                if (character == '"') return StrictUtf8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
                if (char.IsControl(character)) throw Error(position - 1, "Steuerzeichen müssen in SII-Strings escaped sein.");
                if (character == '\\')
                {
                    if (position >= content.Length) throw Error(start, "Unvollständige Escape-Sequenz.");
                    var escape = content[position++];
                    switch (escape)
                    {
                        case '\\': bytes.WriteByte((byte)'\\'); break;
                        case '"': bytes.WriteByte((byte)'"'); break;
                        case 'n': bytes.WriteByte((byte)'\n'); break;
                        case 'r': bytes.WriteByte((byte)'\r'); break;
                        case 't': bytes.WriteByte((byte)'\t'); break;
                        case 'x':
                            if (position + 1 >= content.Length || !byte.TryParse(content.AsSpan(position, 2), NumberStyles.AllowHexSpecifier,
                                    CultureInfo.InvariantCulture, out var value))
                                throw Error(position, "Ungültige SII-Byte-Escape-Sequenz.");
                            bytes.WriteByte(value);
                            position += 2;
                            break;
                        default: throw Error(position - 1, "Nicht unterstützte SII-Escape-Sequenz.");
                    }
                }
                else
                {
                    var length = char.IsHighSurrogate(character) && position < content.Length && char.IsLowSurrogate(content[position]) ? 2 : 1;
                    var literalStart = position - 1;
                    if (length == 2) position++;
                    var count = StrictUtf8.GetBytes(content.AsSpan(literalStart, length), encoded);
                    bytes.Write(encoded[..count]);
                }
                if (bytes.Length > MaximumStringBytes) throw Error(start, "SII-String überschreitet das Größenlimit.");
            }
        }
        catch (Exception exception) when (exception is EncoderFallbackException or DecoderFallbackException)
        {
            throw new InvalidDataException($"Ungültiges UTF-8 im SII-String an Position {start}.", exception);
        }
        throw Error(start, "Unvollständiger SII-String.");
    }

    private sealed class Parser(IReadOnlyList<Lexeme> tokens)
    {
        private int _position;
        private Lexeme Current => tokens[_position];
        private bool Is(string punctuation) => Current.Kind == TokenKind.Punctuation && Current.Text == punctuation;

        internal ScsDefinitionDocument ReadDocument()
        {
            if (Current.Kind != TokenKind.Word || Current.Text != "SiiNunit") throw Error(Current.Offset, "SiiNunit-Kopf fehlt.");
            _position++;
            Consume("{");
            var units = new List<ScsDefinitionUnit>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            while (!Is("}"))
            {
                var type = ReadIdentifier("Unit-Typ");
                Consume(":");
                var id = Current;
                if (id.Kind != TokenKind.Word || id.Text.Length is 0 or > 512 ||
                    id.Text.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.')) ||
                    (id.Text.StartsWith('.') ? id.Text[1..] : id.Text).Split('.').Any(part => part.Length == 0))
                    throw Error(id.Offset, "Ungültige Unit-ID.");
                _position++;
                if (!ids.Add(id.Text)) throw Error(id.Offset, $"Unit-ID '{id.Text}' ist mehrfach vorhanden.");
                Consume("{");
                var attributes = new Dictionary<string, AttributeBuilder>(StringComparer.Ordinal);
                while (!Is("}")) ReadAttribute(attributes);
                Consume("}");
                units.Add(new(type, id.Text, attributes.ToDictionary(pair => pair.Key, pair => pair.Value.Finish(pair.Key), StringComparer.Ordinal)));
                if (units.Count > MaximumUnits) throw Error(Current.Offset, "SII-Dokument überschreitet das Unit-Limit.");
            }
            Consume("}");
            if (Current.Kind != TokenKind.End) throw Error(Current.Offset, "Unerwartete Daten nach dem SiiNunit-Ende.");
            return new(units.AsReadOnly());
        }

        private void ReadAttribute(Dictionary<string, AttributeBuilder> attributes)
        {
            var offset = Current.Offset;
            var key = ReadIdentifier("Attributname");
            var append = false;
            int? index = null;
            if (Is("["))
            {
                _position++;
                if (Is("]")) append = true;
                else
                {
                    if (Current.Kind != TokenKind.Word || !int.TryParse(Current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                        parsed < 0 || parsed >= MaximumArrayEntries) throw Error(Current.Offset, "Ungültiger oder zu großer Array-Index.");
                    index = parsed;
                    _position++;
                }
                Consume("]");
            }
            Consume(":");
            var value = ReadValue(0);
            if (value.Kind == ScsDefinitionValueKind.Tuple && Is("("))
            {
                var second = ReadValue(0);
                value = new(ScsDefinitionValueKind.Placement, value.Text + " " + second.Text, Array.AsReadOnly(new[] { value, second }));
            }
            if (!attributes.TryGetValue(key, out var builder)) attributes.Add(key, builder = new(offset));
            builder.Add(value, append, index);
        }

        private ScsDefinitionValue ReadValue(int depth)
        {
            var token = Current;
            if (token.Kind is TokenKind.Word or TokenKind.String)
            {
                _position++;
                return new(token.Kind == TokenKind.String ? ScsDefinitionValueKind.String : ScsDefinitionValueKind.Token, token.Text);
            }
            if (!Is("(")) throw Error(token.Offset, "SII-Wert fehlt oder enthält eine nicht unterstützte Struktur.");
            if (depth >= MaximumTupleDepth) throw Error(token.Offset, "SII-Tupel überschreitet das Verschachtelungslimit.");
            _position++;
            var elements = new List<ScsDefinitionValue> { ReadValue(depth + 1) };
            var separators = new List<char>();
            while (!Is(")"))
            {
                if (!Is(",") && !Is(";")) throw Error(Current.Offset, "Ungültiges SII-Tupeltrennzeichen.");
                separators.Add(Current.Text[0]);
                _position++;
                elements.Add(ReadValue(depth + 1));
                if (elements.Count > 64) throw Error(Current.Offset, "SII-Tupel überschreitet das Elementlimit.");
            }
            Consume(")");
            var text = new StringBuilder("(").Append(elements[0].Text);
            for (var index = 1; index < elements.Count; index++) text.Append(separators[index - 1]).Append(' ').Append(elements[index].Text);
            text.Append(')');
            return new(ScsDefinitionValueKind.Tuple, text.ToString(), elements.AsReadOnly(), separators.AsReadOnly());
        }

        private string ReadIdentifier(string description)
        {
            var token = Current;
            if (token.Kind != TokenKind.Word || token.Text.Length is 0 or > 128 ||
                !(char.IsAsciiLetter(token.Text[0]) || token.Text[0] == '_') ||
                token.Text.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                throw Error(token.Offset, $"Ungültiger {description}.");
            _position++;
            return token.Text;
        }

        private void Consume(string punctuation)
        {
            if (!Is(punctuation)) throw Error(Current.Offset, $"SII-Zeichen '{punctuation}' erwartet.");
            _position++;
        }
    }

    private sealed class AttributeBuilder(int offset)
    {
        private ScsDefinitionValue? _scalar;
        private readonly List<ScsDefinitionValue> _append = [];
        private readonly SortedDictionary<int, ScsDefinitionValue> _indexed = [];

        internal void Add(ScsDefinitionValue value, bool append, int? index)
        {
            if (append)
            {
                if (_indexed.Count > 0) throw Error(offset, "Gemischte SII-Arraydarstellung.");
                if (_append.Count >= MaximumArrayEntries) throw Error(offset, "SII-Array überschreitet das Elementlimit.");
                _append.Add(value);
            }
            else if (index is not null)
            {
                if (_append.Count > 0) throw Error(offset, "Gemischte SII-Arraydarstellung.");
                if (!_indexed.TryAdd(index.Value, value)) throw Error(offset, "SII-Arrayindex ist mehrfach vorhanden.");
            }
            else
            {
                if (_scalar is not null) throw Error(offset, "SII-Skalar ist mehrfach vorhanden.");
                _scalar = value;
            }
        }

        internal ScsDefinitionUnit.Attribute Finish(string key)
        {
            if (_append.Count == 0 && _indexed.Count == 0) return new(_scalar, null);
            if (_indexed.Keys.Where((index, position) => index != position).Any())
                throw Error(offset, $"SII-Array '{key}' enthält Indexlücken.");
            var entryCount = _append.Count > 0 ? _append.Count : _indexed.Count;
            if (_scalar is not null && (_scalar.Kind != ScsDefinitionValueKind.Token ||
                !int.TryParse(_scalar.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count != entryCount))
                throw Error(offset, $"SII-Array '{key}' stimmt nicht mit seiner deklarierten Anzahl überein.");
            var declaredCount = _scalar is null ? (int?)null : entryCount;
            // Original SCS sound includes combine an explicit count with append entries.
            // The count still has to match exactly; indexed/append mixing remains invalid.
            if (_append.Count > 0) return new(null, _append.AsReadOnly(), declaredCount);
            return new(null, Array.AsReadOnly(_indexed.Values.ToArray()), declaredCount);
        }
    }

    private static InvalidDataException Error(int offset, string message) => new($"{message} (Position {offset})");
}
