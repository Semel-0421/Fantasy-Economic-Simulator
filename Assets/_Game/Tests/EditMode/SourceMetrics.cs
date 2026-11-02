using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FES.Tests
{
    /// <summary>Size and complexity of one method, as Sonar's and Microsoft's analyzers define them.</summary>
    public readonly struct MethodMetrics
    {
        public readonly string Name;
        public readonly int Line, Parameters, Lines, Cognitive, Cyclomatic, Nesting;

        public MethodMetrics(string name, int line, int parameters, int lines, int cognitive, int cyclomatic, int nesting)
        {
            Name = name;
            Line = line;
            Parameters = parameters;
            Lines = lines;
            Cognitive = cognitive;
            Cyclomatic = cyclomatic;
            Nesting = nesting;
        }
    }

    /// <summary>
    /// Measures C# source without a compiler, because Unity projects cannot run the Roslyn analyzers from a test.
    /// Cognitive complexity follows sonar-dotnet's CSharpCognitiveComplexityMetric, except that it does not add the +1
    /// for direct recursion; nesting follows S134, lines S138, parameters S107, cyclomatic complexity CA1502.
    /// </summary>
    public static class SourceMetrics
    {
        static readonly Regex TokenPattern = new Regex(
            @"[A-Za-z_][A-Za-z0-9_]*|\d[\w.]*|&&|\|\||\?\?=?|\?\.|\?\[|=>|<=|>=|==|!=|[?:;,(){}\[\]<>=.]",
            RegexOptions.Compiled);

        internal readonly struct Token
        {
            public readonly string Text;
            public readonly int Line;

            public Token(string text, int line)
            {
                Text = text;
                Line = line;
            }
        }

        /// <summary>The code with comments, string and char literals and preprocessor lines blanked; line numbers hold.</summary>
        public static string StripCommentsAndStrings(string text)
        {
            var result = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (IsAt(text, i, "//") || AtPreprocessorLine(text, i)) i = SkipToLineEnd(text, i);
                else if (IsAt(text, i, "/*")) i = CopyNewlines(text, i, BlockCommentEnd(text, i), result);
                else if (text[i] == '"' || AtStringPrefix(text, i)) i = CopyNewlines(text, i, StringEnd(text, i), result);
                else if (text[i] == '\'') i = CharEnd(text, i);
                else result.Append(text[i++]);
            }
            return result.ToString();
        }

        /// <summary>Measures every method, constructor, operator and accessor in stripped code.</summary>
        public static List<MethodMetrics> Measure(string strippedCode)
        {
            var walker = new MemberWalker(Tokens(strippedCode));
            walker.Members(walker.Count);
            return walker.Found;
        }

        /// <summary>Each line holding code, with spaces evened out and lone braces dropped, and its number.</summary>
        public static List<(int line, string text)> CodeLines(string strippedCode)
        {
            var lines = new List<(int, string)>();
            string[] all = strippedCode.Split('\n');
            for (int i = 0; i < all.Length; i++)
            {
                string text = Regex.Replace(all[i], @"\s+", " ").Trim();
                if (text.Length > 0 && text != "{" && text != "}" && text != "};") lines.Add((i + 1, text));
            }
            return lines;
        }

        /// <summary>The names of methods declared with neither an access word nor override or an interface's name before them.</summary>
        public static IEnumerable<string> PrivateMethodNames(string strippedCode)
        {
            foreach (Match match in PrivateMethod.Matches(strippedCode))
                yield return match.Groups[1].Value;
        }

        static readonly Regex PrivateMethod = new Regex(
            @"^\s*(?:private\s+)?(?:static\s+|unsafe\s+|readonly\s+)*(?!return\b|new\b|throw\b|else\b|await\b|public\b|internal\b|protected\b|override\b|virtual\b|abstract\b|var\b|const\b|using\b|if\b|for\b|foreach\b|while\b|switch\b|lock\b)[A-Za-z_][\w<>,\[\]\.\? ]*?\s+([A-Za-z_]\w*)(?:<[^>()]*>)?\([^;{}=]*\)\s*(?:where\s[^{;]*)?(?:\{|=>)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        internal static List<Token> Tokens(string strippedCode)
        {
            var tokens = new List<Token>();
            int line = 1, position = 0;
            foreach (Match match in TokenPattern.Matches(strippedCode))
            {
                for (; position < match.Index; position++)
                    if (strippedCode[position] == '\n') line++;
                tokens.Add(new Token(match.Value, line));
            }
            return tokens;
        }

        static bool IsAt(string text, int i, string what) => string.CompareOrdinal(text, i, what, 0, what.Length) == 0;

        static bool AtPreprocessorLine(string text, int i)
        {
            if (text[i] != '#') return false;
            for (int k = i - 1; k >= 0 && text[k] != '\n'; k--)
                if (!char.IsWhiteSpace(text[k])) return false;
            return true;
        }

        static int SkipToLineEnd(string text, int i)
        {
            while (i < text.Length && text[i] != '\n') i++;
            return i;
        }

        static int BlockCommentEnd(string text, int i)
        {
            int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + 2;
        }

        static bool AtStringPrefix(string text, int i) =>
            IsAt(text, i, "@\"") || IsAt(text, i, "$\"") || IsAt(text, i, "$@\"") || IsAt(text, i, "@$\"");

        static int StringEnd(string text, int i)
        {
            int open = text.IndexOf('"', i);
            bool verbatim = text.IndexOf('@', i, open - i) >= 0;
            for (int k = open + 1; k < text.Length; k++)
            {
                if (verbatim && IsAt(text, k, "\"\"")) k++;
                else if (!verbatim && text[k] == '\\') k++;
                else if (text[k] == '"') return k + 1;
            }
            return text.Length;
        }

        static int CharEnd(string text, int i)
        {
            int k = i + 1;
            while (k < text.Length && text[k] != '\'' && text[k] != '\n') k += text[k] == '\\' ? 2 : 1;
            return k + 1;
        }

        static int CopyNewlines(string text, int from, int to, StringBuilder result)
        {
            for (int k = from; k < to; k++)
                if (text[k] == '\n') result.Append('\n');
            result.Append(' ');
            return to;
        }

        /// <summary>Walks a file's types and members, and measures each body it finds.</summary>
        sealed class MemberWalker
        {
            static readonly HashSet<string> TypeWords = new HashSet<string> { "class", "struct", "interface", "namespace", "record" };
            static readonly HashSet<string> Accessors = new HashSet<string> { "get", "set", "init", "add", "remove" };

            readonly List<Token> _tokens;
            int _i;

            public readonly List<MethodMetrics> Found = new List<MethodMetrics>();
            public int Count => _tokens.Count;

            public MemberWalker(List<Token> tokens) => _tokens = tokens;

            public void Members(int end)
            {
                int head = _i;
                while (_i < end)
                {
                    string text = _tokens[_i].Text;
                    if (text == "[" || text == "(") _i = Closing(_i) + 1;
                    else if (text == ";") head = ++_i;
                    else if (text == "=") head = _i = StatementEnd(_i) + 1;
                    else if (text == "=>") head = Arrow(head);
                    else if (text == "{") head = Open(head);
                    else _i++;
                }
            }

            int Open(int head)
            {
                int close = Closing(_i);
                var (name, parameters) = Signature(head, _i);
                if (name != null) Record(name, parameters, _i + 1, close, _tokens[_i].Line);
                else if (IsNestingType(head)) NestedMembers(close);
                else if (IsProperty(head)) AccessorBodies(LastIdentifier(head, _i), close);
                _i = close + 1;
                return _i;
            }

            bool IsNestingType(int head)
            {
                for (int k = head; k < _i; k++)
                    if (TypeWords.Contains(_tokens[k].Text)) return true;
                return false;
            }

            bool IsProperty(int head)
            {
                for (int k = head; k < _i; k++)
                    if (_tokens[k].Text == "enum") return false;
                return true;
            }

            void NestedMembers(int close)
            {
                _i++;
                Members(close);
            }

            int Arrow(int head)
            {
                int end = StatementEnd(_i);
                var (name, parameters) = Signature(head, _i);
                Record(name ?? LastIdentifier(head, _i), parameters, _i + 1, end, _tokens[_i].Line);
                _i = end + 1;
                return _i;
            }

            void AccessorBodies(string property, int close)
            {
                for (int k = _i + 1; k < close; k++)
                {
                    if (!Accessors.Contains(_tokens[k].Text)) continue;
                    string next = _tokens[k + 1].Text;
                    if (next == "{")
                    {
                        int end = Closing(k + 1);
                        Record(property + "." + _tokens[k].Text, 0, k + 2, end, _tokens[k].Line);
                        k = end;
                    }
                    else if (next == "=>")
                    {
                        int end = StatementEnd(k + 1);
                        Record(property + "." + _tokens[k].Text, 0, k + 2, end, _tokens[k].Line);
                        k = end;
                    }
                }
            }

            void Record(string name, int parameters, int from, int to, int line)
            {
                var scorer = new BodyScorer(_tokens, from, to);
                scorer.Body();
                int lines = _tokens.Skip(from).Take(to - from).Select(t => t.Line).Distinct().Count();
                Found.Add(new MethodMetrics(name, line, parameters, lines, scorer.Cognitive, scorer.Cyclomatic, scorer.MaxDepth));
            }

            /// <summary>The name and parameter count, when the tokens before a body hold a parameter list.</summary>
            (string name, int parameters) Signature(int from, int to)
            {
                for (int k = from; k < to; k++)
                {
                    string text = _tokens[k].Text;
                    if (text == "[") k = Closing(k);
                    else if (text == "(")
                    {
                        int close = Closing(k);
                        string after = close + 1 < to ? _tokens[close + 1].Text : "";
                        if (after == "" || after == ":" || after == "where") return (NameBefore(k), ParameterCount(k, close));
                        k = close;
                    }
                }
                return (null, 0);
            }

            string NameBefore(int open)
            {
                int k = open - 1;
                if (k >= 0 && _tokens[k].Text == ">")
                {
                    int depth = 0;
                    for (; k >= 0; k--)
                    {
                        if (_tokens[k].Text == ">") depth++;
                        else if (_tokens[k].Text == "<" && --depth == 0) break;
                    }
                    k--;
                }
                return k >= 0 ? _tokens[k].Text : "?";
            }

            int ParameterCount(int open, int close)
            {
                if (close == open + 1) return 0;
                int depth = 0, count = 1;
                for (int k = open + 1; k < close; k++)
                {
                    string text = _tokens[k].Text;
                    if (text == "<" || text == "(" || text == "[") depth++;
                    else if (text == ">" || text == ")" || text == "]") depth--;
                    else if (text == "," && depth == 0) count++;
                }
                return count;
            }

            string LastIdentifier(int from, int to)
            {
                for (int k = to - 1; k >= from; k--)
                    if (char.IsLetter(_tokens[k].Text[0]) || _tokens[k].Text[0] == '_') return _tokens[k].Text;
                return "?";
            }

            int Closing(int open)
            {
                string opening = _tokens[open].Text;
                string closing = opening == "(" ? ")" : opening == "[" ? "]" : "}";
                int depth = 0;
                for (int k = open; k < _tokens.Count; k++)
                {
                    if (_tokens[k].Text == opening) depth++;
                    else if (_tokens[k].Text == closing && --depth == 0) return k;
                }
                return _tokens.Count - 1;
            }

            int StatementEnd(int from)
            {
                int depth = 0;
                for (int k = from; k < _tokens.Count; k++)
                {
                    string text = _tokens[k].Text;
                    if (text == "(" || text == "{" || text == "[") depth++;
                    else if (text == ")" || text == "}" || text == "]") depth--;
                    else if (text == ";" && depth == 0) return k;
                }
                return _tokens.Count - 1;
            }
        }

        /// <summary>Scores one body by walking its statements.</summary>
        sealed class BodyScorer
        {
            static readonly HashSet<string> Primitives = new HashSet<string> { "int", "long", "bool", "byte", "short", "uint", "ulong", "char", "string", "object" };

            readonly List<Token> _t;
            readonly int _end;
            int _i, _nesting, _depth;

            public int Cognitive, Cyclomatic = 1, MaxDepth;

            public BodyScorer(List<Token> tokens, int from, int to)
            {
                _t = tokens;
                _i = from;
                _end = to;
            }

            public void Body()
            {
                while (_i < _end)
                {
                    int before = _i;
                    Statement();
                    if (_i == before) _i++;
                }
            }

            bool At(string text) => _i < _end && _t[_i].Text == text;
            string Next => _i + 1 < _end ? _t[_i + 1].Text : "";

            void Score() => Cognitive += 1 + _nesting;

            void Enter()
            {
                _nesting++;
                EnterDepth();
            }

            void Leave()
            {
                _nesting--;
                _depth--;
            }

            void EnterDepth()
            {
                _depth++;
                if (_depth > MaxDepth) MaxDepth = _depth;
            }

            void Statement()
            {
                switch (_t[_i].Text)
                {
                    case "{": Block(); break;
                    case "if": If(false); break;
                    case "for": case "foreach": case "while": Loop(); break;
                    case "do": Do(); break;
                    case "switch": Switch(); break;
                    case "try": Try(); break;
                    case "case": Case(); break;
                    case "goto":
                        Score();
                        Scan(false);
                        break;
                    case "default" when Next == ":":
                        _i += 2;
                        break;
                    case "using": case "lock": case "fixed": Guarded(); break;
                    case "unsafe": case "checked": case "unchecked" when Next == "{":
                        _i++;
                        Block();
                        break;
                    default: Scan(false); break;
                }
            }

            void Block()
            {
                _i++;
                while (_i < _end && !At("}"))
                {
                    int before = _i;
                    Statement();
                    if (_i == before) _i++;
                }
                _i++;
            }

            void Group()
            {
                if (!At("(")) return;
                _i++;
                Scan(true);
            }

            void If(bool chained)
            {
                Cyclomatic++;
                if (!chained)
                {
                    Score();
                    Enter();
                }
                _i++;
                Group();
                Statement();
                if (At("else"))
                {
                    Cognitive++;
                    _i++;
                    if (At("if")) If(true);
                    else Statement();
                }
                if (!chained) Leave();
            }

            void Loop()
            {
                Cyclomatic++;
                Score();
                Enter();
                _i++;
                Group();
                Statement();
                Leave();
            }

            void Do()
            {
                Cyclomatic++;
                Score();
                Enter();
                _i++;
                Statement();
                Leave();
                if (!At("while")) return;
                _i++;
                Group();
                if (At(";")) _i++;
            }

            void Switch()
            {
                Score();
                Enter();
                _i++;
                Group();
                Statement();
                Leave();
            }

            void Case()
            {
                Cyclomatic++;
                _i++;
                int depth = 0;
                while (_i < _end && (depth > 0 || !At(":")))
                {
                    string text = _t[_i].Text;
                    if (text == "(" || text == "{") depth++;
                    else if (text == ")" || text == "}") depth--;
                    _i++;
                }
                _i++;
            }

            void Try()
            {
                _i++;
                EnterDepth();
                Block();
                _depth--;
                while (At("catch"))
                {
                    Cyclomatic++;
                    Score();
                    Enter();
                    _i++;
                    Group();
                    if (At("when"))
                    {
                        _i++;
                        Group();
                    }
                    Block();
                    Leave();
                }
                if (!At("finally")) return;
                _i++;
                EnterDepth();
                Block();
                _depth--;
            }

            void Guarded()
            {
                if (Next != "(")
                {
                    Scan(false);
                    return;
                }
                _i++;
                Group();
                Statement();
            }

            /// <summary>Walks an expression to its statement's `;`, or for a group to the `)` that closes it.</summary>
            void Scan(bool group)
            {
                var logical = new Stack<string>();
                logical.Push(null);
                var raised = new Stack<int>();
                var arms = new Stack<int>();
                int depth = group ? 1 : 0;
                bool declaring = true;
                while (_i < _end)
                {
                    string text = _t[_i].Text;
                    switch (text)
                    {
                        case "(": case "[": case "?[":
                            depth++;
                            logical.Push(null);
                            _i++;
                            break;
                        case ")": case "]":
                            depth--;
                            if (logical.Count > 1) logical.Pop();
                            Lower(raised, depth);
                            _i++;
                            if (group && depth == 0) return;
                            break;
                        case ",":
                            Lower(raised, depth - 1);
                            _i++;
                            break;
                        case ";":
                            _i++;
                            if (depth <= 0)
                            {
                                Lower(raised, -1);
                                return;
                            }
                            break;
                        case "&&": case "||":
                            Cyclomatic++;
                            if (logical.Peek() != text)
                            {
                                Cognitive++;
                                logical.Pop();
                                logical.Push(text);
                            }
                            _i++;
                            break;
                        case "??": case "??=":
                            Cyclomatic++;
                            _i++;
                            break;
                        case "?":
                            if (!Primitives.Contains(_t[_i - 1].Text))
                            {
                                Cyclomatic++;
                                Score();
                                _nesting++;
                                raised.Push(depth);
                            }
                            _i++;
                            break;
                        case "=>":
                            _i++;
                            if (arms.Count > 0 && arms.Peek() == depth) Cyclomatic++;
                            else if (At("{")) NestedBlock();
                            else
                            {
                                _nesting++;
                                raised.Push(depth);
                            }
                            break;
                        case "switch":
                            Cognitive += 1 + _nesting;
                            _nesting++;
                            _i++;
                            if (At("{"))
                            {
                                depth++;
                                arms.Push(depth);
                                _i++;
                            }
                            break;
                        case "{":
                            if (declaring && depth == 0 && _t[_i - 1].Text == ")")
                            {
                                NestedBlock();
                                Lower(raised, -1);
                                return;
                            }
                            depth++;
                            _i++;
                            break;
                        case "}":
                            if (depth <= 0)
                            {
                                Lower(raised, -1);
                                return;
                            }
                            if (arms.Count > 0 && arms.Peek() == depth)
                            {
                                arms.Pop();
                                _nesting--;
                            }
                            depth--;
                            _i++;
                            break;
                        case "=": case "new": case "return":
                            declaring = false;
                            _i++;
                            break;
                        default:
                            _i++;
                            break;
                    }
                }
                Lower(raised, -1);
            }

            void NestedBlock()
            {
                _nesting++;
                Block();
                _nesting--;
            }

            void Lower(Stack<int> raised, int depth)
            {
                while (raised.Count > 0 && raised.Peek() > depth)
                {
                    raised.Pop();
                    _nesting--;
                }
            }
        }
    }
}
