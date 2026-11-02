using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FES.Agents;
using FES.Core;
using NUnit.Framework;
using UnityEngine;

namespace FES.Tests
{
    /// <summary>Kinds of defect that came back across reviews (META_2026-11-02, root causes 7 and 8). Each test refuses the kind, not one case.</summary>
    public class RecurringDefectTests
    {
        static string Game => Path.Combine(Application.dataPath, "_Game");
        static string Project => Path.GetDirectoryName(Application.dataPath);

        static string[] Sources(string folder) => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);

        // A command's text runs from its declaration to the next type declared in the file.
        static string CommandText(Type command)
        {
            string declaration = "class " + command.Name;
            string text = Sources(Path.Combine(Game, "Agents"))
                .Select(file => SourceMetrics.StripCommentsAndStrings(File.ReadAllText(file)))
                .First(code => code.Contains(declaration));
            text = text.Substring(text.IndexOf(declaration, StringComparison.Ordinal) + declaration.Length);
            var next = Regex.Match(text, @"\n\s*(public |internal |static |sealed |readonly )*(class|struct|enum|interface) ");
            return next.Success ? text.Substring(0, next.Index) : text;
        }

        // C16: an argument cast from a bad number passed unchecked and was stored.
        [Test]
        public void Commands_CheckEveryEnumArgument()
        {
            var unchecked_ = typeof(BuyFacilityCommand).Assembly.GetTypes()
                .Where(t => typeof(ICommand).IsAssignableFrom(t) && t.IsClass)
                .SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    .Where(f => f.FieldType.IsEnum)
                    .Select(f => (command: t, field: f.Name)))
                .Where(a => !Regex.IsMatch(CommandText(a.command), $@"IsValid\({a.field}\)|\b{a.field} *(==|!=)|Enum\.IsDefined\([^)]*{a.field}"))
                .Select(a => $"{a.command.Name}.{a.field}")
                .ToArray();
            CollectionAssert.IsEmpty(unchecked_, "These command arguments are not checked against their range.");
        }

        // Root cause 8: code that cites a decision nobody can read.
        [Test]
        public void DecisionsCited_AreAllRecorded()
        {
            string decisions = File.ReadAllText(Path.Combine(Project, "Docs", "GDD", "03_Decisions.md"));
            string data = Path.Combine(Application.streamingAssetsPath, "Data");
            var missing = Sources(Game).Concat(Directory.GetFiles(data, "*.csv"))
                .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"DEC-\d+").Cast<Match>().Select(m => m.Value))
                .Distinct()
                .Where(id => !decisions.Contains("### " + id))
                .ToArray();
            CollectionAssert.IsEmpty(missing, "Cited in code or data, absent from 03_Decisions.md.");
        }

        // Measuring tests are written as Temp*.cs and must not outlive the measurement.
        [Test]
        public void NoTemporaryTests_Remain()
        {
            var left = Sources(Path.Combine(Game, "Tests")).Select(Path.GetFileName).Where(name => name.StartsWith("Temp")).ToArray();
            CollectionAssert.IsEmpty(left, "Remove these before the work is finished.");
        }
    }
}
