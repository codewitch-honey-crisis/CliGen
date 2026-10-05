using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Cli.Generator
{
	internal static class Descriptors
	{
		const string Category = "Cli.Generator";

		static readonly Dictionary<string, DiagnosticDescriptor> _all = new Dictionary<string, DiagnosticDescriptor>();

		static DiagnosticDescriptor Add(string id, string title, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error)
		{
			var d = new DiagnosticDescriptor(id, title, message, Category, severity, true);
			_all.Add(id, d);
			return d;
		}

		// Type level
		public static readonly DiagnosticDescriptor NotPartial = Add("CLI001", "Type must be partial", "'{0}' must be declared partial so the generator can add members to it");
		public static readonly DiagnosticDescriptor Generic = Add("CLI002", "Generic types not supported", "'{0}' is generic; [CliArgs] types can't have type parameters");
		public static readonly DiagnosticDescriptor NotInstantiable = Add("CLI003", "Type must be instantiable", "'{0}' must be a non-static, non-abstract class or struct");
		public static readonly DiagnosticDescriptor NoDefaultCtor = Add("CLI004", "Parameterless constructor required", "'{0}' needs a parameterless constructor (it may be private)");
		public static readonly DiagnosticDescriptor DisposableStruct = Add("CLI006", "Dispose generated on a struct", "'{0}' has disposable arguments, so Dispose() is generated, but '{0}' is a struct: every copy shares the same readers and writers, so disposing one copy closes them for all. Consider making '{0}' a class.", DiagnosticSeverity.Warning);
		public static readonly DiagnosticDescriptor NoArgs = Add("CLI005", "No arguments declared", "'{0}' has no members marked [CmdArg]", DiagnosticSeverity.Warning);

		// Member level
		public static readonly DiagnosticDescriptor StaticMember = Add("CLI010", "Static members not supported", "'{0}' is static; [CmdArg] only works on instance fields and properties");
		public static readonly DiagnosticDescriptor NotSettable = Add("CLI011", "Member must be settable", "'{0}' can't be assigned; use a writable field or a property with a set accessor (init-only doesn't work). A get-only property is allowed only for an existing ICollection<T>.");
		public static readonly DiagnosticDescriptor NameAndOrdinal = Add("CLI012", "Both name and ordinal", "'{0}' sets both Name and Ordinal; an argument is either ordinal or named");
		public static readonly DiagnosticDescriptor NoConversion = Add("CLI013", "No conversion from string", "There's no way to convert a string to '{1}' for '{0}'. Add a static Parse(string) method, a constructor taking a string, a [TypeConverter] on the type, or set Converter on [CmdArg].");
		public static readonly DiagnosticDescriptor BadConverter = Add("CLI014", "Invalid converter", "Converter '{1}' for '{0}' must derive from System.ComponentModel.TypeConverter and have a public parameterless constructor");
		public static readonly DiagnosticDescriptor BadLongName = Add("CLI015", "Invalid switch name", "Switch name '{1}' for '{0}' must start with a letter and contain only letters, digits, '-' and '_'");
		public static readonly DiagnosticDescriptor BadShortName = Add("CLI016", "Invalid short name", "Short name '{1}' for '{0}' must be a letter");
		public static readonly DiagnosticDescriptor ShortOnOrdinal = Add("CLI017", "Short name on ordinal", "'{0}' is an ordinal argument, so its Name and ShortName are ignored", DiagnosticSeverity.Warning);

		// Group / layout level
		public static readonly DiagnosticDescriptor UngroupedOrdinal = Add("CLI018", "Ordinal needs a group", "'{0}' is an ordinal argument without a Group, but the type uses groups. Only named arguments can be shared by all groups.");
		public static readonly DiagnosticDescriptor DuplicateOrdinal = Add("CLI019", "Duplicate ordinal", "'{0}' and '{1}' both use ordinal {2}{3}");
		public static readonly DiagnosticDescriptor ListNotLast = Add("CLI020", "Ordinal list must be last", "'{0}' takes multiple values, so it must be the last ordinal argument{1}; '{2}' comes after it and could never receive a value");
		public static readonly DiagnosticDescriptor RequiredAfterOptional = Add("CLI021", "Required ordinal after optional", "Required ordinal '{0}' comes after optional ordinal '{1}'{2}; optional ordinals must come last");
		public static readonly DiagnosticDescriptor DuplicateLongName = Add("CLI022", "Duplicate switch name", "'{0}' and '{1}' both use switch --{2}{3}");
		public static readonly DiagnosticDescriptor DuplicateShortName = Add("CLI023", "Duplicate short name", "'{0}' and '{1}' both use switch -{2}{3}");
		public static readonly DiagnosticDescriptor AmbiguousGroups = Add("CLI024", "Groups can't be told apart", "Groups '{0}' and '{1}' can accept the same command line. Give one a required switch the other doesn't have, or make the number of ordinal arguments they accept differ.");

		public static readonly DiagnosticDescriptor MissingCliArgs = Add("CLI030", "Missing [CliArgs]", "'{0}' is marked [CmdArg] but its type '{1}' isn't marked [CliArgs], so nothing is generated for it", DiagnosticSeverity.Warning);

		public static DiagnosticDescriptor Get(string id) => _all[id];

		public static bool IsError(string id) => _all[id].DefaultSeverity == DiagnosticSeverity.Error;
	}
}
