using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using FES.Core;

namespace FES.Tests
{
    /// <summary>
    /// Minimal reflection JSON writer for the parity fixtures the Rust port reads.
    /// Objects: public instance fields, then public readable non-indexer properties, each in declaration
    /// (metadata) order. Enums as names; <see cref="Fx"/> as its raw value; id structs (a value type in
    /// FES.* whose name ends in "Id" and that has a public int field "Value") as that value; long and ulong
    /// (and Fx raw) as decimal strings, so no 64-bit value passes through a JSON double; lists, arrays as
    /// arrays; dictionaries as objects with string keys.
    /// </summary>
    public static class ParityJson
    {
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        /// <summary>A raw JSON fragment written as is.</summary>
        public sealed class Raw
        {
            public readonly string Json;
            public Raw(string json) => Json = json;
        }

        static void Write(StringBuilder sb, object value, int depth)
        {
            if (depth > 32) throw new InvalidOperationException("ParityJson: nesting too deep (cycle?).");
            switch (value)
            {
                case null: sb.Append("null"); return;
                case Raw raw: sb.Append(raw.Json); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case long l: WriteString(sb, l.ToString(CultureInfo.InvariantCulture)); return;
                case ulong ul: WriteString(sb, ul.ToString(CultureInfo.InvariantCulture)); return;
                case Fx fx: WriteString(sb, fx.Raw.ToString(CultureInfo.InvariantCulture)); return;
                case Enum e: WriteString(sb, e.ToString()); return;
                case int _: case uint _: case short _: case ushort _: case byte _: case sbyte _:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return;
                case float _: case double _: case decimal _:
                    throw new InvalidOperationException($"ParityJson: floating value of type {value.GetType()}.");
                case IDictionary dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                        sb.Append(':');
                        Write(sb, entry.Value, depth + 1);
                    }
                    sb.Append('}');
                    return;
                }
            }

            var type = value.GetType();
            if (IsIdStruct(type, out var valueField))
            {
                Write(sb, valueField.GetValue(value), depth + 1);
                return;
            }

            // IReadOnlyDictionary<string, string> (and similar) that is not IDictionary.
            var roDict = type.GetInterfaces().FirstOrDefault(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IReadOnlyDictionary<,>));
            if (roDict != null && value is IEnumerable pairs)
            {
                sb.Append('{');
                bool first = true;
                foreach (var pair in pairs)
                {
                    var pt = pair.GetType();
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(pt.GetProperty("Key").GetValue(pair), CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Write(sb, pt.GetProperty("Value").GetValue(pair), depth + 1);
                }
                sb.Append('}');
                return;
            }

            if (value is IEnumerable items)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in items)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }

            WriteObject(sb, value, type, depth);
        }

        static void WriteObject(StringBuilder sb, object value, Type type, int depth)
        {
            sb.Append('{');
            bool first = true;
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.MetadataToken))
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, f.Name);
                sb.Append(':');
                Write(sb, f.GetValue(value), depth + 1);
            }
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.MetadataToken))
            {
                if (p.GetIndexParameters().Length > 0 || p.GetGetMethod() == null) continue;
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, p.Name);
                sb.Append(':');
                Write(sb, p.GetValue(value), depth + 1);
            }
            sb.Append('}');
        }

        static bool IsIdStruct(Type type, out FieldInfo valueField)
        {
            valueField = null;
            if (!type.IsValueType || type.IsPrimitive || type.Namespace == null || !type.Namespace.StartsWith("FES")) return false;
            if (!type.Name.EndsWith("Id", StringComparison.Ordinal)) return false;
            valueField = type.GetField("Value", BindingFlags.Public | BindingFlags.Instance);
            return valueField != null && valueField.FieldType == typeof(int);
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
