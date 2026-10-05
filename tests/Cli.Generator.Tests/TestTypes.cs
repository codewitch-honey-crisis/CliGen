using System.ComponentModel;
using System.Globalization;
using Cli;

namespace Cli.Generator.Tests;

// ---- Value types with different conversion routes -------------------------------------------

/// <summary>Converted through its static Parse(string).</summary>
public readonly record struct Point(int X, int Y)
{
	public static Point Parse(string s)
	{
		var parts = s.Split(',');
		return new Point(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
	}
}

/// <summary>Converted through its string constructor.</summary>
public sealed class Celsius
{
	public double Degrees { get; }
	public Celsius(string s) => Degrees = double.Parse(s.TrimEnd('C', 'c'), CultureInfo.InvariantCulture);
}

/// <summary>Converted through the [TypeConverter] on the type.</summary>
[TypeConverter(typeof(ColorNameConverter))]
public sealed class ColorName
{
	public string Value { get; init; } = "";
}

public sealed class ColorNameConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);
	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
		=> new ColorName { Value = "color:" + (string)value };
}

/// <summary>Used explicitly through [CmdArg(Converter = ...)].</summary>
public sealed class UpperConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);
	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
		=> ((string)value).ToUpperInvariant();
}

public enum Shade
{
	Light,
	Dark
}

// ---- Argument specifications -------------------------------------------------------------------

[CliArgs(ExeName = "basic", Description = "Basic test options.")]
partial class Basic
{
	[CmdArg(0, Description = "The input.")]
	public string Input = "";

	[CmdArg(1, Required = false, Description = "The output.")]
	public string? Output = "out.txt";

	[CmdArg(ShortName = 'n', Description = "How many.")]
	public int Count = 1;

	[CmdArg(ShortName = 'v')]
	public bool Verbose;

	[CmdArg(ShortName = 'q')]
	public bool Quiet;

	[CmdArg]
	public double Ratio = 0.5;

	[CmdArg("max-size")]
	public long? MaxSize;

	[CmdArg]
	public string OutputFormat = "text";

	[CmdArg(Required = true, ShortName = 'm')]
	public string Mode = "";
}

[CliArgs]
partial class Lists
{
	[CmdArg(0)]
	public string Command = "";

	[CmdArg(1, Required = false)]
	public string[] Files = [];

	[CmdArg(ShortName = 'i')]
	public List<int> Ids = new();

	[CmdArg]
	public HashSet<string> Tags = new();

	[CmdArg]
	public IReadOnlyList<Guid> Guids = [];

	[CmdArg]
	public List<string> Existing { get; } = new() { "seed" };

	[CmdArg]
	public bool[] Bits = [];
}

// A struct holding readers and writers gets Dispose() plus warning CLI006, which is suppressed here on purpose.
#pragma warning disable CLI006
[CliArgs]
partial struct Conversions
{
	[CmdArg] public Shade Shade;
	[CmdArg] public Guid Id;
	[CmdArg] public TimeSpan Wait;
	[CmdArg] public Uri? Url;
	[CmdArg] public FileInfo? File;
	[CmdArg] public DirectoryInfo? Dir;
	[CmdArg] public DateTime When;
	[CmdArg] public Point At;
	[CmdArg] public Celsius? Temp;
	[CmdArg] public ColorName? Named;
	[CmdArg(Converter = typeof(UpperConverter))] public string? Shout;
	[CmdArg] public char Letter;
	[CmdArg] public decimal Money;
	[CmdArg] public Version? Ver;
	[CmdArg] public int? Maybe;
	[CmdArg] public TextReader? In;
	[CmdArg] public TextWriter? Out;
	[CmdArg] public Point[]? Path;
}
#pragma warning restore CLI006

/// <summary>Disposable, converted through its string constructor; records whether it was disposed.</summary>
public sealed class Tracker(string name) : IDisposable
{
	public string Name { get; } = name;
	public int DisposeCount { get; private set; }
	public void Dispose() => ++DisposeCount;
}

[CliArgs]
partial class Disposables
{
	[CmdArg] public TextReader? In;
	[CmdArg] public TextWriter Out = Console.Out;
	[CmdArg] public Tracker? One;
	[CmdArg] public Tracker[] Many = [];
	[CmdArg] public List<Tracker> Kept { get; } = new();
	[CmdArg] public Tracker Default = new("initial");
	[CmdArg] public int Count;
}

[CliArgs]
partial record Rec
{
	[CmdArg(0)]
	public string Name { get; set; } = "";

	[CmdArg(ShortName = 'a')]
	public int Age { get; private set; }
}

partial class Outer
{
	[CliArgs]
	internal partial struct Inner
	{
		[CmdArg(ShortName = 'x')]
		public bool X;
	}
}

[CliArgs]
partial class HelpOwner
{
	[CmdArg(ShortName = 'h')]
	public bool ShowHelp;
}

[CliArgs(ExeName = "todo")]
partial class Grouped
{
	[CmdArg(0, Group = "add", ValueName = "item")]
	public string Item = "";

	[CmdArg(Group = "add", ShortName = 'p')]
	public int Priority = 3;

	[CmdArg(Group = "remove", Required = true, ShortName = 'r', ValueName = "item")]
	public string Remove = "";

	[CmdArg(Group = "remove")]
	public bool Force;

	[CmdArg(Group = "list", Required = true)]
	public bool List;

	[CmdArg(ShortName = 'v')]
	public bool Verbose;
}
