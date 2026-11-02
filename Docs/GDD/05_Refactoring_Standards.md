---
doc: REFACTORING
title: Refactoring standards
status: draft
updated: 2026-11-02
related: [DEC-108, 01_Extension_Guide]
---

# Refactoring standards

The limits below are the published defaults of the rules they name, not numbers picked for this project. Tests enforce them
(`CodeStandardsTests`, `GoldenRunTests`), so code that breaks them fails the EditMode run (DEC-108).

## What counts as a refactoring
A change to the internal structure of the code that leaves its observable behaviour unchanged, made as a series of small
steps, with the system working after each one ([refactoring.com](https://refactoring.com/)). Here, observable behaviour is
what a run saves: a refactoring does not change a single byte of `GoldenRunTests`' saved states.

## Procedure
1. **Record the starting point.** All EditMode tests pass. Before touching a hot path, run
   `FullScale_ShippedRules_StayWithinTurnBudget` (explicit) and keep its turn time.
2. **Change the structure only, in small batches.** No rule, number or bug fix goes in with it, not even a bug found on the way:
   note it and fix it in its own change.
3. **After each batch**, recompile and run `GoldenRunTests`. A changed hash means the batch changed behaviour: find out why and
   undo it. Do not record a new hash.
4. **Before committing**, run all EditMode tests, and the scale test again if a hot path moved: the turn time stays within noise.
5. **Keep it apart.** A commit that records new golden hashes is a behaviour change with its own decision, not a refactoring.

## Limits
| What | Limit | Rule and source |
|---|---|---|
| Cognitive complexity of a method | 15 | Sonar S3776 ([default in sonar-dotnet](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/CognitiveComplexityBase.cs)) |
| Cyclomatic complexity of a method | 25 | Microsoft [CA1502](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1502) |
| Lines of code in a method | 80 | Sonar S138 ([default](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/MethodsShouldNotHaveTooManyLinesBase.cs)) |
| Nesting of control flow | 3 | Sonar S134 ([default](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/FunctionNestingDepthBase.cs)) |
| Parameters of a method | 7 | Sonar S107 ([default](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/TooManyParametersBase.cs)) |
| Lines in a file | 1,000 | Sonar S104 ([default](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/FileLinesBase.cs)) |
| Duplicated block | none of 100 tokens over 10 lines | [Sonar's definition](https://docs.sonarsource.com/sonarqube-server/latest/user-guide/code-metrics/metrics-definition/) |
| Private method nothing calls | none | Sonar [S1144](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/rspec/cs/S1144.json) |

Cognitive complexity counts as sonar-dotnet's
[C# metric](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.CSharp/Metrics/CSharpCognitiveComplexityMetric.cs)
does. Microsoft's page on [cyclomatic complexity](https://learn.microsoft.com/en-us/visualstudio/code-quality/code-metrics-cyclomatic-complexity)
quotes NIST on 10 as a starting point; this project takes CA1502's 25 because cognitive complexity is the tighter limit in practice.

The limits apply to production code (`Core`, `Data`, `World`, `Economy`, `Agents`, `Persistence`, `Simulation`, `Unity`), not to tests.

## Exemptions
A method may break a limit only with an entry and a reason in `CodeStandardsTests.Exempt`. Microsoft names a large `switch` as
the case to exempt, and Fowler warns that [a smell is only a sign to look deeper](https://martinfowler.com/bliki/CodeSmell.html),
so an exemption is allowed when splitting would make the code worse. An entry no longer needed fails the test, so the list can
only shrink. There are none now.

## Smells to look for by hand
A test cannot see these, so a review looks for them ([catalogue](https://refactoring.guru/refactoring/smells)): primitive obsession,
data clumps, divergent change, shotgun surgery, feature envy, inappropriate intimacy, message chains, speculative generality.

## What the checks do not do
- `SourceMetrics` reads source without a compiler, because Unity projects cannot run the Roslyn analyzers from a test. Its scores
  follow the rules' published counting, checked in `SourceMetricsTests`, but it leaves out the +1 Sonar adds for direct recursion,
  and it can differ from the real analyzers on unusual syntax.
- The check for unused methods sees names only, and only private methods: a name in use elsewhere hides an unused method.
- Microsoft's maintainability index (CA1505) and class coupling (CA1506) need a compiler, so they are not checked.
- The golden runs cover the sample world and some settings, seeds and spans, not every path. Player commands are covered by the other tests.
