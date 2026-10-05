# Cli.Generator

Cli.Generator is a C# source generator that turns a class into a command line parser. You mark fields and properties with attributes, and it writes the parsing, validation, usage screen and cleanup code for you at compile time. There's no reflection at runtime, and mistakes in how you've laid out your arguments show up as compile errors rather than surprises in production.

It also gives your project `CliUtility`, a set of helpers for command line programs: word wrapping, assembly information, "is this output file out of date?" checks and progress output.

## Contents

- [Getting started](#getting-started)
- [Declaring arguments](#declaring-arguments)
- [What users can type](#what-users-can-type)
- [Supported types](#supported-types)
- [Lists and collections](#lists-and-collections)
- [Usage forms (groups)](#usage-forms-groups)
- [What gets generated](#what-gets-generated)
- [Errors and help](#errors-and-help)
- [Usage text](#usage-text)
- [Disposal](#disposal)
- [CliUtility reference](#cliutility-reference)
- [Compile-time diagnostics](#compile-time-diagnostics)
- [Visibility and namespaces](#visibility-and-namespaces)
- [Building this repository](#building-this-repository)

## Getting started

**1. Reference the generator.** From a project in the same solution:

```xml
<ItemGroup>
  <ProjectReference Include="..\Cli.Generator\Cli.Generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

Or run `dotnet pack src/Cli.Generator` and add the resulting package with a `PackageReference`.

**2. Describe your arguments.** Make a `partial` class, mark it `[CliArgs]`, and mark each argument `[CmdArg]`:

```csharp
using Cli;

namespace MyTool;

[CliArgs(Description = "Copies a text file, optionally changing its case.")]
partial class Options
{
    [CmdArg(0, Description = "The file to read, or - for standard input.")]
    public TextReader Input = Console.In;

    [CmdArg(1, Required = false, Description = "The file to write, or - for standard output.")]
    public TextWriter Output = Console.Out;

    [CmdArg(ShortName = 'c', ValueName = "mode", Description = "keep, upper or lower.")]
    public CaseMode Case = CaseMode.Keep;

    [CmdArg(ShortName = 'v', Description = "Write progress to standard error.")]
    public bool Verbose;
}
```

**3. Parse.**

```csharp
using MyTool;

if (!Options.TryCreate(args, out var opts, out var error))
{
    if (!error.IsHelpRequest) Console.Error.WriteLine("Error: " + error.Message);
    Options.PrintUsage();
    return error.IsHelpRequest ? 0 : 1;
}

using (opts)   // closes the input and output files when you're done
{
    // use opts.Input, opts.Output, opts.Case, opts.Verbose
}
return 0;
```

That program now accepts `mytool in.txt out.txt -vc upper`, prints a usage screen for `--help`, and reports `Error: Missing required argument <input>.` when it's run with nothing.

The values you give fields and properties in their initializers are the defaults. Anything the user doesn't supply keeps its initial value.

## Declaring arguments

### `[CliArgs]` on the type

The type must be `partial` (and so must any type it's nested in), non-generic, not abstract, and have a parameterless constructor, which may be private. Classes, records, structs and nested types all work. Prefer a class if any argument is disposable; see [Disposal](#disposal).

| Property | Meaning |
|---|---|
| `ExeName` | The program name shown in usage. Defaults to the running executable's file name. |
| `Description` | Shown at the top of the usage screen. Defaults to the assembly description (`<Description>` in your .csproj). |

### `[CmdArg]` on fields and properties

Put it on instance fields or on properties with a setter; a private setter is fine. There are two kinds of argument:

- **Ordinal (positional)** arguments are identified by position: `[CmdArg(0)]`, `[CmdArg(1)]`, and so on. The numbers only set the order, so gaps are fine.
- **Named** arguments are switches: `[CmdArg]` uses the member name, and `[CmdArg("name")]` sets it explicitly.

| Property | Meaning |
|---|---|
| `Ordinal` | The position, for an ordinal argument. Same as `[CmdArg(n)]`. |
| `Name` | The long switch name, used as `--name`. Defaults to the member name in kebab-case: `OutputFile` becomes `--output-file` and `IOPath` becomes `--io-path`. |
| `ShortName` | A one-letter abbreviation, used as `-n`. Must be a letter. |
| `Required` | Defaults to **true for ordinals** and **false for switches**. |
| `ValueName` | The placeholder shown in usage, so `ValueName = "file"` shows `<file>`. Defaults to the kebab-cased member name. |
| `Description` | Shown in the usage table. |
| `Group` | Which usage form this belongs to. See [Usage forms](#usage-forms-groups). |
| `Converter` | A `TypeConverter` type to convert each value with. See [Supported types](#supported-types). |

A `bool` (or `bool?`) switch is a **flag**: it takes no value, and being present sets it to `true`.

### Rules the generator enforces

Some layouts can't be parsed unambiguously, so they're compile errors:

- Only the last ordinal can be a list. In `[CmdArg(0)] string[] Files; [CmdArg(1)] string Out;`, `Out` could never receive a value.
- Optional ordinals must come after the required ones.
- Two arguments can't share a switch name, short name or ordinal within the same usage form.
- An argument is either ordinal or named, never both.

The full list is under [Compile-time diagnostics](#compile-time-diagnostics).

## What users can type

| Form | Example |
|---|---|
| Long switch, value separate or attached | `--count 3`, `--count=3` |
| Short switch, value separate or attached | `-n 3`, `-n=3` |
| Flags | `--verbose`, or `--verbose=false` (also yes/no, on/off, 1/0) |
| Bundled short flags | `-vq` is the same as `-v -q` |
| Bundle ending in a switch that takes a value | `-vc upper`, `-vc=upper` |
| Ordinals and switches in any order | `mytool -v in.txt -c upper out.txt` |
| Lists | `--tag a b c`, or `--tag a --tag b` |
| Help | `--help`, `-h`, `-?`, `/?` |

Some details worth knowing:

- In a bundle like `-vqc`, every letter except the last must be a flag. `-cv upper` is an error because `-c` needs a value.
- Negative numbers are values, not switches, because only `-` followed by a letter starts a switch: `--offset -5` works. A lone `-` (often meaning "standard input") and a lone `--` are values too.
- Lists are greedy. A list switch takes every following plain value until the next switch.
- `--help` and `-h` count as help only if your type doesn't define a switch with that name itself.

### `Parse` versus `Create`

- `Create(args)` takes arguments that are already split, such as the `args` passed to `Main`. Use this normally.
- `Parse(commandLine)` takes one string containing the whole command line, starting with the program name, which it removes. With no argument it reads `Environment.CommandLine`.

`Parse` splits the string the way Windows does: spaces separate arguments, double quotes group them, and `""` or `\"` inside quotes is a literal quote. An argument that starts with a quote is always a value, so `"--not-a-switch"` reaches your program as text. On Linux and macOS, .NET rebuilds `Environment.CommandLine` with the same quoting, so `Parse()` works there too.

## Supported types

Every value arrives as a string. For each argument's type, the generator picks a conversion at compile time, in this order:

1. The `Converter` you set on `[CmdArg]`, if any.
2. `string` (as is), `object` (as a string) and `bool` (true/false, yes/no, on/off, 1/0).
3. `TextReader` opens the named file for reading, and `-` means standard input. `TextWriter` returns a writer that creates the file on its first write, and `-` means standard output. Because of the deferred creation, naming an output file you never write to doesn't wipe it.
4. Enums, matched by name, ignoring case.
5. `Nullable<T>` of any type in this list.
6. A public static `Parse(string, IFormatProvider)`, called with the invariant culture. This covers `int`, `double`, `decimal`, `DateTime`, `TimeSpan`, `Guid` and anything implementing `IParsable<T>`.
7. A public static `Parse(string)`, which includes your own types.
8. A `[TypeConverter]` attribute on the type.
9. A public constructor that takes a single `string`, such as `Uri`, `FileInfo`, `DirectoryInfo` or your own types.

If none of these apply, you get a compile error (CLI013) naming the member and type.

Using the invariant culture means `--ratio 0.5` works on a machine set to a culture that uses commas for decimals.

Your own type can be converted just by giving it a `Parse` method:

```csharp
public readonly record struct Point(int X, int Y)
{
    public static Point Parse(string s)
    {
        var p = s.Split(',');
        return new Point(int.Parse(p[0]), int.Parse(p[1]));
    }
}

[CmdArg] public Point Origin;          // --origin 3,4
[CmdArg] public Point[] Path = [];     // --path 1,2 3,4 5,6
```

## Lists and collections

An argument takes several values when its type is an array, `List<T>`, `HashSet<T>`, or any other class that implements `ICollection<T>` and has a parameterless constructor. `IEnumerable<T>`, `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>` and `IReadOnlyCollection<T>` are filled with a new `List<T>`, and `ISet<T>` with a new `HashSet<T>`.

A **get-only** collection property is cleared and refilled in place:

```csharp
[CmdArg(ShortName = 't')]
public List<string> Tag { get; } = new();   // -t a b -t c  gives  [a, b, c]
```

An ordinal list must be the last ordinal: `[CmdArg(0)] string Command; [CmdArg(1, Required = false)] string[] Files;`.

## Usage forms (groups)

A program often has several distinct ways to be called:

```
todo <item> [--priority <n>]
todo --remove <item> [--force]
todo --list
```

Give each argument a `Group` to say which form it belongs to:

```csharp
[CliArgs(ExeName = "todo")]
partial class TodoArgs
{
    [CmdArg(0, Group = "add")]                                       public string Item = "";
    [CmdArg(Group = "add", ShortName = 'p')]                         public int Priority = 3;
    [CmdArg(Group = "remove", Required = true, ValueName = "item")]  public string Remove = "";
    [CmdArg(Group = "remove")]                                       public bool Force;
    [CmdArg(Group = "list", Required = true)]                        public bool List;
    [CmdArg(ShortName = 'v')]                                        public bool Verbose;   // no group: shared by all forms
}
```

- Switches with no group are **shared by every form**. Ordinals must have a group once you use groups.
- The same switch name can mean different things in different forms.
- After parsing, `ArgumentGroup` tells you which form matched:

```csharp
switch (opts.ArgumentGroup)
{
    case "add":    /* ... */ break;
    case "remove": /* ... */ break;
    case "list":   /* ... */ break;
}
```

**How a form is chosen.** Each form is tried against the command line, checking structure only. No values are converted at this stage, so no files are opened for forms that don't win. Exactly one form must fit. If none does, the error message describes the closest one, meaning the form with the fewest unrecognized switches.

**Forms must be distinguishable.** The generator rejects any set of forms that a single command line could match more than once (CLI024). Two forms are distinguishable when either:
- one has a required switch the other doesn't recognize, or
- the number of ordinals they accept can't overlap.

That's why `--list` above is `Required = true`: it's what tells the "list" form apart from "remove", which accepts no ordinals either.

## What gets generated

For each `[CliArgs]` type `T`:

| Member | |
|---|---|
| `static T Create(IEnumerable<string> args)` | Parses already-split arguments. Throws `CmdException`. |
| `static bool TryCreate(IEnumerable<string> args, out T result, out CmdException error)` | The same, returning `false` instead of throwing. |
| `static T Parse(string commandLine = null)` | Parses a whole command line, program name first. `null` means `Environment.CommandLine`. Throws `CmdException`. |
| `static bool TryParse(string commandLine, out T result, out CmdException error)` | The same, returning `false` instead of throwing. |
| `static string GetUsage(int width = 0)` | The complete usage screen. |
| `static void PrintUsage(TextWriter writer = null, int width = 0)` | Writes the usage screen, to standard error by default. |
| `static string GetUsageArguments(int width = 0, int startOffset = 0, string group = null)` | Just the synopsis line, such as `<input> [--verbose]`. |
| `static string GetUsageDescriptions(int width = 0, string group = null)` | Just the table of arguments and descriptions. |
| `string ArgumentGroup { get; }` | Which usage form matched. Only generated when you use groups. |
| `void Dispose()` | Only when an argument is disposable. See [Disposal](#disposal). |

Once per project, it also generates `CliUtility` and `CmdException`, in the namespace of your first `[CliArgs]` type.

To see exactly what was generated, add `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` to your project. The files then appear under `obj/.../generated/`.

## Errors and help

Everything the user can get wrong is reported as a `CmdException`:

| Property | |
|---|---|
| `Message` | Ready to show the user, e.g. `Invalid value "shouty" for --case: Requested value 'shouty' was not found.` |
| `ArgumentName` | The argument involved, as displayed (`--count`, `<input>`), or null. |
| `Group` | The usage form that was being matched, or null. |
| `IsHelpRequest` | `true` when the user asked for help rather than making a mistake. |
| `InnerException` | For conversion failures, the original exception from `Parse`, the constructor and so on. |

Errors you can expect:

- missing required arguments;
- unknown switches;
- a switch given twice (except list switches, which accumulate);
- a switch with no value;
- too many ordinals;
- a value that doesn't convert.

The pattern in [Getting started](#getting-started) handles both errors and help: print the message unless it's a help request, then print usage, and exit with 0 for help or 1 for an error.

## Usage text

`PrintUsage()` produces something like this, wrapped to the console width:

```
MyTool v1.2.0.0

Copies a text file, optionally changing its case.

Usage: mytool <input> [<output>] [--case <mode>] [--verbose]

    <input>            The file to read, or - for standard input.
    <output>           The file to write, or - for standard output. Defaults
                       to <stdout>.
    -c, --case <mode>  keep, upper or lower. Defaults to Keep.
    -v, --verbose      Write progress to standard error.
```

- The title comes from `<AssemblyTitle>` (or the assembly name) plus the version.
- The description comes from `[CliArgs(Description)]` or `<Description>`.
- Optional arguments automatically get "Defaults to …", taken from their initial values, unless your description already mentions a default.
- With groups, each form gets its own synopsis and table.

To lay out your own screen, combine `GetUsageArguments` and `GetUsageDescriptions` with `CliUtility.WordWrap`:

```csharp
var prefix = "usage: " + CliUtility.ExeName + " ";
Console.Error.WriteLine(prefix + Options.GetUsageArguments(startOffset: prefix.Length));
Console.Error.Write(Options.GetUsageDescriptions());
```

## Disposal

If any argument holds something disposable, such as a `TextReader`, a `TextWriter`, or a list of them, the type gets `IDisposable` and a generated `Dispose()`:

- Every `[CmdArg]` member holding a disposable value is disposed, including the items of collections. This includes values that came from your initializers, not just ones the parser created, as in the old CliUtility.
- `Console.In`, `Console.Out` and `Console.Error` are never disposed, so the usual defaults and `-` are safe.
- Members that were disposed are set to `null`, or emptied if they're get-only collections, so calling `Dispose()` twice does nothing.
- Disposing a `TextWriter` flushes it, so `using (opts)` is all it takes to make sure output reaches the file.

```csharp
using (opts)
{
    // ...
}
```

If your type, or a base class, already has a `Dispose()` (including an explicit `IDisposable.Dispose`), nothing is generated and yours is left alone.

**Structs.** A struct with disposable arguments still gets `Dispose()`, along with warning CLI006. Every copy of a struct shares the same readers and writers, so disposing one copy closes them for all of them. Use a class instead, or suppress the warning with `#pragma warning disable CLI006` if you know what you're doing.

## CliUtility reference

`CliUtility` is a static class generated once per project, next to your first `[CliArgs]` type. Several `[CliArgs]` types share it.

**Text**

| Member | |
|---|---|
| `WordWrap(string text, int width = 0, int indent = 0, int startOffset = 0)` | Wraps text to `width` characters. `0` means the console width. Continuation lines are indented by `indent` spaces. `startOffset` is the column the first line starts at, when something is already printed before it. Newlines in the text are kept. Join words with ` ` (a non-breaking space) to keep them on one line. |
| `ConsoleWidth` | The usable width of the console, or 79 when output is redirected or there's no console. |

```csharp
Console.WriteLine(CliUtility.WordWrap(longMessage, indent: 4));
```

**Program information**

| Member | |
|---|---|
| `AssemblyTitle` | `<AssemblyTitle>` of the entry assembly, or its name. |
| `AssemblyDescription` | `<Description>` of the entry assembly, or null. |
| `AssemblyVersion` | The entry assembly's version. |
| `ExeName` | The running program's file name without extension. |
| `ParseExePath(string commandLine)` | The program path from the start of a full command line. |
| `IsWindows` | Whether the program is running on Windows. |
| `SwitchPrefix`, `ShortSwitchPrefix` | `"--"` and `"-"`. |

**Files**

| Member | |
|---|---|
| `IsStale(string input, string output)` | `true` if `output` doesn't exist or is older than `input`. |
| `IsStale(FileSystemInfo input, FileSystemInfo output)` | The same, for `FileInfo`/`DirectoryInfo`. |
| `IsStale(IEnumerable<FileSystemInfo> inputs, FileSystemInfo output)` | `true` if `output` is older than any input, or there are no inputs. |
| `IsStale(TextReader input, TextWriter output)` | The same for readers and writers. These include the ones the parser creates, even before the output file exists. It's `true` if either isn't a file. |
| `IsStale(IEnumerable<TextReader> inputs, TextWriter output)` | The multi-input form. |
| `GetFilename(TextReader)`, `GetFilename(TextWriter)` | The file behind a reader or writer, or null for the console and other streams. |

`IsStale` makes "only rebuild when the input changed" a one-liner:

```csharp
if (!opts.Force && !CliUtility.IsStale(opts.Input, opts.Output))
{
    Console.Error.WriteLine(CliUtility.GetFilename(opts.Output) + " is up to date.");
    return 0;
}
```

**Progress**

| Member | |
|---|---|
| `WriteProgressBar(int percent, bool update = true, TextWriter writer = null)` | Draws `[■■■■      ]  40%`. Pass `update: false` the first time, then `true` to redraw in place. |
| `WriteProgress(int progress, bool update = false, TextWriter writer = null)` | A spinner (`- \ \| /`) for work of unknown length. Keep incrementing `progress`, and pass `update: true` after the first call. |

Both write to standard error by default, so they don't mix with your program's output.

```csharp
CliUtility.WriteProgressBar(0, update: false);
for (int i = 1; i <= 100; ++i)
{
    DoWork(i);
    CliUtility.WriteProgressBar(i);
}
Console.Error.WriteLine();
```

**`CliUtility.Generated`** holds the parser's internals and isn't meant to be called directly. It's a single nested class so that it shows up as one entry in IntelliSense.

## Compile-time diagnostics

| Id | | Meaning |
|---|---|---|
| CLI001 | Error | The type, or a type it's nested in, isn't `partial` |
| CLI002 | Error | The type is generic |
| CLI003 | Error | The type is static or abstract |
| CLI004 | Error | No parameterless constructor |
| CLI005 | Warning | `[CliArgs]` type with no `[CmdArg]` members |
| CLI006 | Warning | `Dispose()` generated on a struct; consider a class |
| CLI010 | Error | `[CmdArg]` on a static member |
| CLI011 | Error | The member can't be assigned (readonly, init-only, or get-only and not a collection) |
| CLI012 | Error | Both a name and an ordinal |
| CLI013 | Error | No way to convert a string to the member's type (or its element type) |
| CLI014 | Error | `Converter` isn't a `TypeConverter` with a public parameterless constructor |
| CLI015 | Error | Invalid switch name (must start with a letter; letters, digits, `-` and `_` only) |
| CLI016 | Error | `ShortName` isn't a letter |
| CLI017 | Warning | `ShortName` on an ordinal argument is ignored |
| CLI018 | Error | An ordinal with no group while groups are in use |
| CLI019 | Error | Two ordinals with the same number in one form |
| CLI020 | Error | An ordinal list followed by another ordinal |
| CLI021 | Error | A required ordinal after an optional one |
| CLI022 | Error | Two arguments with the same switch name in one form |
| CLI023 | Error | Two arguments with the same short name in one form |
| CLI024 | Error | Two usage forms a single command line could match |
| CLI030 | Warning | `[CmdArg]` in a type without `[CliArgs]`; nothing is generated for it |

When a type has an error, nothing is generated for it, so calls to `Parse`, `Create` and the rest also fail until it's fixed. Warnings can be suppressed with `#pragma warning disable` or `<NoWarn>`.

## Visibility and namespaces

- `[CliArgs]` and `[CmdArg]` are always in the `Cli` namespace, so you need `using Cli;` wherever you declare arguments. The generator finds your types by those exact names.
- `CliUtility` and `CmdException` go in the namespace of your **first** `[CliArgs]` type, in the compiler's file order. If that type is in the global namespace, so are they. If you later add a `[CliArgs]` type in another namespace, in a file that sorts earlier, they move there.
- Everything generated is `internal` by default. If a `[CliArgs]` type is `public`, its `TryParse` and `TryCreate` are `internal`, because they expose `CmdException`. To make the attributes, `CliUtility` and `CmdException` public, add this to the project:

  ```xml
  <DefineConstants>$(DefineConstants);CLI_PUBLIC</DefineConstants>
  ```

  Avoid that in two projects that reference each other, since both would then export a public `CliUtility`.

## Building this repository

```
src/Cli.Generator/            the generator (targets netstandard2.0, as Visual Studio requires)
  Runtime/CliAttributes.cs    [CliArgs] and [CmdArg], emitted into every project that uses the generator
  Runtime/CliUtility.cs       template for the generated CliUtility and CmdException
samples/Sample.App/           a small file-copy tool using groups, lists, bundling, IsStale and Dispose
tests/Cli.Generator.Tests/    parsing behaviour, every diagnostic, disposal and incremental caching
```

```
dotnet build
dotnet run --project samples/Sample.App -- --help
dotnet run --project tests/Cli.Generator.Tests            # all tests
dotnet run --project tests/Cli.Generator.Tests -- groups   # tests whose name contains "groups"
dotnet pack src/Cli.Generator
```

The tests use a small built-in runner rather than xUnit, so they need no test packages. Porting them to xUnit is mechanical: `[Test]` becomes `[Fact]` and `Check.*` becomes `Assert.*`.

`Directory.Build.props` pins Roslyn 4.8, which works with the .NET 8 SDK and Visual Studio 17.8 or later. The `CliGenTargetFramework` property in the generator project only exists for building where netstandard2.0 reference assemblies aren't available; you don't normally set it.