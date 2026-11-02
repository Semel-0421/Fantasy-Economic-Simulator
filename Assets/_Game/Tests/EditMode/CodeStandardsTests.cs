using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FES.Tests
{
    /// <summary>
    /// Enforces Docs/GDD/05_Refactoring_Standards.md. Each limit is the published default of the rule it names.
    /// </summary>
    public class CodeStandardsTests
    {
        const int MaxCognitive = 15; // Sonar S3776
        const int MaxCyclomatic = 25; // Microsoft CA1502
        const int MaxMethodLines = 80; // Sonar S138
        const int MaxNesting = 3; // Sonar S134
        const int MaxParameters = 7; // Sonar S107
        const int MaxFileLines = 1000; // Sonar S104

        static readonly string[] ProductionFolders = { "Core", "Data", "World", "Economy", "Agents", "Persistence", "Simulation", "Unity" };

        /// <summary>
        /// Methods that may break a limit, each with its reason. Microsoft names a large switch as the case to exempt.
        /// An entry that is no longer needed fails the test, so this list can only shrink.
        /// </summary>
        static readonly Dictionary<string, string> Exempt = new Dictionary<string, string>();

        // Unity calls these by name.
        static readonly HashSet<string> UnityMessages = new HashSet<string>
            { "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy", "OnGUI", "OnValidate", "Reset" };

        readonly struct Measured
        {
            public readonly string File;
            public readonly MethodMetrics Method;

            public Measured(string file, MethodMetrics method)
            {
                File = file;
                Method = method;
            }

            public string Key => File + " " + Method.Name;
            public string Where => $"{File}:{Method.Line} {Method.Name}";
        }

        static string Root => Path.Combine(Application.dataPath, "_Game");

        static IEnumerable<string> SourceFiles() => ProductionFolders
            .SelectMany(folder => Directory.GetFiles(Path.Combine(Root, folder), "*.cs", SearchOption.AllDirectories));

        static string Relative(string file) => file.Substring(Root.Length + 1).Replace('\\', '/');

        static List<Measured> AllMethods() => SourceFiles()
            .SelectMany(file => SourceMetrics.Measure(SourceMetrics.StripCommentsAndStrings(File.ReadAllText(file)))
                .Select(method => new Measured(Relative(file), method)))
            .ToList();

        static void AssertWithin(string rule, int limit, Func<MethodMetrics, int> value)
        {
            var over = AllMethods()
                .Where(m => value(m.Method) > limit && !Exempt.ContainsKey(m.Key))
                .OrderByDescending(m => value(m.Method))
                .Select(m => $"{value(m.Method)} {m.Where}")
                .ToArray();
            CollectionAssert.IsEmpty(over, $"{rule}: at most {limit}. {over.Length} over:\n{string.Join("\n", over)}\n");
        }

        [Test]
        public void Methods_CognitiveComplexity_StaysWithinTheLimit() => AssertWithin("Sonar S3776 cognitive complexity", MaxCognitive, m => m.Cognitive);

        [Test]
        public void Methods_CyclomaticComplexity_StaysWithinTheLimit() => AssertWithin("Microsoft CA1502 cyclomatic complexity", MaxCyclomatic, m => m.Cyclomatic);

        [Test]
        public void Methods_Lines_StayWithinTheLimit() => AssertWithin("Sonar S138 lines of code", MaxMethodLines, m => m.Lines);

        [Test]
        public void Methods_Nesting_StaysWithinTheLimit() => AssertWithin("Sonar S134 nesting", MaxNesting, m => m.Nesting);

        [Test]
        public void Methods_Parameters_StayWithinTheLimit() => AssertWithin("Sonar S107 parameters", MaxParameters, m => m.Parameters);

        [Test]
        public void Files_Lines_StayWithinTheLimit()
        {
            var over = SourceFiles()
                .Select(file => (lines: File.ReadAllLines(file).Length, name: Relative(file)))
                .Where(f => f.lines > MaxFileLines)
                .Select(f => $"{f.lines} {f.name}")
                .ToArray();
            CollectionAssert.IsEmpty(over, $"Sonar S104 lines per file: at most {MaxFileLines}.");
        }

        // Sonar's duplicated block: at least 100 tokens over at least 10 lines of code.
        [Test]
        public void Code_HasNoDuplicatedBlocks()
        {
            const int blockLines = 10, blockTokens = 100;
            var firstSeen = new Dictionary<string, string>();
            var duplicates = new List<string>();
            foreach (string file in SourceFiles())
            {
                var lines = SourceMetrics.CodeLines(SourceMetrics.StripCommentsAndStrings(File.ReadAllText(file)));
                for (int k = 0; k + blockLines <= lines.Count; k++)
                {
                    string block = string.Join("\n", lines.Skip(k).Take(blockLines).Select(l => l.text));
                    if (SourceMetrics.Tokens(block).Count < blockTokens) continue;
                    string here = $"{Relative(file)}:{lines[k].line}";
                    if (firstSeen.TryGetValue(block, out string there)) duplicates.Add($"{here} repeats {there}");
                    else firstSeen.Add(block, here);
                }
            }
            CollectionAssert.IsEmpty(duplicates, $"Sonar duplicated blocks: none allowed.\n{string.Join("\n", duplicates)}\n");
        }

        // Sonar S1144: a private method nothing calls. Partial types span files, so names are counted over all the code;
        // the scan sees names only, so a name in use elsewhere hides an unused method.
        [Test]
        public void PrivateMethods_AreAllCalled()
        {
            var code = SourceFiles().ToDictionary(file => file, file => SourceMetrics.StripCommentsAndStrings(File.ReadAllText(file)));
            var uses = code.Values.SelectMany(SourceMetrics.Tokens).GroupBy(t => t.Text).ToDictionary(g => g.Key, g => g.Count());
            var unused = code
                .SelectMany(f => SourceMetrics.PrivateMethodNames(f.Value).Select(name => (file: Relative(f.Key), name)))
                .Where(m => uses[m.name] == 1 && !UnityMessages.Contains(m.name))
                .Select(m => $"{m.file} {m.name}")
                .ToArray();
            CollectionAssert.IsEmpty(unused, "These private methods are never called.");
        }

        [Test]
        public void Exemptions_AreAllStillNeeded()
        {
            var measured = AllMethods();
            var stale = Exempt.Keys
                .Where(key => !measured.Any(m => m.Key == key && (m.Method.Cognitive > MaxCognitive || m.Method.Cyclomatic > MaxCyclomatic
                    || m.Method.Lines > MaxMethodLines || m.Method.Nesting > MaxNesting || m.Method.Parameters > MaxParameters)))
                .ToArray();
            CollectionAssert.IsEmpty(stale, "These exemptions no longer apply; remove them.");
        }
    }
}
