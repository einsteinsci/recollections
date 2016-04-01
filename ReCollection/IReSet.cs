using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public interface IReSet<T> : IReCollection<T>, IReIterable<T>
	{
		IReSet<T> MakeNewSet();

		void AddAll(ICollection<T> items);
		void AddAll(IReCollection<T> items);
		void AddAll(params T[] items);

		void RemoveAll(RePredicate<T> pred);

		bool Contains(T item);

		bool IsSubsetOf(IReSet<T> other);
		bool IsSupersetOf(IReSet<T> other);

		IReSet<T> Union(IReSet<T> other);
		IReSet<T> Intersect(IReSet<T> other);
		IReSet<T> Xor(IReSet<T> other);

		ISet<T> ToSystemSet();
		IReList<T> ToList();

		bool TrueForAll(RePredicate<T> pred);
	}
}
