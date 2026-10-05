using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cli.Generator
{
	/// <summary>Turns a [CliArgs] type symbol into an equatable <see cref="TypeModel"/> plus diagnostics.</summary>
	internal static class Analyzer
	{
		public const string CliArgsAttributeName = "Cli.CliArgsAttribute";
		public const string CmdArgAttributeName = "Cli.CmdArgAttribute";

		static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat
			.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

		sealed class ArgBuilder
		{
			public int Id;
			public string MemberName = "";
			public Location? Location;
			public string LongName = "";
			public char ShortName;
			public int DeclaredOrdinal = -1;
			public int Position = -1;
			public ArgKind Kind;
			public bool Required;
			public string ValueName = "";
			public string? Description;
			public string Group = "";
			public string ConvertTemplate = "";
			public CollectionShape Shape;
			public string? ElementType;
			public string? CreateExpression;
			public bool MayDispose;
			public bool StaticallyDisposable;
			public bool Settable;

			public bool IsPositional => DeclaredOrdinal >= 0;

			public ArgModel ToModel() => new ArgModel(Id, MemberName, LongName, ShortName, IsPositional ? Position : -1, Kind, Required,
				ValueName, Description, Group, ConvertTemplate, Shape, ElementType, CreateExpression, !Required && Kind != ArgKind.Flag,
				MayDispose, StaticallyDisposable, Settable);
		}

		public static TypeModel? Analyze(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
		{
			if (ctx.TargetSymbol is not INamedTypeSymbol type) return null;
			var decl = (TypeDeclarationSyntax)ctx.TargetNode;
			var compilation = ctx.SemanticModel.Compilation;
			var diags = new List<DiagInfo>();
			var typeLocation = decl.Identifier.GetLocation();

			if (!IsPartial(type, ct))
			{
				diags.Add(DiagInfo.Create(Descriptors.NotPartial.Id, typeLocation, type.Name));
			}
			var containers = new List<string>();
			for (var c = type.ContainingType; c != null; c = c.ContainingType)
			{
				if (!IsPartial(c, ct))
				{
					diags.Add(DiagInfo.Create(Descriptors.NotPartial.Id, typeLocation, c.Name));
				}
				if (c.IsGenericType)
				{
					diags.Add(DiagInfo.Create(Descriptors.Generic.Id, typeLocation, c.Name));
				}
				containers.Insert(0, "partial " + Keyword(c, ct) + " " + c.Name);
			}
			if (type.IsGenericType)
			{
				diags.Add(DiagInfo.Create(Descriptors.Generic.Id, typeLocation, type.Name));
			}
			if (type.IsStatic || type.IsAbstract || (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct))
			{
				diags.Add(DiagInfo.Create(Descriptors.NotInstantiable.Id, typeLocation, type.Name));
			}
			else if (type.TypeKind == TypeKind.Class && !type.InstanceConstructors.Any(c => c.Parameters.Length == 0))
			{
				diags.Add(DiagInfo.Create(Descriptors.NoDefaultCtor.Id, typeLocation, type.Name));
			}

			string? exeName = null;
			string? description = null;
			foreach (var a in ctx.Attributes)
			{
				foreach (var na in a.NamedArguments)
				{
					if (na.Key == "ExeName") exeName = na.Value.Value as string;
					else if (na.Key == "Description") description = na.Value.Value as string;
				}
			}

			var args = new List<ArgBuilder>();
			foreach (var member in type.GetMembers())
			{
				ct.ThrowIfCancellationRequested();
				if (member.IsImplicitlyDeclared) continue;
				var attr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CmdArgAttributeName);
				if (attr == null) continue;
				var arg = AnalyzeMember(member, attr, type, compilation, diags, args.Count);
				if (arg != null) args.Add(arg);
			}
			if (args.Count == 0)
			{
				diags.Add(DiagInfo.Create(Descriptors.NoArgs.Id, typeLocation, type.Name));
			}

			var groups = AnalyzeGroups(args, diags, out bool hasNamedGroups);

			// Dispose() is generated when any argument is statically disposable, unless the type already has one.
			bool generateDispose = args.Any(a => a.StaticallyDisposable) && !HasDispose(type, compilation);
			if (generateDispose && type.TypeKind == TypeKind.Struct)
			{
				diags.Add(DiagInfo.Create(Descriptors.DisposableStruct.Id, typeLocation, type.Name));
			}

			string fullName = type.ToDisplayString(FullyQualified);
			string ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
			string hint = fullName.Replace("global::", "").Replace('<', '_').Replace('>', '_') + ".Cli.g.cs";

			return new TypeModel(
				IsEffectivelyPublic(type),
				ns.Length == 0 ? null : ns,
				containers.ToEquatableArray(),
				"partial " + Keyword(type, ct) + " " + type.Name,
				type.Name,
				fullName,
				hint,
				exeName,
				description,
				args.Select(a => a.ToModel()).ToEquatableArray(),
				groups.ToEquatableArray(),
				hasNamedGroups,
				generateDispose,
				diags.ToEquatableArray());
		}

		/// <summary>True when the type or a base type already has a parameterless Dispose, or implements IDisposable.Dispose explicitly.</summary>
		static bool HasDispose(INamedTypeSymbol type, Compilation compilation)
		{
			for (INamedTypeSymbol? t = type; t != null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
			{
				foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
				{
					if (m.Parameters.Length != 0 || m.IsStatic) continue;
					if (!SymbolEqualityComparer.Default.Equals(t, type) && m.DeclaredAccessibility == Accessibility.Private && m.ExplicitInterfaceImplementations.Length == 0) continue;
					if (m.Name == "Dispose") return true;
					if (m.ExplicitInterfaceImplementations.Any(i => i.Name == "Dispose" && i.ContainingType.SpecialType == SpecialType.System_IDisposable)) return true;
				}
			}
			return false;
		}

		static bool Implements(ITypeSymbol t, SpecialType iface)
			=> t.SpecialType == iface || t.AllInterfaces.Any(i => i.SpecialType == iface);

		/// <summary>Whether a value of this static type might be disposable at runtime (old CliUtility disposed any IDisposable argument).</summary>
		static bool CouldBeDisposable(ITypeSymbol t)
		{
			if (Implements(t, SpecialType.System_IDisposable)) return true;
			if (t.IsValueType || t.SpecialType == SpecialType.System_String) return false;
			if (t.TypeKind == TypeKind.Interface || t.SpecialType == SpecialType.System_Object) return true;
			return t is INamedTypeSymbol n && n.TypeKind == TypeKind.Class && !n.IsSealed;
		}

		static bool IsEffectivelyPublic(INamedTypeSymbol type)
		{
			for (INamedTypeSymbol? t = type; t != null; t = t.ContainingType)
			{
				if (t.DeclaredAccessibility != Accessibility.Public && t.DeclaredAccessibility != Accessibility.Protected
					&& t.DeclaredAccessibility != Accessibility.ProtectedOrInternal) return false;
			}
			return true;
		}

		static bool IsPartial(INamedTypeSymbol type, CancellationToken ct)
		{
			foreach (var r in type.DeclaringSyntaxReferences)
			{
				if (r.GetSyntax(ct) is TypeDeclarationSyntax t && t.Modifiers.Any(SyntaxKind.PartialKeyword)) return true;
			}
			return false;
		}

		static string Keyword(INamedTypeSymbol type, CancellationToken ct)
		{
			foreach (var r in type.DeclaringSyntaxReferences)
			{
				if (r.GetSyntax(ct) is RecordDeclarationSyntax rec)
				{
					return rec.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record";
				}
				if (r.GetSyntax(ct) is TypeDeclarationSyntax t)
				{
					return t.Keyword.Text;
				}
			}
			return type.TypeKind == TypeKind.Struct ? "struct" : "class";
		}

		static ArgBuilder? AnalyzeMember(ISymbol member, AttributeData attr, INamedTypeSymbol owner, Compilation compilation, List<DiagInfo> diags, int id)
		{
			var location = member.Locations.FirstOrDefault();
			ITypeSymbol memberType;
			bool settable;
			switch (member)
			{
				case IFieldSymbol f:
					memberType = f.Type;
					settable = !f.IsReadOnly && !f.IsConst;
					break;
				case IPropertySymbol p:
					memberType = p.Type;
					settable = p.SetMethod != null && !p.SetMethod.IsInitOnly;
					break;
				default:
					return null;
			}
			if (member.IsStatic)
			{
				diags.Add(DiagInfo.Create(Descriptors.StaticMember.Id, location, member.Name));
				return null;
			}

			int ordinal = -1;
			string? name = null;
			char shortName = '\0';
			bool? required = null;
			string? valueName = null;
			string? description = null;
			string? group = null;
			ITypeSymbol? converter = null;
			if (attr.ConstructorArguments.Length == 1)
			{
				var v = attr.ConstructorArguments[0].Value;
				if (v is int i) ordinal = i;
				else if (v is string s) name = s;
			}
			foreach (var na in attr.NamedArguments)
			{
				var v = na.Value.Value;
				switch (na.Key)
				{
					case "Name": name = v as string; break;
					case "ShortName": if (v is char c) shortName = c; break;
					case "Ordinal": if (v is int o) ordinal = o; break;
					case "Required": if (v is bool b) required = b; break;
					case "ValueName": valueName = v as string; break;
					case "Description": description = v as string; break;
					case "Group": group = v as string; break;
					case "Converter": converter = v as ITypeSymbol; break;
				}
			}
			if (ordinal < -1) ordinal = -1;
			bool positional = ordinal >= 0;
			if (positional && !string.IsNullOrEmpty(name))
			{
				// Name given via the string constructor or the property together with an ordinal.
				if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string || attr.NamedArguments.Any(n => n.Key == "Name"))
				{
					diags.Add(DiagInfo.Create(Descriptors.NameAndOrdinal.Id, location, member.Name));
					return null;
				}
			}
			if (positional && shortName != '\0')
			{
				diags.Add(DiagInfo.Create(Descriptors.ShortOnOrdinal.Id, location, member.Name));
				shortName = '\0';
			}

			var arg = new ArgBuilder
			{
				Id = id,
				MemberName = member.Name,
				Location = location,
				DeclaredOrdinal = ordinal,
				ShortName = shortName,
				Required = required ?? positional,
				Description = description,
				Group = group ?? "",
				ValueName = string.IsNullOrEmpty(valueName) ? Kebab(member.Name) : valueName!,
				LongName = positional ? "" : (string.IsNullOrEmpty(name) ? Kebab(member.Name) : name!)
			};

			if (!positional)
			{
				if (!IsValidLongName(arg.LongName))
				{
					diags.Add(DiagInfo.Create(Descriptors.BadLongName.Id, location, member.Name, arg.LongName));
					return null;
				}
				if (shortName != '\0' && !char.IsLetter(shortName))
				{
					diags.Add(DiagInfo.Create(Descriptors.BadShortName.Id, location, member.Name, shortName.ToString()));
					return null;
				}
			}

			if (converter != null)
			{
				var tc = compilation.GetTypeByMetadataName("System.ComponentModel.TypeConverter");
				bool ok = tc != null && converter is INamedTypeSymbol nc && !nc.IsAbstract && InheritsFrom(nc, tc)
					&& nc.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
				if (!ok)
				{
					diags.Add(DiagInfo.Create(Descriptors.BadConverter.Id, location, member.Name, converter.ToDisplayString()));
					return null;
				}
			}

			// Flags: named bool / bool?
			if (!positional && IsBool(memberType))
			{
				if (!settable)
				{
					diags.Add(DiagInfo.Create(Descriptors.NotSettable.Id, location, member.Name));
					return null;
				}
				arg.Kind = ArgKind.Flag;
				arg.ConvertTemplate = "{G}.ParseBool({0})";
				return arg;
			}

			var (shape, element, create) = GetCollectionShape(memberType, settable, compilation);
			if ((shape == CollectionShape.None || shape == CollectionShape.Array) && !settable)
			{
				diags.Add(DiagInfo.Create(Descriptors.NotSettable.Id, location, member.Name));
				return null;
			}
			var valueType = shape == CollectionShape.None ? memberType : element!;
			var template = GetConversion(valueType, converter, owner, compilation);
			if (template == null)
			{
				diags.Add(DiagInfo.Create(Descriptors.NoConversion.Id, location, member.Name, valueType.ToDisplayString()));
				return null;
			}
			arg.Kind = shape == CollectionShape.None ? ArgKind.Scalar : ArgKind.List;
			arg.Shape = shape;
			arg.ConvertTemplate = template;
			arg.ElementType = shape == CollectionShape.None ? null : element!.ToDisplayString(FullyQualified);
			arg.CreateExpression = create;
			arg.Settable = settable;
			arg.MayDispose = CouldBeDisposable(valueType);
			arg.StaticallyDisposable = Implements(valueType, SpecialType.System_IDisposable);
			return arg;
		}

		/// <summary>The type's display name without a nullable reference annotation (TextReader? → System.IO.TextReader).</summary>
		static string Plain(ITypeSymbol t) => t.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

		static bool IsBool(ITypeSymbol t)
		{
			if (t.SpecialType == SpecialType.System_Boolean) return true;
			return t is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
				&& n.TypeArguments[0].SpecialType == SpecialType.System_Boolean;
		}

		static bool InheritsFrom(INamedTypeSymbol t, INamedTypeSymbol baseType)
		{
			for (var b = t.BaseType; b != null; b = b.BaseType)
			{
				if (SymbolEqualityComparer.Default.Equals(b, baseType)) return true;
			}
			return false;
		}

		static (CollectionShape Shape, ITypeSymbol? Element, string? Create) GetCollectionShape(ITypeSymbol type, bool settable, Compilation compilation)
		{
			if (type.SpecialType == SpecialType.System_String) return (CollectionShape.None, null, null);
			if (type is IArrayTypeSymbol arr)
			{
				return arr.Rank == 1 ? (CollectionShape.Array, arr.ElementType, null) : (CollectionShape.None, null, null);
			}
			if (type is not INamedTypeSymbol named) return (CollectionShape.None, null, null);

			if (named.TypeKind == TypeKind.Interface && named.IsGenericType && named.TypeArguments.Length == 1)
			{
				var def = named.OriginalDefinition.ToDisplayString();
				var e = named.TypeArguments[0];
				string ef = e.ToDisplayString(FullyQualified);
				switch (def)
				{
					case "System.Collections.Generic.IEnumerable<T>":
					case "System.Collections.Generic.ICollection<T>":
					case "System.Collections.Generic.IList<T>":
					case "System.Collections.Generic.IReadOnlyCollection<T>":
					case "System.Collections.Generic.IReadOnlyList<T>":
						if (settable) return (CollectionShape.Create, e, "new global::System.Collections.Generic.List<" + ef + ">()");
						break;
					case "System.Collections.Generic.ISet<T>":
						if (settable) return (CollectionShape.Create, e, "new global::System.Collections.Generic.HashSet<" + ef + ">()");
						break;
				}
				if (def == "System.Collections.Generic.ICollection<T>" || def == "System.Collections.Generic.IList<T>" || def == "System.Collections.Generic.ISet<T>")
				{
					return (CollectionShape.Existing, e, null);
				}
				return (CollectionShape.None, null, null);
			}

			if (named.TypeKind != TypeKind.Class) return (CollectionShape.None, null, null);
			var icoll = named.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>");
			if (icoll == null) return (CollectionShape.None, null, null);
			var element = icoll.TypeArguments[0];
			if (settable && !named.IsAbstract && named.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
			{
				return (CollectionShape.Create, element, "new " + named.ToDisplayString(FullyQualified) + "()");
			}
			return (CollectionShape.Existing, element, null);
		}

		/// <summary>
		/// Picks how to turn a string into <paramref name="type"/>. Returns a C# expression template where {0} is the string,
		/// or null when no conversion is available.
		/// </summary>
		static string? GetConversion(ITypeSymbol type, ITypeSymbol? converter, INamedTypeSymbol owner, Compilation compilation)
		{
			string fq = type.ToDisplayString(FullyQualified);
			if (converter != null)
			{
				return "(" + fq + ")new " + converter.ToDisplayString(FullyQualified) + "().ConvertFromInvariantString({0})";
			}
			if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
			{
				var inner = GetConversion(nullable.TypeArguments[0], null, owner, compilation);
				return inner == null ? null : "(" + fq + ")(" + inner + ")";
			}
			switch (type.SpecialType)
			{
				case SpecialType.System_String: return "{0}";
				case SpecialType.System_Object: return "(object){0}";
				case SpecialType.System_Boolean: return "{G}.ParseBool({0})";
			}
			string display = Plain(type);
			if (display == "System.IO.TextReader") return "{G}.OpenReader({0})";
			if (display == "System.IO.TextWriter") return "{G}.OpenWriter({0})";
			if (type.TypeKind == TypeKind.Enum)
			{
				return "(" + fq + ")global::System.Enum.Parse(typeof(" + fq + "), {0}, true)";
			}
			if (type is not INamedTypeSymbol named) return null;

			IMethodSymbol? parseWithProvider = null;
			IMethodSymbol? parse = null;
			foreach (var m in named.GetMembers("Parse").OfType<IMethodSymbol>())
			{
				if (!m.IsStatic || m.IsGenericMethod || !compilation.IsSymbolAccessibleWithin(m, owner)) continue;
				if (!SymbolEqualityComparer.Default.Equals(m.ReturnType.WithNullableAnnotation(NullableAnnotation.NotAnnotated), type.WithNullableAnnotation(NullableAnnotation.NotAnnotated))) continue;
				if (m.Parameters.Any(p => p.RefKind != RefKind.None)) continue;
				if (m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_String)
				{
					parse = m;
				}
				else if (m.Parameters.Length == 2 && m.Parameters[0].Type.SpecialType == SpecialType.System_String
					&& Plain(m.Parameters[1].Type) == "System.IFormatProvider")
				{
					parseWithProvider = m;
				}
			}
			if (parseWithProvider != null) return fq + ".Parse({0}, global::System.Globalization.CultureInfo.InvariantCulture)";
			if (parse != null) return fq + ".Parse({0})";

			if (type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.ComponentModel.TypeConverterAttribute"))
			{
				return "(" + fq + ")global::System.ComponentModel.TypeDescriptor.GetConverter(typeof(" + fq + ")).ConvertFromInvariantString({0})";
			}
			if (!named.IsAbstract && named.TypeKind != TypeKind.Interface)
			{
				foreach (var c in named.InstanceConstructors)
				{
					if (c.Parameters.Length == 1 && c.Parameters[0].Type.SpecialType == SpecialType.System_String
						&& c.Parameters[0].RefKind == RefKind.None && compilation.IsSymbolAccessibleWithin(c, owner))
					{
						return "new " + fq + "({0})";
					}
				}
			}
			return null;
		}

		static List<GroupModel> AnalyzeGroups(List<ArgBuilder> args, List<DiagInfo> diags, out bool hasNamedGroups)
		{
			var names = new List<string>();
			foreach (var a in args)
			{
				if (a.Group.Length > 0 && !names.Contains(a.Group)) names.Add(a.Group);
			}
			hasNamedGroups = names.Count > 0;
			if (!hasNamedGroups) names.Add("");
			else
			{
				foreach (var a in args)
				{
					if (a.Group.Length == 0 && a.IsPositional)
					{
						diags.Add(DiagInfo.Create(Descriptors.UngroupedOrdinal.Id, a.Location, a.MemberName));
					}
				}
			}

			var result = new List<GroupModel>();
			var metrics = new List<(string Name, int Min, int Max, List<ArgBuilder> Named)>();
			foreach (var g in names)
			{
				string where = g.Length == 0 ? "" : " in group '" + g + "'";
				var members = args.Where(a => a.Group == g || (a.Group.Length == 0 && !a.IsPositional)).ToList();
				var positional = members.Where(a => a.IsPositional && a.Group == g).OrderBy(a => a.DeclaredOrdinal).ToList();
				var named = members.Where(a => !a.IsPositional).ToList();

				for (int i = 1; i < positional.Count; ++i)
				{
					if (positional[i].DeclaredOrdinal == positional[i - 1].DeclaredOrdinal)
					{
						diags.Add(DiagInfo.Create(Descriptors.DuplicateOrdinal.Id, positional[i].Location,
							positional[i - 1].MemberName, positional[i].MemberName, positional[i].DeclaredOrdinal.ToString(), where));
					}
				}
				for (int i = 0; i < positional.Count; ++i)
				{
					positional[i].Position = i;
					if (positional[i].Kind == ArgKind.List && i < positional.Count - 1)
					{
						diags.Add(DiagInfo.Create(Descriptors.ListNotLast.Id, positional[i].Location,
							positional[i].MemberName, where, positional[i + 1].MemberName));
					}
					if (i > 0 && positional[i].Required && !positional[i - 1].Required)
					{
						diags.Add(DiagInfo.Create(Descriptors.RequiredAfterOptional.Id, positional[i].Location,
							positional[i].MemberName, positional[i - 1].MemberName, where));
					}
				}
				for (int i = 0; i < named.Count; ++i)
				{
					for (int j = 0; j < i; ++j)
					{
						if (named[i].LongName == named[j].LongName)
						{
							diags.Add(DiagInfo.Create(Descriptors.DuplicateLongName.Id, named[i].Location, named[j].MemberName, named[i].MemberName, named[i].LongName, where));
						}
						if (named[i].ShortName != '\0' && named[i].ShortName == named[j].ShortName)
						{
							diags.Add(DiagInfo.Create(Descriptors.DuplicateShortName.Id, named[i].Location, named[j].MemberName, named[i].MemberName, named[i].ShortName.ToString(), where));
						}
					}
				}

				int min = positional.Count(a => a.Required);
				int max = positional.Any(a => a.Kind == ArgKind.List) ? int.MaxValue : positional.Count;
				metrics.Add((g, min, max, named));

				bool anyShort = named.Any(a => a.ShortName != '\0');
				var lefts = new List<string>();
				var rowIds = new List<int>();
				var synopsis = new List<string>();
				foreach (var a in positional)
				{
					string core = "<" + a.ValueName + ">" + (a.Kind == ArgKind.List ? "..." : "");
					synopsis.Add(a.Required ? core : "[" + core + "]");
					lefts.Add(core);
					rowIds.Add(a.Id);
				}
				foreach (var a in named)
				{
					string value = a.Kind == ArgKind.Flag ? "" : " <" + a.ValueName + ">" + (a.Kind == ArgKind.List ? "..." : "");
					string core = "--" + a.LongName + value;
					synopsis.Add((a.Required ? core : "[" + core + "]").Replace(' ', ' '));
					string prefix = a.ShortName != '\0' ? "-" + a.ShortName + ", " : (anyShort ? "    " : "");
					lefts.Add(prefix + core);
					rowIds.Add(a.Id);
				}
				result.Add(new GroupModel(g,
					positional.Select(a => a.Id).ToEquatableArray(),
					named.Select(a => a.Id).ToEquatableArray(),
					string.Join(" ", synopsis),
					lefts.ToEquatableArray(),
					rowIds.ToEquatableArray()));
			}

			// Every pair of groups must be distinguishable from the command line alone.
			for (int i = 0; i < metrics.Count; ++i)
			{
				for (int j = i + 1; j < metrics.Count; ++j)
				{
					var a = metrics[i];
					var b = metrics[j];
					bool disjoint = a.Max < b.Min || b.Max < a.Min;
					if (disjoint || HasUniqueRequired(a.Named, b.Named) || HasUniqueRequired(b.Named, a.Named)) continue;
					var loc = args.FirstOrDefault(x => x.Group == b.Name)?.Location;
					diags.Add(DiagInfo.Create(Descriptors.AmbiguousGroups.Id, loc, a.Name, b.Name));
				}
			}
			return result;
		}

		/// <summary>True when <paramref name="a"/> has a required switch that <paramref name="b"/> doesn't recognize in any spelling.</summary>
		static bool HasUniqueRequired(List<ArgBuilder> a, List<ArgBuilder> b)
		{
			foreach (var r in a)
			{
				if (!r.Required) continue;
				bool longKnown = b.Any(x => x.LongName == r.LongName);
				bool shortKnown = r.ShortName != '\0' && b.Any(x => x.ShortName == r.ShortName);
				if (!longKnown && !shortKnown) return true;
			}
			return false;
		}

		static bool IsValidLongName(string name)
		{
			if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0])) return false;
			foreach (var c in name)
			{
				if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
			}
			return true;
		}

		/// <summary>OutputFile → output-file, IOPath → io-path, _count → count.</summary>
		public static string Kebab(string name)
		{
			name = name.TrimStart('_');
			var sb = new StringBuilder();
			for (int i = 0; i < name.Length; ++i)
			{
				char c = name[i];
				if (char.IsUpper(c))
				{
					if (i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])
						|| (char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]))))
					{
						sb.Append('-');
					}
					sb.Append(char.ToLowerInvariant(c));
				}
				else if (c == '_')
				{
					if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
				}
				else
				{
					sb.Append(c);
				}
			}
			return sb.ToString();
		}
	}
}
