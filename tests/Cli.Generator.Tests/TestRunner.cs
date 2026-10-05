using System.Reflection;

namespace Cli.Generator.Tests;

/// <summary>Marks a public static method as a test.</summary>
[AttributeUsage(AttributeTargets.Method)]
sealed class TestAttribute : Attribute { }

sealed class CheckFailedException(string message) : Exception(message);

static class Check
{
	public static void Equal<T>(T expected, T actual, string? what = null)
	{
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
			throw new CheckFailedException($"{what ?? "value"}: expected <{expected}> but got <{actual}>");
	}

	public static void Seq<T>(IEnumerable<T> expected, IEnumerable<T>? actual, string? what = null)
	{
		var e = expected.ToArray();
		var a = actual?.ToArray() ?? [];
		if (!e.SequenceEqual(a))
			throw new CheckFailedException($"{what ?? "sequence"}: expected [{string.Join(", ", e)}] but got [{string.Join(", ", a)}]");
	}

	public static void True(bool condition, string what)
	{
		if (!condition) throw new CheckFailedException(what);
	}

	public static void Contains(string expected, string actual)
	{
		if (!actual.Contains(expected, StringComparison.Ordinal))
			throw new CheckFailedException($"expected text to contain <{expected}> but it was:\n{actual}");
	}

	public static TEx Throws<TEx>(Action action) where TEx : Exception
	{
		try
		{
			action();
		}
		catch (TEx ex)
		{
			return ex;
		}
		catch (Exception ex)
		{
			throw new CheckFailedException($"expected {typeof(TEx).Name} but got {ex.GetType().Name}: {ex.Message}");
		}
		throw new CheckFailedException($"expected {typeof(TEx).Name} but nothing was thrown");
	}
}

static class TestRunner
{
	static int Main(string[] args)
	{
		var tests = typeof(TestRunner).Assembly.GetTypes()
			.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
			.Where(m => m.GetCustomAttribute<TestAttribute>() != null)
			.Where(m => args.Length == 0 || args.Any(a => (m.DeclaringType!.Name + "." + m.Name).Contains(a, StringComparison.OrdinalIgnoreCase)))
			.OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
			.ToList();
		int failed = 0;
		foreach (var test in tests)
		{
			string name = test.DeclaringType!.Name + "." + test.Name;
			try
			{
				test.Invoke(null, null);
				Console.WriteLine("  pass  " + name);
			}
			catch (TargetInvocationException tie)
			{
				++failed;
				var ex = tie.InnerException!;
				Console.WriteLine("  FAIL  " + name);
				Console.WriteLine("        " + (ex is CheckFailedException ? ex.Message : ex.ToString()).Replace("\n", "\n        "));
			}
		}
		Console.WriteLine();
		Console.WriteLine($"{tests.Count - failed} passed, {failed} failed, {tests.Count} total");
		return failed == 0 ? 0 : 1;
	}
}
