using System.Linq;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>The standards are only as good as the measure: known code must score what the rules' authors say.</summary>
    public class SourceMetricsTests
    {
        static MethodMetrics Only(string classBody)
        {
            string code = SourceMetrics.StripCommentsAndStrings("class C {\n" + classBody + "\n}\n");
            return SourceMetrics.Measure(code).Single();
        }

        [Test]
        public void Cognitive_SumOfPrimes_ScoresNestingAsInSonarsPaper()
        {
            // The paper's example, with its labelled continue written as goto: sonar-dotnet scores goto with its nesting.
            var m = Only(@"int SumOfPrimes(int max) {
                int total = 0;
                for (int i = 1; i <= max; ++i) {
                    for (int j = 2; j < i; ++j) {
                        if (i % j == 0) { goto next; }
                    }
                    total += i;
                    next: ;
                }
                return total;
            }");
            Assert.AreEqual(1 + 2 + 3 + 4, m.Cognitive);
            Assert.AreEqual(3, m.Nesting);
        }

        [Test]
        public void Cognitive_Switch_ScoresOneAsInSonarsPaper()
        {
            var m = Only(@"string GetWords(int number) {
                switch (number) {
                    case 1: return ""one"";
                    case 2: return ""a couple"";
                    default: return ""lots"";
                }
            }");
            Assert.AreEqual(1, m.Cognitive);
            Assert.AreEqual(3, m.Cyclomatic, "1 plus each case label");
        }

        [Test]
        public void Cognitive_ElseIfAndElse_AddOneFlatWithoutNesting()
        {
            var m = Only(@"void F(int a) {
                if (a > 0) { Do(); }
                else if (a < 0) { Do(); }
                else { Do(); }
            }");
            Assert.AreEqual(3, m.Cognitive);
            Assert.AreEqual(1, m.Nesting);
        }

        [Test]
        public void Cognitive_NestedInElseIf_CountsTheNesting()
        {
            var m = Only(@"void F(int a) {
                if (a > 0) { Do(); }
                else if (a < 0) { if (a < -1) { Do(); } }
            }");
            Assert.AreEqual(1 + 1 + 2, m.Cognitive);
            Assert.AreEqual(2, m.Nesting);
        }

        [Test]
        public void Cognitive_LogicalOperators_CountEachChangeOfOperator()
        {
            Assert.AreEqual(1 + 1, Only("void F(bool a, bool b, bool c) { if (a && b && c) Do(); }").Cognitive, "same operator: one sequence");
            Assert.AreEqual(1 + 3, Only("void F(bool a, bool b, bool c, bool d) { if (a && b || c && d) Do(); }").Cognitive, "three sequences");
        }

        [Test]
        public void Cognitive_BracelessBodies_NestLikeBracedOnes()
        {
            var m = Only(@"void F(int n) {
                for (int i = 0; i < n; i++)
                    if (i > 1)
                        Do();
                    else
                        Do();
                for (int i = 0; i < n; i++) Do();
            }");
            Assert.AreEqual((1) + (2) + (1) + (1), m.Cognitive);
            Assert.AreEqual(2, m.Nesting);
        }

        [Test]
        public void Cognitive_Conditional_CountsNestingAndNestsInItself()
        {
            Assert.AreEqual(1 + 2, Only("int F(bool a) { if (a) return a ? 1 : 2; return 0; }").Cognitive);
            Assert.AreEqual(1 + 2, Only("int F(bool a, bool b) { return a ? 1 : b ? 2 : 3; }").Cognitive);
            Assert.AreEqual(1 + 1, Only("void F(bool a, bool b) { G(a ? 1 : 2, b ? 3 : 4); }").Cognitive, "side by side, not nested");
        }

        [Test]
        public void Cognitive_Lambda_RaisesNestingWithoutScoring()
        {
            Assert.AreEqual(2, Only("void F() { Run(() => { if (x) Do(); }); }").Cognitive);
            Assert.AreEqual(2, Only("void F() { Run(y => y ? 1 : 2); }").Cognitive);
        }

        [Test]
        public void Cognitive_TryIsFreeAndCatchScores()
        {
            var m = Only(@"void F() {
                try { Do(); }
                catch (Exception e) when (e != null) { Do(); }
                finally { Do(); }
            }");
            Assert.AreEqual(1, m.Cognitive);
            Assert.AreEqual(1, m.Nesting);
        }

        [Test]
        public void Nesting_CountsTryButNotElseIfAsS134Does()
        {
            var m = Only(@"void F(int a) {
                try {
                    if (a > 0) {
                        for (;;) { if (a > 1) { Do(); } }
                    }
                }
                finally { }
            }");
            Assert.AreEqual(4, m.Nesting);
        }

        [Test]
        public void Members_FindEveryKindOfBody()
        {
            string code = SourceMetrics.StripCommentsAndStrings(@"
                namespace N {
                    [Attr(1)] public struct S<T> where T : struct {
                        static readonly Func<int, int> Field = x => x > 0 ? 1 : 2;
                        public int[] Arr = { 1, 2 };
                        enum E { A, B }
                        public S(int a) : this() { }
                        public (int, int) Tuple(int a, Dictionary<int, int> b, int c = 2) { return (a, c); }
                        public int Arrow(int a) => a > 0 ? 1 : 2;
                        public int Prop => 1;
                        public int Full { get { return 1; } set { Do(); } }
                        public int Auto { get; set; }
                        public static S<T> operator +(S<T> a, S<T> b) => a;
                        void Generic<U>(U u) where U : class { }
                        abstract void Abstract(int a);
                    }
                }");
            var found = SourceMetrics.Measure(code);
            CollectionAssert.AreEquivalent(
                new[] { "S", "Tuple", "Arrow", "Prop", "Full.get", "Full.set", "operator", "Generic" },
                found.Select(m => m.Name));
            Assert.AreEqual(3, found.Single(m => m.Name == "Tuple").Parameters);
            Assert.AreEqual(1, found.Single(m => m.Name == "Arrow").Cognitive);
        }

        [Test]
        public void Lines_CountOnlyLinesWithCode()
        {
            var m = Only(@"void F() {
                Do(); // a comment

                // only a comment
                /* a block
                   comment */
                Do(""not { a brace"");
            }");
            Assert.AreEqual(2, m.Lines);
        }

        [Test]
        public void PrivateMethods_FindOnlyMethodsWithoutAnAccessWord()
        {
            string code = SourceMetrics.StripCommentsAndStrings(@"class C {
                void Plain(int a) { }
                static long Arrow(int a) => a;
                private int Marked() { return 1; }
                public void Open() { }
                internal void Inside() { }
                public override string ToString() { return """"; }
                void F() {
                    int x = Call(1);
                    return Other(x);
                }
            }");
            CollectionAssert.AreEquivalent(new[] { "Plain", "Arrow", "Marked", "F" }, SourceMetrics.PrivateMethodNames(code));
        }

        [Test]
        public void CodeLines_DropBracesAndCommentsAndEvenOutSpaces()
        {
            var lines = SourceMetrics.CodeLines(SourceMetrics.StripCommentsAndStrings("{\n  int   a; // x\n\n}\n"));
            Assert.AreEqual((2, "int a;"), lines.Single());
        }

        [Test]
        public void Strip_LeavesLineNumbersInPlace()
        {
            var m = SourceMetrics.Measure(SourceMetrics.StripCommentsAndStrings("class C {\n/* a\nb */\nvoid F() { }\n}")).Single();
            Assert.AreEqual(4, m.Line);
        }
    }
}
