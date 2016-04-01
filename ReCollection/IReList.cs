using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public interface IReList<T> : IReCollection<T>, IReIterable<T>
	{
		T this[int index]
		{ get; set; }

		IReList<T> MakeNewList();

		void AddAll(ICollection<T> items);
		void AddAll(IReCollection<T> items);
		void AddAll(params T[] items);

		void Insert(int index, T item);
		void Insert(int index, ICollection<T> items);
		void Insert(int index, IReCollection<T> items);
		void Insert(int index, params T[] items);

		bool RemoveAt(int index);
		void RemoveRange(int startId, int endId);
		void RemoveAll(RePredicate<T> predicate);

		int IndexOf(T item);
		int LastIndexOf(T item);

		void Set(int index, T item);
		T Get(int index);

		bool Contains(T item);

		IList<T> ToSystemList();

		void ForeachReversed(ReAction<T> todo);

		T FindLast(RePredicate<T> pred);
		int FindIndex(RePredicate<T> pred);
		int FindLastIndex(RePredicate<T> pred);

		void Sort(ReComparison<T> comparer);
		void Reverse();

		bool TrueForAll(RePredicate<T> pred);
	}
}
