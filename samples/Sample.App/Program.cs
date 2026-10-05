using Sample.App;

// Create() takes args already split by the runtime. Options.Parse() would instead read Environment.CommandLine
// (or a string you pass it) and strip the executable name itself.
if (!Options.TryCreate(args, out var opts, out var error))
{
	if (!error!.IsHelpRequest)
	{
		Console.Error.WriteLine("Error: " + error.Message);
		Console.Error.WriteLine();
	}
	Options.PrintUsage();
	return error.IsHelpRequest ? 0 : 1;
}

// Options holds a TextReader and a TextWriter, so the generator gave it a Dispose() that closes them
// (but never Console.In/Out). Disposing it also flushes the output file.
using (opts)
{
	if (opts.ArgumentGroup == "list")
	{
		foreach (var mode in Enum.GetNames<CaseMode>())
		{
			Console.WriteLine(mode.ToLowerInvariant());
		}
		return 0;
	}

	// CliUtility is generated next to Options (same namespace) along with CmdException.
	if (!opts.Force && !CliUtility.IsStale(opts.Input, opts.Output))
	{
		Console.Error.WriteLine(CliUtility.GetFilename(opts.Output) + " is up to date. Use --force to copy anyway.");
		return 0;
	}

	if (opts.Verbose)
	{
		Console.Error.WriteLine(CliUtility.WordWrap(
			$"{CliUtility.AssemblyTitle} copying {CliUtility.GetFilename(opts.Input) ?? "<stdin>"} to {CliUtility.GetFilename(opts.Output) ?? "<stdout>"} with case={opts.Case}, timeout={opts.Timeout}, tags=[{string.Join(", ", opts.Tag)}]",
			indent: 4));
	}
	foreach (var tag in opts.Tag)
	{
		opts.Output.WriteLine("# " + tag);
	}
	string? line;
	while ((line = opts.Input.ReadLine()) != null)
	{
		opts.Output.WriteLine(opts.Case switch
		{
			CaseMode.Upper => line.ToUpperInvariant(),
			CaseMode.Lower => line.ToLowerInvariant(),
			_ => line
		});
	}
}
return 0;
