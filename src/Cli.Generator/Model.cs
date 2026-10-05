using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Cli.Generator
{
	/// <summary>An immutable array with value equality, so pipeline outputs cache correctly.</summary>
	internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
		where T : IEquatable<T>
	{
		readonly T[]? _items;

		public EquatableArray(IEnumerable<T> items)
		{
			_items = items.ToArray();
		}

		public static readonly EquatableArray<T> Empty = new EquatableArray<T>(Array.Empty<T>());

		public int Count => _items?.Length ?? 0;
		public T this[int index] => _items![index];

		public bool Equals(EquatableArray<T> other)
		{
			var a = _items ?? Array.Empty<T>();
			var b = other._items ?? Array.Empty<T>();
			if (a.Length != b.Length) return false;
			for (int i = 0; i < a.Length; ++i)
			{
				if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
			}
			return true;
		}

		public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

		public override int GetHashCode()
		{
			unchecked
			{
				int h = 17;
				if (_items != null)
				{
					foreach (var item in _items) h = h * 31 + (item == null ? 0 : item.GetHashCode());
				}
				return h;
			}
		}

		public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	internal static class EquatableArray
	{
		public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items) where T : IEquatable<T>
			=> new EquatableArray<T>(items);
	}

	internal enum ArgKind
	{
		Flag,
		Scalar,
		List
	}

	internal enum CollectionShape
	{
		/// <summary>Not a collection.</summary>
		None,
		/// <summary>A one-dimensional array, built from a List and assigned.</summary>
		Array,
		/// <summary>A new collection is created, filled through ICollection&lt;T&gt;.Add and assigned.</summary>
		Create,
		/// <summary>A get-only collection that already exists; it is cleared and refilled.</summary>
		Existing
	}

	internal sealed record LocationInfo(string Path, TextSpan Span, LinePositionSpan LineSpan)
	{
		public Location ToLocation() => Location.Create(Path, Span, LineSpan);

		public static LocationInfo? From(Location? location)
		{
			if (location == null || location.SourceTree == null) return null;
			return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
		}
	}

	internal sealed record DiagInfo(string Id, LocationInfo? Location, EquatableArray<string> Args)
	{
		public static DiagInfo Create(string id, Location? location, params string[] args)
			=> new DiagInfo(id, LocationInfo.From(location), args.ToEquatableArray());
	}

	internal sealed record ArgModel(
		int Id,
		string MemberName,
		string LongName,
		char ShortName,
		int Position,
		ArgKind Kind,
		bool Required,
		string ValueName,
		string? Description,
		string Group,
		string ConvertTemplate,
		CollectionShape Shape,
		string? ElementType,
		string? CreateExpression,
		bool ShowDefault,
		bool MayDispose,
		bool StaticallyDisposable,
		bool Settable)
	{
		public bool IsPositional => Position >= 0;
	}

	internal sealed record GroupModel(
		string Name,
		EquatableArray<int> Positional,
		EquatableArray<int> Named,
		string Synopsis,
		EquatableArray<string> Lefts,
		EquatableArray<int> RowIds);

	internal sealed record TypeModel(
		bool IsPublic,
		string? Namespace,
		EquatableArray<string> ContainerDeclarations,
		string TypeDeclaration,
		string TypeName,
		string FullName,
		string HintName,
		string? ExeName,
		string? Description,
		EquatableArray<ArgModel> Args,
		EquatableArray<GroupModel> Groups,
		bool HasNamedGroups,
		bool GenerateDispose,
		EquatableArray<DiagInfo> Diagnostics)
	{
		public bool HasErrors => Diagnostics.Any(d => Descriptors.IsError(d.Id));
	}
}

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
	internal static class IsExternalInit { }
}
#endif
