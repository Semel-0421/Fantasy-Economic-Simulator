using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FES.Data
{
    /// <summary>
    /// Minimal RFC-4180-style CSV reader: header row, quoted fields (a quote only opens a field when it is
    /// the field's first character), lines starting with '#' are comments, blank lines are skipped.
    /// Malformed input fails loudly instead of silently merging rows.
    /// </summary>
    public sealed class CsvTable
    {
        readonly Dictionary<string, int> _columns = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<string[]> _rows = new List<string[]>();

        public string Name { get; }
        public int RowCount => _rows.Count;

        CsvTable(string name) => Name = name;

        public static CsvTable Parse(string name, string text)
        {
            var table = new CsvTable(name);
            bool headerRead = false;
            foreach (var fields in ParseRows(name, text))
            {
                if (!headerRead)
                {
                    for (int i = 0; i < fields.Length; i++) table._columns[fields[i].Trim()] = i;
                    headerRead = true;
                }
                else
                {
                    table._rows.Add(fields);
                }
            }
            return table;
        }

        public bool HasColumn(string column) => _columns.ContainsKey(column);

        public string GetString(int row, string column)
        {
            if (!_columns.TryGetValue(column, out int index))
                throw new FormatException($"{Name}: missing column '{column}'.");
            var fields = _rows[row];
            return index < fields.Length ? fields[index].Trim() : string.Empty;
        }

        public string GetStringOr(int row, string column, string fallback) =>
            HasColumn(column) && GetString(row, column).Length > 0 ? GetString(row, column) : fallback;

        public int GetInt(int row, string column)
        {
            long value = GetLong(row, column);
            if (value < int.MinValue || value > int.MaxValue)
                throw new FormatException($"{Name} row {row + 1}: '{column}' = {value} is out of int range.");
            return (int)value;
        }

        public int GetIntOr(int row, string column, int fallback) =>
            HasColumn(column) && GetString(row, column).Length > 0 ? GetInt(row, column) : fallback;

        public long GetLong(int row, string column)
        {
            var s = GetString(row, column);
            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                throw new FormatException($"{Name} row {row + 1}: '{column}' = '{s}' is not an integer.");
            return value;
        }

        public T GetEnum<T>(int row, string column) where T : struct, Enum
        {
            var s = GetString(row, column);
            if (!TryParseEnumName(s, out T value))
                throw new FormatException($"{Name} row {row + 1}: '{column}' = '{s}' is not a valid {typeof(T).Name}.");
            return value;
        }

        /// <summary>Parses an enum by name only (numeric strings such as "9" are rejected).</summary>
        public static bool TryParseEnumName<T>(string s, out T value) where T : struct, Enum
        {
            value = default;
            if (string.IsNullOrEmpty(s) || char.IsDigit(s[0]) || s[0] == '-' || s[0] == '+') return false;
            return Enum.TryParse(s, true, out value) && Enum.IsDefined(typeof(T), value);
        }

        static IEnumerable<string[]> ParseRows(string name, string text)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            bool fieldStarted = false; // a non-whitespace character or quote has been seen in this field
            bool rowStarted = false;   // any field content has been seen in this row
            int line = 1;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"') inQuotes = false;
                    else
                    {
                        if (c == '\n') line++;
                        field.Append(c);
                    }
                    continue;
                }

                if (c == '#' && !rowStarted && fields.Count == 0 && !fieldStarted)
                {
                    while (i < text.Length && text[i] != '\n') i++; // comment line
                    line++;
                    field.Clear();
                    continue;
                }
                if (c == '"' && !fieldStarted)
                {
                    inQuotes = true;
                    fieldStarted = rowStarted = true;
                    field.Clear();
                    continue;
                }
                if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    rowStarted = true;
                    continue;
                }
                if (c == '\r' || c == '\n')
                {
                    if (c == '\n') line++;
                    if (rowStarted || fieldStarted)
                    {
                        fields.Add(field.ToString());
                        yield return fields.ToArray();
                    }
                    fields.Clear();
                    field.Clear();
                    fieldStarted = rowStarted = false;
                    continue;
                }
                if (!char.IsWhiteSpace(c)) fieldStarted = rowStarted = true;
                field.Append(c);
            }

            if (inQuotes) throw new FormatException($"{name} line {line}: unterminated quoted field.");
            if (rowStarted || fieldStarted)
            {
                fields.Add(field.ToString());
                yield return fields.ToArray();
            }
        }
    }
}
