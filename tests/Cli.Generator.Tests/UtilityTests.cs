namespace Cli.Generator.Tests;

/// <summary>The public helpers on the generated CliUtility, and the per-type usage parts.</summary>
static class UtilityTests
{
	[Test]
	public static void WordWrap_wraps_indents_and_keeps_nonbreaking_runs()
	{
		var text = CliUtility.WordWrap("one two three four five six", width: 12, indent: 2);
		Check.Equal("one two" + Environment.NewLine + "  three four" + Environment.NewLine + "  five six", text);

		var nb = CliUtility.WordWrap("aa bb\u00A0cc\u00A0dd", width: 6);
		Check.Equal("aa" + Environment.NewLine + "bb cc dd", nb);

		var offset = CliUtility.WordWrap("alpha beta", width: 12, indent: 0, startOffset: 6);
		Check.Equal("alpha" + Environment.NewLine + "beta", offset);

		Check.Throws<ArgumentOutOfRangeException>(() => CliUtility.WordWrap("x", width: -1));
	}

	[Test]
	public static void Assembly_and_environment_info()
	{
		Check.True(!string.IsNullOrEmpty(CliUtility.AssemblyTitle), "AssemblyTitle");
		Check.True(CliUtility.AssemblyVersion != null, "AssemblyVersion");
		Check.Equal("Cli.Generator.Tests", CliUtility.ExeName);
		Check.Equal("C:\\Program Files\\x.exe", CliUtility.ParseExePath("\"C:\\Program Files\\x.exe\" a b"));
		Check.Equal("/usr/bin/x", CliUtility.ParseExePath("  /usr/bin/x --flag"));
		Check.Equal(OperatingSystem.IsWindows(), CliUtility.IsWindows);
		Check.True(CliUtility.ConsoleWidth > 20, "ConsoleWidth");
		Check.Equal("--", CliUtility.SwitchPrefix);
	}

	[Test]
	public static void IsStale_and_GetFilename()
	{
		var dir = Directory.CreateTempSubdirectory("cli-gen-stale").FullName;
		try
		{
			var input = Path.Combine(dir, "in.txt");
			var output = Path.Combine(dir, "out.txt");
			File.WriteAllText(input, "x");
			Check.True(CliUtility.IsStale(input, output), "missing output is stale");
			File.WriteAllText(output, "y");
			File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(-5));
			File.SetLastWriteTimeUtc(output, DateTime.UtcNow);
			Check.True(!CliUtility.IsStale(input, output), "newer output is fresh");
			File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(5));
			Check.True(CliUtility.IsStale(new FileInfo(input), new FileInfo(output)), "older output is stale");
			Check.True(CliUtility.IsStale(Array.Empty<FileSystemInfo>(), new FileInfo(output)), "no inputs counts as stale");

			// Readers and writers produced by the generated parser keep their file names.
			var c = Conversions.Create(["--in", input, "--out", output]);
			Check.Equal(Path.GetFullPath(input), CliUtility.GetFilename(c.In!));
			Check.Equal(output, CliUtility.GetFilename(c.Out!));
			Check.True(CliUtility.IsStale(c.In!, c.Out!), "IsStale(TextReader, TextWriter)");
			c.In!.Dispose();
			c.Out!.Dispose();
			Check.Equal(null, CliUtility.GetFilename(Console.Out));
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	[Test]
	public static void Progress_output()
	{
		var w = new StringWriter();
		CliUtility.WriteProgressBar(40, update: false, writer: w);
		Check.Equal("[■■■■      ]  40%", w.ToString());
		w.GetStringBuilder().Clear();
		CliUtility.WriteProgressBar(100, update: true, writer: w);
		Check.True(w.ToString().StartsWith(new string('\b', 17) + "[■■■■■■■■■■] 100%"), "update backs up first: " + w);

		var s = new StringWriter();
		for (int i = 0; i < 5; ++i) CliUtility.WriteProgress(i, update: i > 0, writer: s);
		Check.Equal("-\b\\\b|\b/\b-", s.ToString());
	}

	[Test]
	public static void Usage_parts()
	{
		Check.Equal("<input> [<output>] [--count <count>]", Basic.GetUsageArguments(width: 1000).Substring(0, 36));
		var wrapped = Basic.GetUsageArguments(width: 40, startOffset: 10);
		Check.True(wrapped.Split('\n')[0].Length <= 30, "first line respects startOffset");

		Check.Equal("--remove <item> [--force] [--verbose]", Grouped.GetUsageArguments(width: 1000, group: "remove"));
		Check.Equal("<item> [--priority <priority>] [--verbose]", Grouped.GetUsageArguments(width: 1000), "null group is the first");
		Check.Contains("--list", Grouped.GetUsageDescriptions(width: 100, group: "list"));
		Check.Throws<ArgumentException>(() => Grouped.GetUsageArguments(group: "nope"));

		var rows = Basic.GetUsageDescriptions(width: 100);
		Check.Contains("-n, --count <count>", rows);
		Check.Contains("How many. Defaults to 1.", rows);
	}
}
