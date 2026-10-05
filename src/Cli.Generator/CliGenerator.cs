using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Cli.Generator
{
	/// <summary>Step names for incremental-caching tests.</summary>
	public static class TrackingNames
	{
		public const string Models = "CliModels";
		public const string UtilityNamespace = "CliUtilityNamespace";
	}

	/// <summary>
	/// Generates Parse/TryParse/Create/TryCreate/GetUsage/PrintUsage for partial types marked [CliArgs],
	/// from their instance fields and properties marked [CmdArg].
	/// </summary>
	[Generator(LanguageNames.CSharp)]
	public sealed class CliGenerator : IIncrementalGenerator
	{
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			context.RegisterPostInitializationOutput(ctx =>
			{
				ctx.AddSource("Cli.Attributes.g.cs", SourceText.From(ReadResource("CliAttributes.cs"), Encoding.UTF8));
			});

			var types = context.SyntaxProvider.ForAttributeWithMetadataName(
				Analyzer.CliArgsAttributeName,
				static (node, _) => node is TypeDeclarationSyntax,
				static (ctx, ct) => Analyzer.Analyze(ctx, ct))
				.WithTrackingName(TrackingNames.Models)
				.Where(static m => m != null);

			// CliUtility (with CmdException) is emitted once per project, into the namespace of the first [CliArgs] type,
			// and only when there is at least one. Every type's generated code refers to that single copy.
			var utilityNamespace = types.Collect()
				.Select(static (all, _) => all.Length == 0 ? null : all[0]!.Namespace ?? "")
				.WithTrackingName(TrackingNames.UtilityNamespace);

			context.RegisterSourceOutput(utilityNamespace, static (spc, ns) =>
			{
				if (ns != null) spc.AddSource("CliUtility.g.cs", SourceText.From(UtilitySource(ns), Encoding.UTF8));
			});

			context.RegisterSourceOutput(types.Combine(utilityNamespace), static (spc, pair) =>
			{
				var (model, ns) = pair;
				if (!model!.HasErrors)
				{
					spc.AddSource(model.HintName, SourceText.From(Emitter.Emit(model, ns!), Encoding.UTF8));
				}
			});

			// Warn about [CmdArg] members whose type isn't marked [CliArgs].
			var orphans = context.SyntaxProvider.ForAttributeWithMetadataName(
				Analyzer.CmdArgAttributeName,
				static (node, _) => node is VariableDeclaratorSyntax || node is PropertyDeclarationSyntax,
				static (ctx, _) =>
				{
					var owner = ctx.TargetSymbol.ContainingType;
					if (owner == null || owner.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == Analyzer.CliArgsAttributeName)) return null;
					return DiagInfo.Create(Descriptors.MissingCliArgs.Id, ctx.TargetSymbol.Locations.FirstOrDefault(), ctx.TargetSymbol.Name, owner.Name);
				})
				.Where(static d => d != null);

			// Diagnostics are reported separately from code generation. They're paired with the compilation so each location
			// can be tied back to its syntax tree, which is what lets #pragma warning disable work on them. Only types that
			// actually have diagnostics flow into this step, so a clean project does no work here.
			var diagnostics = types
				.Select(static (m, _) => m!.Diagnostics)
				.Where(static d => d.Count > 0)
				.Collect()
				.Combine(orphans.Collect())
				.Combine(context.CompilationProvider);

			context.RegisterSourceOutput(diagnostics, static (spc, input) =>
			{
				var ((typeDiags, orphanDiags), compilation) = input;
				foreach (var d in typeDiags.SelectMany(x => x).Concat(orphanDiags.Select(x => x!)))
				{
					spc.ReportDiagnostic(Diagnostic.Create(Descriptors.Get(d.Id), ResolveLocation(d.Location, compilation), d.Args.Cast<object>().ToArray()));
				}
			});
		}

		static Location? ResolveLocation(LocationInfo? info, Compilation compilation)
		{
			if (info == null) return null;
			foreach (var tree in compilation.SyntaxTrees)
			{
				if (tree.FilePath == info.Path && info.Span.End <= tree.Length) return Location.Create(tree, info.Span);
			}
			return info.ToLocation();
		}

		static string UtilitySource(string ns)
		{
			var text = ReadResource("CliUtility.cs").Replace("\r\n", "\n");
			if (ns.Length == 0)
			{
				return text.Replace("//#NAMESPACE_OPEN\n", "").Replace("//#NAMESPACE_CLOSE\n", "");
			}
			return text.Replace("//#NAMESPACE_OPEN", "namespace " + ns + "\n{").Replace("//#NAMESPACE_CLOSE", "}");
		}

		static string ReadResource(string name)
		{
			var asm = typeof(CliGenerator).GetTypeInfo().Assembly;
			using var stream = asm.GetManifestResourceStream("Cli.Generator.Runtime." + name)
				?? throw new FileNotFoundException("Missing embedded resource " + name);
			using var reader = new StreamReader(stream);
			return reader.ReadToEnd();
		}
	}
}
