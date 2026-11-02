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
            var reader = new RowReader(name, text);
            string[] row;
            while ((row = reader.Next()) != null) yield return row;
        }

        /// <summary>Reads the text one row at a time.</summary>
        sealed class RowReader
        {
            readonly string _name, _text;
            readonly List<string> _fields = new List<string>();
            readonly StringBuilder _field = new StringBuilder();
            bool _inQuotes;
            bool _fieldStarted; // a non-whitespace character or quote has been seen in this field
            bool _rowStarted;   // any field content has been seen in this row
            int _i, _line = 1;

            public RowReader(string name, string text)
            {
                _name = name;
                _text = text;
            }

            /// <summary>The next row, or null at the end of the text.</summary>
            public string[] Next()
            {
                string[] row = null;
                for (; row == null && _i < _text.Length; _i++)
                {
                    char c = _text[_i];
                    if (_inQuotes) ReadQuoted(c);
                    else if (c == '#' && !_rowStarted && _fields.Count == 0 && !_fieldStarted) SkipComment();
                    else if (c == '"' && !_fieldStarted) OpenQuote();
                    else if (c == ',') EndField();
                    else if (c == '\r' || c == '\n') row = EndLine(c);
                    else ReadPlain(c);
                }
                if (row != null) return row;
                if (_inQuotes) throw new FormatException($"{_name} line {_line}: unterminated quoted field.");
                return TakeRow();
            }

            void ReadQuoted(char c)
            {
                if (c == '"' && _i + 1 < _text.Length && _text[_i + 1] == '"')
                {
                    _field.Append('"');
                    _i++;
                }
                else if (c == '"') _inQuotes = false;
                else
                {
                    if (c == '\n') _line++;
                    _field.Append(c);
                }
            }

            void SkipComment()
            {
                while (_i < _text.Length && _text[_i] != '\n') _i++;
                _line++;
                _field.Clear();
            }

            void OpenQuote()
            {
                _inQuotes = true;
                _fieldStarted = _rowStarted = true;
                _field.Clear();
            }

            void EndField()
            {
                _fields.Add(_field.ToString());
                _field.Clear();
                _fieldStarted = false;
                _rowStarted = true;
            }

            string[] EndLine(char c)
            {
                if (c == '\n') _line++;
                return TakeRow();
            }

            void ReadPlain(char c)
            {
                if (!char.IsWhiteSpace(c)) _fieldStarted = _rowStarted = true;
                _field.Append(c);
            }

            /// <summary>Ends the row and returns it, or null when it held nothing.</summary>
            string[] TakeRow()
            {
                string[] row = null;
                if (_rowStarted || _fieldStarted)
                {
                    _fields.Add(_field.ToString());
                    row = _fields.ToArray();
                }
                _fields.Clear();
                _field.Clear();
                _fieldStarted = _rowStarted = false;
                return row;
            }
        }
    }
}
