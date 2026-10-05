using Cli;

namespace Cli.Generator.Tests;

/// <summary>Behaviour of the generated Parse/Create code against the types in TestTypes.cs.</summary>
static class RuntimeTests
{
	[Test]
	public static void Ordinals_switches_and_defaults()
	{
		var o = Basic.Create(["in.txt", "-m", "fast"]);
		Check.Equal("in.txt", o.Input);
		Check.Equal("out.txt", o.Output, "optional ordinal keeps its initializer");
		Check.Equal(1, o.Count);
		Check.Equal(false, o.Verbose);
		Check.Equal("fast", o.Mode);
		Check.Equal(0.5, o.Ratio);
		Check.Equal(null, o.MaxSize);
	}

	[Test]
	public static void Long_short_and_equals_forms()
	{
		var o = Basic.Create(["in", "result", "--count", "7", "--ratio=0.25", "-m=slow", "--max-size", "100", "--output-format", "json"]);
		Check.Equal("result", o.Output);
		Check.Equal(7, o.Count);
		Check.Equal(0.25, o.Ratio, "invariant culture parse");
		Check.Equal("slow", o.Mode);
		Check.Equal(100L, o.MaxSize);
		Check.Equal("json", o.OutputFormat, "default switch name is kebab-case");
	}

	[Test]
	public static void Switches_may_come_before_ordinals()
	{
		var o = Basic.Create(["-v", "--mode", "x", "in", "-n", "2", "out"]);
		Check.Equal("in", o.Input);
		Check.Equal("out", o.Output);
		Check.Equal(2, o.Count);
		Check.Equal(true, o.Verbose);
	}

	[Test]
	public static void Bundled_short_flags()
	{
		var o = Basic.Create(["in", "-vqm", "x"]);
		Check.Equal(true, o.Verbose);
		Check.Equal(true, o.Quiet);
		Check.Equal("x", o.Mode, "value-taking switch may end a bundle");

		var o2 = Basic.Create(["in", "-vn=5", "-m", "x"]);
		Check.Equal(5, o2.Count);

		var ex = Check.Throws<CmdException>(() => Basic.Create(["in", "-nv", "5", "-m", "x"]));
		Check.Contains("can only be the last letter", ex.Message);
	}

	[Test]
	public static void Flag_with_explicit_value()
	{
		var o = Basic.Create(["in", "-m", "x", "--verbose=false", "-q=yes"]);
		Check.Equal(false, o.Verbose);
		Check.Equal(true, o.Quiet);
	}

	[Test]
	public static void Negative_numbers_are_values()
	{
		var o = Basic.Create(["-5", "-m", "x", "-n", "-3", "--ratio", "-1.5"]);
		Check.Equal("-5", o.Input);
		Check.Equal(-3, o.Count);
		Check.Equal(-1.5, o.Ratio);
	}

	[Test]
	public static void Parse_strips_exe_and_handles_quotes()
	{
		var o = Basic.Parse("\"C:\\Program Files\\app.exe\" \"my file.txt\" -m \"two words\" --output-format=\"a b\"");
		Check.Equal("my file.txt", o.Input);
		Check.Equal("two words", o.Mode);
		Check.Equal("a b", o.OutputFormat, "quotes inside a --name=value token");

		var o2 = Basic.Parse("app say\\\"hi\\\" -m \"x\"\"y\"");
		Check.Equal("say\"hi\"", o2.Input, "backslash-escaped quotes");
		Check.Equal("x\"y", o2.Mode, "doubled quote inside quotes");

		var o3 = Basic.Parse("app.exe \"--not-a-switch\" -m x");
		Check.Equal("--not-a-switch", o3.Input, "a quoted token is always a value");

		var o4 = Basic.Parse("app.exe C:\\dir\\ -m x");
		Check.Equal("C:\\dir\\", o4.Input, "backslashes not before a quote are literal");
	}

	[Test]
	public static void Parse_null_uses_environment_command_line()
	{
		// This test runner was started without the required arguments, so the environment's command line fails to bind.
		// What matters is that Parse(null) reads it at all.
		var ok = Basic.TryParse(null!, out _, out var error);
		Check.True(!ok && error != null, "expected a CmdException from the real command line");
	}

	[Test]
	public static void Required_and_structural_errors()
	{
		var e1 = Check.Throws<CmdException>(() => Basic.Create(["-m", "x"]));
		Check.Equal("<input>", e1.ArgumentName);
		Check.Contains("Missing required argument <input>", e1.Message);

		var e2 = Check.Throws<CmdException>(() => Basic.Create(["in"]));
		Check.Equal("--mode", e2.ArgumentName);

		var e3 = Check.Throws<CmdException>(() => Basic.Create(["in", "-m", "x", "--bogus"]));
		Check.Contains("Unknown switch --bogus", e3.Message);

		var e4 = Check.Throws<CmdException>(() => Basic.Create(["in", "-m", "x", "-n", "1", "--count", "2"]));
		Check.Contains("more than once", e4.Message);

		var e5 = Check.Throws<CmdException>(() => Basic.Create(["in", "out", "extra", "-m", "x"]));
		Check.Contains("Unexpected argument \"extra\"", e5.Message);

		var e6 = Check.Throws<CmdException>(() => Basic.Create(["in", "-m"]));
		Check.Contains("--mode needs a value", e6.Message);
	}

	[Test]
	public static void Conversion_errors_name_the_argument()
	{
		var ex = Check.Throws<CmdException>(() => Basic.Create(["in", "-m", "x", "-n", "lots"]));
		Check.Equal("--count", ex.ArgumentName);
		Check.Contains("Invalid value \"lots\" for --count", ex.Message);
		Check.True(ex.InnerException is FormatException, "inner exception is kept");

		Check.Equal(false, Basic.TryCreate(["in", "-m", "x", "-n", "lots"], out _, out var err));
		Check.Equal("--count", err.ArgumentName);
	}

	[Test]
	public static void Lists_and_collections()
	{
		var o = Lists.Create(["run", "a", "b", "-i", "1", "2", "--ids=3", "--tags", "x", "x", "y",
			"--guids", "00000000-0000-0000-0000-000000000001", "--existing", "new", "--bits", "true", "0"]);
		Check.Equal("run", o.Command);
		Check.Seq(["a", "b"], o.Files, "ordinal list");
		Check.Seq([1, 2, 3], o.Ids, "repeated list switch accumulates");
		Check.Seq(["x", "y"], o.Tags.OrderBy(t => t), "HashSet");
		Check.Equal(1, o.Guids.Count);
		Check.Seq(["new"], o.Existing, "get-only collection is cleared and refilled");
		Check.Seq([true, false], o.Bits);
	}

	[Test]
	public static void Ordinal_list_continues_after_a_switch()
	{
		var o = Lists.Create(["run", "a", "-i", "1", "--tags", "t", "--existing", "e"]);
		Check.Seq(["a"], o.Files);
		Check.Seq([1], o.Ids);
		// A named list is greedy: bare values after it belong to it, even after --name=value.
		var o2 = Lists.Create(["run", "a", "--tags=t", "b"]);
		Check.Seq(["a"], o2.Files);
		Check.Seq(["b", "t"], o2.Tags.OrderBy(t => t));
	}

	[Test]
	public static void Optional_ordinal_list_may_be_empty()
	{
		var o = Lists.Create(["run"]);
		Check.Equal(0, o.Files.Length);
		Check.Seq(["seed"], o.Existing, "untouched collection keeps its initializer");
	}

	[Test]
	public static void Conversion_routes()
	{
		var tmp = Path.Combine(Path.GetTempPath(), "cli-gen-test-" + Guid.NewGuid().ToString("N") + ".txt");
		File.WriteAllText(tmp, "content");
		try
		{
			var c = Conversions.Create([
				"--shade", "DARK",
				"--id", "6f9619ff-8b86-d011-b42d-00cf4fc964ff",
				"--wait", "00:01:30",
				"--url", "https://example.com/x",
				"--file", tmp,
				"--dir", ".",
				"--when", "2024-02-29T10:00:00",
				"--at", "3,4",
				"--temp", "21.5C",
				"--named", "teal",
				"--shout", "hey",
				"--letter", "z",
				"--money", "12.34",
				"--ver", "1.2.3",
				"--maybe", "42",
				"--in", tmp,
				"--out", "-",
				"--path", "1,2", "3,4"]);
			Check.Equal(Shade.Dark, c.Shade, "enum, case-insensitive");
			Check.Equal(new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff"), c.Id);
			Check.Equal(TimeSpan.FromSeconds(90), c.Wait);
			Check.Equal("https://example.com/x", c.Url!.ToString());
			Check.Equal(tmp, c.File!.FullName);
			Check.Equal(new DateTime(2024, 2, 29, 10, 0, 0), c.When);
			Check.Equal(new Point(3, 4), c.At, "static Parse(string)");
			Check.Equal(21.5, c.Temp!.Degrees, "constructor(string)");
			Check.Equal("color:teal", c.Named!.Value, "[TypeConverter] on the type");
			Check.Equal("HEY", c.Shout, "explicit Converter");
			Check.Equal('z', c.Letter);
			Check.Equal(12.34m, c.Money);
			Check.Equal(new Version(1, 2, 3), c.Ver);
			Check.Equal(42, c.Maybe, "Nullable<int>");
			Check.Equal("content", c.In!.ReadToEnd());
			c.In.Dispose();
			Check.True(ReferenceEquals(c.Out, Console.Out), "'-' means standard output");
			Check.Seq([new Point(1, 2), new Point(3, 4)], c.Path, "array of a Parse type");
		}
		finally
		{
			File.Delete(tmp);
		}
	}

	[Test]
	public static void Output_file_is_created_lazily()
	{
		var tmp = Path.Combine(Path.GetTempPath(), "cli-gen-lazy-" + Guid.NewGuid().ToString("N") + ".txt");
		var c = Conversions.Create(["--out", tmp]);
		Check.True(!File.Exists(tmp), "not created until written");
		c.Out!.WriteLine("hi");
		c.Out.Dispose();
		Check.Equal("hi" + Environment.NewLine, File.ReadAllText(tmp));
		File.Delete(tmp);
	}

	[Test]
	public static void Struct_record_and_nested_types()
	{
		var r = Rec.Create(["bob", "-a", "40"]);
		Check.Equal("bob", r.Name);
		Check.Equal(40, r.Age, "private setter is fine, the generated code is inside the type");
		var n = Outer.Inner.Create(["-x"]);
		Check.Equal(true, n.X);
	}

	[Test]
	public static void Help_requests()
	{
		foreach (var h in new[] { "--help", "-h", "-?", "/?" })
		{
			var ex = Check.Throws<CmdException>(() => Basic.Create(["in", h]));
			Check.True(ex.IsHelpRequest, h + " is a help request");
		}
		Check.Equal(false, Basic.TryCreate(["--help"], out _, out var err));
		Check.True(err.IsHelpRequest, "TryCreate reports help through the error");

		// A type that defines -h itself gets its own switch, but --help still means help.
		Check.Equal(true, HelpOwner.Create(["-h"]).ShowHelp);
		Check.True(Check.Throws<CmdException>(() => HelpOwner.Create(["--help"])).IsHelpRequest, "--help still reserved");
	}

	[Test]
	public static void Groups_are_detected()
	{
		var add = Grouped.Create(["milk", "-p", "1", "-v"]);
		Check.Equal("add", add.ArgumentGroup);
		Check.Equal("milk", add.Item);
		Check.Equal(1, add.Priority);
		Check.Equal(true, add.Verbose, "shared switch");

		var rem = Grouped.Create(["-r", "milk", "--force"]);
		Check.Equal("remove", rem.ArgumentGroup);
		Check.Equal("milk", rem.Remove);
		Check.Equal(true, rem.Force);
		Check.Equal(3, rem.Priority, "other groups' members are left alone");

		var list = Grouped.Create(["--list", "-v"]);
		Check.Equal("list", list.ArgumentGroup);
	}

	[Test]
	public static void Group_errors_pick_the_closest_form()
	{
		var e1 = Check.Throws<CmdException>(() => Grouped.Create(["-r", "milk", "eggs"]));
		Check.Equal("remove", e1.Group);
		Check.Contains("Unexpected argument \"eggs\"", e1.Message);

		// Mixing forms fails. Which form the message describes is a heuristic (fewest unknown switches).
		Check.Throws<CmdException>(() => Grouped.Create(["milk", "--force"]));

		var e3 = Check.Throws<CmdException>(() => Grouped.Create([]));
		Check.True(e3.Message.Length > 0, "no-argument input still gets an error");
	}

	[Test]
	public static void Usage_text()
	{
		var u = Basic.GetUsage(100);
		Check.Contains("Basic test options.", u);
		Check.Contains("Usage: basic <input> [<output>] [--count <count>]", u);
		Check.Contains("--mode <mode>", u);
		Check.Contains("-n, --count <count>", u);
		Check.Contains("How many. Defaults to 1.", u);
		Check.Contains("The output. Defaults to out.txt.", u);
		Check.Contains("    --max-size <max-size>", u);

		var g = Grouped.GetUsage(100);
		Check.Contains("todo <item> [--priority <priority>] [--verbose]", g);
		Check.Contains("todo --remove <item> [--force] [--verbose]", g);
		Check.Contains("todo --list [--verbose]", g);

		var sw = new StringWriter();
		Basic.PrintUsage(sw, 100);
		Check.Equal(u, sw.ToString());
	}

	[Test]
	public static void Usage_wraps_to_width()
	{
		var u = Basic.GetUsage(50);
		foreach (var line in u.Split('\n'))
		{
			Check.True(line.TrimEnd('\r').Length <= 50, "line too long: " + line);
		}
	}

	[Test]
	public static void Dispose_disposes_arguments_but_not_console_streams()
	{
		var tmp = Path.GetTempFileName();
		try
		{
			Disposables d;
			Tracker one, a, b, k, initial;
			using (d = Disposables.Create(["--in", tmp, "--one", "x", "--many", "a", "b", "--kept", "k", "--count", "2"]))
			{
				one = d.One!;
				a = d.Many[0];
				b = d.Many[1];
				k = d.Kept[0];
				initial = d.Default;
				Check.True(d.In is StreamReader, "file reader opened");
			}
			Check.Equal(null, d.In, "disposed reader is nulled");
			Check.True(ReferenceEquals(d.Out, Console.Out), "Console.Out is left alone");
			Check.Equal(1, one.DisposeCount, "scalar");
			Check.Equal(1, a.DisposeCount, "array item a");
			Check.Equal(1, b.DisposeCount, "array item b");
			Check.Equal(null, d.Many, "disposed array is nulled");
			Check.Equal(1, k.DisposeCount, "get-only collection items");
			Check.Equal(0, d.Kept.Count, "get-only collection is emptied");
			Check.Equal(1, initial.DisposeCount, "an initializer default is disposed too, as in the old CliUtility");
			Check.Equal(2, d.Count);

			d.Dispose();
			Check.Equal(1, one.DisposeCount, "second Dispose does nothing");
			Console.Out.Write("");
		}
		finally
		{
			File.Delete(tmp);
		}
	}
}
