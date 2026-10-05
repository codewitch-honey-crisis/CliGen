using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cli.Generator.Tests;

/// <summary>Drives the generator directly: diagnostics, clean compiles and incremental caching.</summary>
static class GeneratorTests
{
	static CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

	static readonly ImmutableArray<MetadataReference> References =
		((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator)
			.Where(p => !Path.GetFileName(p).StartsWith("Cli.Generator", StringComparison.OrdinalIgnoreCase))
			.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
			.ToImmutableArray();

	const string Usings = "using Cli; using System; using System.IO; using System.Collections.Generic;\n";

	sealed record Result(ImmutableArray<Diagnostic> Generator, ImmutableArray<Diagnostic> CompileErrors, Compilation Output, GeneratorDriver Driver);

	static CSharpCompilation Compile(params string[] sources) => CSharpCompilation.Create("TestAssembly",
		sources.Select(s => CSharpSyntaxTree.ParseText(Usings + s, ParseOptions)),
		References,
		new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

	static GeneratorDriver NewDriver() => CSharpGeneratorDriver.Create(
		[new CliGenerator().AsSourceGenerator()],
		parseOptions: ParseOptions,
		driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

	static Result Run(string source)
	{
		var driver = NewDriver().RunGeneratorsAndUpdateCompilation(Compile(source), out var output, out var diags);
		var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
		return new Result(diags, errors, output, driver);
	}

	static void ExpectDiagnostic(string id, string source)
	{
		var r = Run(source);
		if (!r.Generator.Any(d => d.Id == id))
		{
			throw new CheckFailedException($"expected {id} but got: " +
				(r.Generator.Length == 0 ? "(none)" : string.Join("; ", r.Generator.Select(d => d.Id + " " + d.GetMessage()))));
		}
		var d = r.Generator.First(x => x.Id == id);
		Check.True(d.Location != Location.None, id + " should point at source");
	}

	static void ExpectClean(string source, params string[] allowed)
	{
		var r = Run(source);
		var all = r.Generator.Where(d => !allowed.Contains(d.Id)).Concat(r.CompileErrors).ToList();
		if (all.Count > 0)
		{
			throw new CheckFailedException("expected no diagnostics but got:\n" + string.Join("\n", all.Select(d => d.ToString())));
		}
	}

	[Test] public static void CLI001_not_partial() => ExpectDiagnostic("CLI001", "[CliArgs] class A { [CmdArg] public int X; }");
	[Test] public static void CLI001_container_not_partial() => ExpectDiagnostic("CLI001", "class O { [CliArgs] partial class A { [CmdArg] public int X; } }");
	[Test] public static void CLI002_generic() => ExpectDiagnostic("CLI002", "[CliArgs] partial class A<T> { [CmdArg] public int X; }");
	[Test] public static void CLI003_abstract() => ExpectDiagnostic("CLI003", "[CliArgs] abstract partial class A { [CmdArg] public int X; }");
	[Test] public static void CLI004_no_default_ctor() => ExpectDiagnostic("CLI004", "[CliArgs] partial class A { public A(int x) {} [CmdArg] public int X; }");
	[Test] public static void CLI005_no_args() => ExpectDiagnostic("CLI005", "[CliArgs] partial class A { }");
	[Test] public static void CLI010_static() => ExpectDiagnostic("CLI010", "[CliArgs] partial class A { [CmdArg] public static int X; }");
	[Test] public static void CLI011_readonly() => ExpectDiagnostic("CLI011", "[CliArgs] partial class A { [CmdArg] public readonly int X; }");
	[Test] public static void CLI011_init_only() => ExpectDiagnostic("CLI011", "[CliArgs] partial class A { [CmdArg] public int X { get; init; } }");
	[Test] public static void CLI011_get_only_array() => ExpectDiagnostic("CLI011", "[CliArgs] partial class A { [CmdArg] public int[] X { get; } = new int[0]; }");
	[Test] public static void CLI012_name_and_ordinal() => ExpectDiagnostic("CLI012", "[CliArgs] partial class A { [CmdArg(\"x\", Ordinal = 0)] public int X; }");
	[Test] public static void CLI013_no_conversion() => ExpectDiagnostic("CLI013", "[CliArgs] partial class A { [CmdArg] public Stream? S; }");
	[Test] public static void CLI013_no_element_conversion() => ExpectDiagnostic("CLI013", "[CliArgs] partial class A { [CmdArg] public Dictionary<string, int> D = new(); }");
	[Test] public static void CLI014_bad_converter() => ExpectDiagnostic("CLI014", "[CliArgs] partial class A { [CmdArg(Converter = typeof(string))] public int X; }");
	[Test] public static void CLI015_bad_name() => ExpectDiagnostic("CLI015", "[CliArgs] partial class A { [CmdArg(\"9lives\")] public int X; }");
	[Test] public static void CLI016_bad_short() => ExpectDiagnostic("CLI016", "[CliArgs] partial class A { [CmdArg(ShortName = '9')] public int X; }");
	[Test] public static void CLI017_short_on_ordinal() => ExpectDiagnostic("CLI017", "[CliArgs] partial class A { [CmdArg(0, ShortName = 'x')] public int X; }");
	[Test] public static void CLI018_ungrouped_ordinal() => ExpectDiagnostic("CLI018",
		"[CliArgs] partial class A { [CmdArg(0)] public int X; [CmdArg(Group = \"g\", Required = true)] public bool G; }");
	[Test] public static void CLI019_duplicate_ordinal() => ExpectDiagnostic("CLI019", "[CliArgs] partial class A { [CmdArg(0)] public int X; [CmdArg(0)] public int Y; }");
	[Test] public static void CLI020_list_not_last() => ExpectDiagnostic("CLI020", "[CliArgs] partial class A { [CmdArg(0)] public string[] X = new string[0]; [CmdArg(1)] public string Y = \"\"; }");
	[Test] public static void CLI021_required_after_optional() => ExpectDiagnostic("CLI021", "[CliArgs] partial class A { [CmdArg(0, Required = false)] public int X; [CmdArg(1)] public int Y; }");
	[Test] public static void CLI022_duplicate_name() => ExpectDiagnostic("CLI022", "[CliArgs] partial class A { [CmdArg(\"x\")] public int A1; [CmdArg(\"x\")] public int A2; }");
	[Test] public static void CLI022_duplicate_with_shared() => ExpectDiagnostic("CLI022",
		"[CliArgs] partial class A { [CmdArg(\"x\")] public int A1; [CmdArg(\"x\", Group = \"g\")] public int A2; }");
	[Test] public static void CLI023_duplicate_short() => ExpectDiagnostic("CLI023", "[CliArgs] partial class A { [CmdArg(ShortName = 'a')] public int A1; [CmdArg(ShortName = 'a')] public int A2; }");
	[Test] public static void CLI024_ambiguous_groups() => ExpectDiagnostic("CLI024", "[CliArgs] partial class A { [CmdArg(Group = \"a\")] public bool A1; [CmdArg(Group = \"b\")] public bool B1; }");
	[Test] public static void CLI030_missing_cliargs() => ExpectDiagnostic("CLI030", "class A { [CmdArg] public int X; }");

	[Test]
	public static void Same_switch_name_in_different_groups_is_allowed() => ExpectClean("""
		[CliArgs] partial class A
		{
			[CmdArg("x", Group = "a", Required = true)] public int AX;
			[CmdArg("x", Group = "b")] public string BX = "";
			[CmdArg(Group = "b", Required = true)] public bool Bee;
		}
		""");

	[Test]
	public static void Groups_distinguished_by_ordinal_count_are_allowed() => ExpectClean("""
		[CliArgs] partial class A
		{
			[CmdArg(0, Group = "one")] public string X = "";
			[CmdArg(0, Group = "two")] public string Y = "";
			[CmdArg(1, Group = "two")] public string Z = "";
		}
		""");

	[Test]
	public static void Wide_spec_compiles_cleanly() => ExpectClean("""
		namespace Deep.Name.Space
		{
			public enum Mode { A, B }
			public partial class Outer
			{
				[CliArgs(ExeName = "x", Description = "Has \"quotes\" and \\ slashes\n and newlines")]
				public partial record struct Spec
				{
					public Spec() { }
					[CmdArg(0, ValueName = "in", Description = "Input")] public TextReader? Input { get; set; }
					[CmdArg(1, Required = false)] public List<string> Rest { get; set; } = new();
					[CmdArg(ShortName = 'm')] public Mode Mode = Mode.B;
					[CmdArg] public bool? Tri;
					[CmdArg] public int? Count;
					[CmdArg] public ISet<int> Set { get; set; } = new HashSet<int>();
					[CmdArg] public IEnumerable<Uri> Uris { get; set; } = Array.Empty<Uri>();
					[CmdArg] public ICollection<string> Existing { get; } = new List<string>();
					[CmdArg("class")] public string @class = "";
					[CmdArg] public DateTimeOffset When;
				}
			}
		}
		""", "CLI006");

	const string PublicSpec = """
		namespace Lib
		{
			[CliArgs] public partial class PublicOptions { [CmdArg(0)] public string Name = ""; }
		}
		""";

	[Test]
	public static void Public_type_compiles_with_internal_runtime() => ExpectClean(PublicSpec);

	[Test]
	public static void Public_type_compiles_with_CLI_PUBLIC()
	{
		var saved = ParseOptions;
		ParseOptions = ParseOptions.WithPreprocessorSymbols("CLI_PUBLIC");
		try
		{
			ExpectClean(PublicSpec);
			var r = Run(PublicSpec);
			var exc = r.Output.GetTypeByMetadataName("Lib.CmdException")!;
			Check.Equal(Accessibility.Public, exc.DeclaredAccessibility, "CmdException accessibility");
			var tryParse = r.Output.GetTypeByMetadataName("Lib.PublicOptions")!.GetMembers("TryParse").Single();
			Check.Equal(Accessibility.Public, tryParse.DeclaredAccessibility, "TryParse accessibility");
		}
		finally
		{
			ParseOptions = saved;
		}
	}

	/// <summary>The generated source for a type, matched on file name so it works with either path separator.</summary>
	static string GeneratedFor(Result r, string typeName)
	{
		static string FileName(string path) => path.Substring(path.LastIndexOfAny(['/', '\\']) + 1);
		string wanted = typeName + ".Cli.g.cs";
		return r.Driver.GetRunResult().GeneratedTrees
			.Single(t => FileName(t.FilePath) == wanted || FileName(t.FilePath).EndsWith("." + wanted))
			.ToString();
	}

	[Test] public static void CLI006_struct_with_disposables() => ExpectDiagnostic("CLI006", "[CliArgs] partial struct S { [CmdArg] public TextReader? R; }");

	[Test]
	public static void Dispose_generated_for_disposable_members()
	{
		var r = Run("[CliArgs] partial class A { [CmdArg(0)] public TextReader? R; [CmdArg] public int N; [CmdArg] public string S = \"\"; [CmdArg] public Uri? U; }");
		Check.Equal(0, r.Generator.Length + r.CompileErrors.Length, "diagnostics");
		var text = GeneratedFor(r, "A");
		Check.Contains("partial class A : global::System.IDisposable", text);
		Check.Contains("public void Dispose()", text);
		Check.Contains("DisposeArgument(this.R)) this.R = null;", text);
		Check.Contains("DisposeArgument(this.U)", text);
		Check.True(!text.Contains("DisposeArgument(this.N)") && !text.Contains("DisposeArgument(this.S)"), "value types and strings are skipped");
	}

	[Test]
	public static void Struct_still_gets_Dispose()
	{
		var r = Run("[CliArgs] partial struct S { [CmdArg] public TextReader? R; }");
		Check.Contains("public void Dispose()", GeneratedFor(r, "S"));
		Check.Equal(0, r.CompileErrors.Length, "compile errors");
	}

	[Test]
	public static void No_Dispose_without_disposable_members()
	{
		var r = Run("[CliArgs] partial class A { [CmdArg(0)] public string S = \"\"; [CmdArg] public FileInfo? F; [CmdArg] public object? O; }");
		var text = GeneratedFor(r, "A");
		Check.True(!text.Contains("Dispose()") && !text.Contains("IDisposable"), "nothing generated");
	}

	[Test]
	public static void Existing_Dispose_is_respected() => ExpectClean("""
		[CliArgs] partial class A : IDisposable { [CmdArg(0)] public TextReader? R; public void Dispose() { } }
		[CliArgs] partial class B : IDisposable { [CmdArg(0)] public TextReader? R; void IDisposable.Dispose() { } }
		class Base : IDisposable { public virtual void Dispose() { } }
		[CliArgs] partial class C : Base { [CmdArg(0)] public TextReader? R; }
		[CliArgs] partial struct D { [CmdArg(0)] public TextReader? R; public void Dispose() { } }
		""");

	static IEnumerable<SyntaxTree> UtilityTrees(Result r) =>
		r.Driver.GetRunResult().GeneratedTrees.Where(t => t.FilePath.EndsWith("CliUtility.g.cs"));

	[Test]
	public static void One_CliUtility_for_several_types_in_first_namespace()
	{
		var comp = Compile(
			"namespace First.Ns { [CliArgs] partial class A { [CmdArg(0)] public int X; } }",
			"namespace Second { [CliArgs] partial struct B { [CmdArg] public bool Y; } }",
			"namespace Third { partial class O { [CliArgs] partial class C { [CmdArg] public string Z = \"\"; } } }");
		NewDriver().RunGeneratorsAndUpdateCompilation(comp, out var output, out var diags);
		Check.Equal(0, diags.Length, "generator diagnostics");
		var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
		Check.True(errors.Count == 0, string.Join("\n", errors));
		var utilities = output.SyntaxTrees.Where(t => t.FilePath.EndsWith("CliUtility.g.cs")).ToList();
		Check.Equal(1, utilities.Count, "CliUtility files");
		Check.True(output.GetTypeByMetadataName("First.Ns.CliUtility") != null, "CliUtility is in the first type's namespace");
		Check.True(output.GetTypeByMetadataName("First.Ns.CmdException") != null, "CmdException is next to it");
		Check.True(output.GetTypeByMetadataName("Second.CliUtility") == null, "no second copy");
		var b = output.SyntaxTrees.Single(t => t.FilePath.EndsWith("Second.B.Cli.g.cs")).ToString();
		Check.Contains("global::First.Ns.CliUtility.Generated", b);
	}

	[Test]
	public static void Global_namespace_type_puts_CliUtility_in_global_namespace()
	{
		var r = Run("[CliArgs] partial class A { [CmdArg(0)] public int X; }");
		Check.Equal(0, r.CompileErrors.Length, "compile errors");
		Check.True(r.Output.GetTypeByMetadataName("CliUtility") != null, "global CliUtility");
		Check.True(!UtilityTrees(r).Single().ToString().Contains("\nnamespace "), "no namespace declaration");
	}

	[Test]
	public static void No_CliArgs_type_means_no_CliUtility()
	{
		var r = Run("class Plain { }");
		Check.Equal(0, UtilityTrees(r).Count(), "CliUtility files");
		Check.True(r.Output.GetTypeByMetadataName("Cli.CmdArgAttribute") != null, "attributes are always there");
	}

	[Test]
	public static void Types_in_user_namespace_dont_shadow_CliUtility() => ExpectClean("""
		namespace App
		{
			class File { } class Path { } class Console { } class StringBuilder { } class Exception { } class List<T> { }
			[CliArgs] partial class Options
			{
				[CmdArg(0)] public TextReader? Input;
				[CmdArg(ShortName = 'o')] public TextWriter? Output;
				[CmdArg] public string[] Tags = new string[0];
			}
		}
		""");

	[Test]
	public static void Generated_source_has_expected_members()
	{
		var r = Run("[CliArgs] partial class A { [CmdArg(0)] public int X; [CmdArg(Group = \"g\")] public bool G; }");
		// Ungrouped ordinal with groups -> error, so nothing is generated for A.
		Check.Equal(0, r.Driver.GetRunResult().GeneratedTrees.Count(t => t.FilePath.EndsWith("A.Cli.g.cs")));

		var ok = Run("namespace N { [CliArgs] partial class B { [CmdArg(0)] public int X; } }");
		var tree = ok.Driver.GetRunResult().GeneratedTrees.Single(t => t.FilePath.EndsWith("N.B.Cli.g.cs"));
		var text = tree.ToString();
		foreach (var member in new[] { "Parse(string commandLine = null)", "TryParse(", "Create(", "TryCreate(", "GetUsage(", "PrintUsage(", "GetUsageArguments(", "GetUsageDescriptions(" })
		{
			Check.Contains(member, text);
		}
		Check.True(!text.Contains("ArgumentGroup"), "no ArgumentGroup property without groups");
		Check.Contains("int.Parse(__s, global::System.Globalization.CultureInfo.InvariantCulture)", text);
	}

	[Test]
	public static void Unrelated_edits_are_cached()
	{
		var spec = "[CliArgs] partial class A { [CmdArg(0)] public int X; [CmdArg(ShortName = 'v')] public bool V; }";
		var comp = Compile(spec, "class Other { int y; }");
		var driver = NewDriver().RunGenerators(comp);

		// Edit only the unrelated file.
		var oldTree = comp.SyntaxTrees.Last();
		var newTree = CSharpSyntaxTree.ParseText(Usings + "class Other { int y; int z; }", ParseOptions);
		driver = driver.RunGenerators(comp.ReplaceSyntaxTree(oldTree, newTree));

		var tracked = driver.GetRunResult().Results.Single().TrackedSteps;
		var reasons = tracked[TrackingNames.Models].Concat(tracked[TrackingNames.UtilityNamespace])
			.SelectMany(s => s.Outputs).Select(o => o.Reason).ToList();
		Check.True(reasons.Count > 0, "model step ran");
		Check.True(reasons.All(r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
			"model should be cached, got: " + string.Join(", ", reasons));
	}

	[Test]
	public static void Comment_edit_in_spec_file_keeps_model_unchanged()
	{
		var comp = Compile("[CliArgs] partial class A { [CmdArg(0)] public int X; }");
		var driver = NewDriver().RunGenerators(comp);
		var oldTree = comp.SyntaxTrees.Single();
		var newTree = CSharpSyntaxTree.ParseText(Usings + "[CliArgs] partial class A { /* note */ [CmdArg(0)] public int X; }", ParseOptions);
		driver = driver.RunGenerators(comp.ReplaceSyntaxTree(oldTree, newTree));
		var reasons = driver.GetRunResult().Results.Single().TrackedSteps[TrackingNames.Models].SelectMany(s => s.Outputs).Select(o => o.Reason).ToList();
		// The model is re-computed (the file changed) but compares equal, so the output step is skipped.
		Check.True(reasons.All(r => r is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached), "got: " + string.Join(", ", reasons));
	}
}
