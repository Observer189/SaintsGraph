using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Minimal ordered JSON DOM — enough for graph sidecars and EditorJsonUtility
    /// round-trips, with no external dependencies. Numbers keep their raw text so
    /// values round-trip losslessly.
    /// </summary>
    internal abstract class JsonValue
    {
        public static JsonValue Parse(string text)
        {
            Parser parser = new Parser(text);
            JsonValue value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                throw new FormatException("Trailing content in JSON at position " + parser.Position);
            }

            return value;
        }

        public string Write(bool pretty = true)
        {
            StringBuilder builder = new StringBuilder();
            WriteTo(builder, pretty, 0);
            return builder.ToString();
        }

        public abstract void WriteTo(StringBuilder builder, bool pretty, int indent);

        protected static void AppendIndent(StringBuilder builder, int indent)
        {
            builder.Append('\n');
            builder.Append(' ', indent * 2);
        }

        internal static void WriteEscaped(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;

            public Parser(string text)
            {
                _text = text ?? throw new ArgumentNullException(nameof(text));
            }

            public int Position => _index;
            public bool AtEnd => _index >= _text.Length;

            public void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                {
                    _index++;
                }
            }

            public JsonValue ParseValue()
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    throw new FormatException("Unexpected end of JSON");
                }

                char c = _text[_index];
                switch (c)
                {
                    case '{':
                        return ParseObject();
                    case '[':
                        return ParseArray();
                    case '"':
                        return new JsonString(ParseStringToken());
                    case 't':
                        Expect("true");
                        return new JsonBool(true);
                    case 'f':
                        Expect("false");
                        return new JsonBool(false);
                    case 'n':
                        Expect("null");
                        return JsonNull.Instance;
                    default:
                        return ParseNumber();
                }
            }

            private void Expect(string literal)
            {
                if (_index + literal.Length > _text.Length
                    || string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                {
                    throw new FormatException("Invalid JSON literal at position " + _index);
                }

                _index += literal.Length;
            }

            private JsonValue ParseObject()
            {
                _index++; // '{'
                JsonObject result = new JsonObject();
                SkipWhitespace();
                if (!AtEnd && _text[_index] == '}')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseStringToken();
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != ':')
                    {
                        throw new FormatException("Expected ':' at position " + _index);
                    }

                    _index++;
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw new FormatException("Unterminated JSON object");
                    }

                    char c = _text[_index++];
                    if (c == '}')
                    {
                        return result;
                    }

                    if (c != ',')
                    {
                        throw new FormatException("Expected ',' or '}' at position " + (_index - 1));
                    }
                }
            }

            private JsonValue ParseArray()
            {
                _index++; // '['
                JsonArray result = new JsonArray();
                SkipWhitespace();
                if (!AtEnd && _text[_index] == ']')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    result.Items.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw new FormatException("Unterminated JSON array");
                    }

                    char c = _text[_index++];
                    if (c == ']')
                    {
                        return result;
                    }

                    if (c != ',')
                    {
                        throw new FormatException("Expected ',' or ']' at position " + (_index - 1));
                    }
                }
            }

            private string ParseStringToken()
            {
                if (AtEnd || _text[_index] != '"')
                {
                    throw new FormatException("Expected '\"' at position " + _index);
                }

                _index++;
                StringBuilder builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw new FormatException("Unterminated JSON string");
                    }

                    char c = _text[_index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (AtEnd)
                    {
                        throw new FormatException("Unterminated escape sequence");
                    }

                    char escape = _text[_index++];
                    switch (escape)
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case '/':
                            builder.Append('/');
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            if (_index + 4 > _text.Length)
                            {
                                throw new FormatException("Invalid \\u escape");
                            }

                            builder.Append((char)ushort.Parse(_text.Substring(_index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture));
                            _index += 4;
                            break;
                        default:
                            throw new FormatException("Invalid escape '\\" + escape + "'");
                    }
                }
            }

            private JsonValue ParseNumber()
            {
                int start = _index;
                if (!AtEnd && (_text[_index] == '-' || _text[_index] == '+'))
                {
                    _index++;
                }

                while (!AtEnd && (char.IsDigit(_text[_index]) || _text[_index] == '.'
                                  || _text[_index] == 'e' || _text[_index] == 'E'
                                  || _text[_index] == '-' || _text[_index] == '+'))
                {
                    _index++;
                }

                string raw = _text.Substring(start, _index - start);
                if (raw.Length == 0
                    || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    throw new FormatException("Invalid JSON number at position " + start);
                }

                return new JsonNumber(raw);
            }
        }
    }

    internal sealed class JsonObject : JsonValue
    {
        public readonly List<KeyValuePair<string, JsonValue>> Entries = new List<KeyValuePair<string, JsonValue>>();

        public JsonValue this[string key]
        {
            get
            {
                foreach (KeyValuePair<string, JsonValue> entry in Entries)
                {
                    if (entry.Key == key)
                    {
                        return entry.Value;
                    }
                }

                return null;
            }
            set
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (Entries[i].Key == key)
                    {
                        Entries[i] = new KeyValuePair<string, JsonValue>(key, value);
                        return;
                    }
                }

                Entries.Add(new KeyValuePair<string, JsonValue>(key, value));
            }
        }

        public bool Remove(string key)
        {
            return Entries.RemoveAll(entry => entry.Key == key) > 0;
        }

        public string GetString(string key)
        {
            return (this[key] as JsonString)?.Value;
        }

        public override void WriteTo(StringBuilder builder, bool pretty, int indent)
        {
            if (Entries.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append('{');
            for (int i = 0; i < Entries.Count; i++)
            {
                if (pretty)
                {
                    AppendIndent(builder, indent + 1);
                }

                WriteEscaped(builder, Entries[i].Key);
                builder.Append(pretty ? ": " : ":");
                Entries[i].Value.WriteTo(builder, pretty, indent + 1);
                if (i < Entries.Count - 1)
                {
                    builder.Append(',');
                }
            }

            if (pretty)
            {
                AppendIndent(builder, indent);
            }

            builder.Append('}');
        }
    }

    internal sealed class JsonArray : JsonValue
    {
        public readonly List<JsonValue> Items = new List<JsonValue>();

        private bool IsInlineable()
        {
            if (Items.Count > 6)
            {
                return false;
            }

            int total = 0;
            foreach (JsonValue item in Items)
            {
                switch (item)
                {
                    case JsonNumber number:
                        total += number.Raw.Length;
                        break;
                    case JsonBool _:
                    case JsonNull _:
                        total += 5;
                        break;
                    case JsonString text when text.Value.Length <= 24:
                        total += text.Value.Length + 2;
                        break;
                    default:
                        return false;
                }
            }

            return total <= 80;
        }

        public override void WriteTo(StringBuilder builder, bool pretty, int indent)
        {
            if (Items.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            bool inline = !pretty || IsInlineable();
            builder.Append('[');
            for (int i = 0; i < Items.Count; i++)
            {
                if (pretty && !inline)
                {
                    AppendIndent(builder, indent + 1);
                }

                Items[i].WriteTo(builder, pretty && !inline, indent + 1);
                if (i < Items.Count - 1)
                {
                    builder.Append(inline && pretty ? ", " : ",");
                }
            }

            if (pretty && !inline)
            {
                AppendIndent(builder, indent);
            }

            builder.Append(']');
        }
    }

    internal sealed class JsonString : JsonValue
    {
        public readonly string Value;

        public JsonString(string value)
        {
            Value = value ?? "";
        }

        public override void WriteTo(StringBuilder builder, bool pretty, int indent)
        {
            WriteEscaped(builder, Value);
        }
    }

    internal sealed class JsonNumber : JsonValue
    {
        public readonly string Raw;

        public JsonNumber(string raw)
        {
            Raw = raw;
        }

        public static JsonNumber From(double value)
        {
            return new JsonNumber(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public static JsonNumber From(int value)
        {
            return new JsonNumber(value.ToString(CultureInfo.InvariantCulture));
        }

        public double AsDouble => double.Parse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        public float AsFloat => (float)AsDouble;

        public override void WriteTo(StringBuilder builder, bool pretty, int indent)
        {
            builder.Append(Raw);
        }
    }

    internal sealed class JsonBool : JsonValue
    {
        public readonly bool Value;

        public JsonBool(bool value)
        {
            Value = value;
        }

        public override void WriteTo(StringBuilder builder, bool pretty, int indent)
        {
            builder.Append(Value ? "true" : "false");
        }
    }

    internal sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();

        private JsonNull()
        {
        }

        public override void WriteTo(StringBuilder builder, bool pretty, int indent)
        {
            builder.Append("null");
        }
    }
}
