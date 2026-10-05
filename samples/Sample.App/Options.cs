using Cli;

namespace Sample.App;

public enum CaseMode
{
	Keep,
	Upper,
	Lower
}

/// <summary>
/// Two usage forms:
///   sample &lt;input&gt; [&lt;output&gt;] [--case &lt;mode&gt;] ...     (group "copy")
///   sample --list-modes                                  (group "list")
/// Ungrouped switches (--verbose, --tag, --timeout) are accepted by both.
/// </summary>
[CliArgs(Description = "Copies a text file, optionally changing its case.")]
internal partial class Options
{
	[CmdArg(0, Group = "copy", ValueName = "input", Description = "The file to read, or - for standard input.")]
	public TextReader Input = Console.In;

	[CmdArg(1, Group = "copy", Required = false, ValueName = "output", Description = "The file to write, or - for standard output.")]
	public TextWriter Output = Console.Out;

	[CmdArg(Group = "copy", ShortName = 'c', ValueName = "mode", Description = "How to change the case of the text: keep, upper or lower.")]
	public CaseMode Case = CaseMode.Keep;

	[CmdArg(Group = "copy", ShortName = 'f', Description = "Copy even when the output file is newer than the input.")]
	public bool Force;

	[CmdArg(Group = "list", Required = true, Description = "List the case modes and exit.")]
	public bool ListModes;

	[CmdArg(ShortName = 'v', Description = "Write progress to standard error.")]
	public bool Verbose;

	[CmdArg(ShortName = 't', ValueName = "tag", Description = "Tags to prepend to the output, one line each.")]
	public List<string> Tag { get; } = new();

	[CmdArg(ValueName = "time", Description = "Give up after this long.")]
	public TimeSpan Timeout = TimeSpan.FromSeconds(30);
}
