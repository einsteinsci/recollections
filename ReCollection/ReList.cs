using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public abstract class ReList<T> : ReCollection<T>,
		IReList<T>
	{
		protected bool breaking;
		protected bool isIterating;

		public virtual T this[int index]
		{
			get
			{
				return Get(index);
			}
			set
			{
				Set(index, value);
			}
		}

		public ReList() : base()
		{
			isIterating = false;
			breaking = false;
		}

		public abstract IReList<T> MakeNewList();

		public virtual void AddAll(params T[] items)
		{
			foreach (T t in items)
			{
				Add(t);
			}
		}
		public virtual void AddAll(IReCollection<T> items)
		{
			AddAll(items.ToArray());
		}
		public virtual void AddAll(ICollection<T> items)
		{
			T[] arr = new T[Count];
			items.CopyTo(arr, 0);
			AddAll(arr);
		}

		public virtual bool Contains(T item)
		{
			if (IsEmpty)
			{
				return false;
			}

			return IndexOf(item) != -1;
		}
		public override bool Exists(RePredicate<T> pred)
		{
			bool res = false;
			Foreach((t) =>
			{
				if (pred(t))
				{
					res = true;
					ForeachBreak();
				}
			});

			return res;
		}

		public virtual T Find(RePredicate<T> pred)
		{
			T res = default(T);
			Foreach((t) =>
			{
				if (pred(t))
				{
					res = t;
					ForeachBreak();
				}
			});

			return res;
		}
		public virtual int FindIndex(RePredicate<T> pred)
		{
			for (int i = 0; i < Count; i++)
			{
				if (pred(Get(i)))
				{
					return i;
				}
			}
			return -1;
		}
		public virtual T FindLast(RePredicate<T> pred)
		{
			T res = default(T);
			ForeachReversed((t) =>
			{
				if (pred(t))
				{
					res = t;
					ForeachBreak();
				}
			});

			return res;
		}
		public virtual int FindLastIndex(RePredicate<T> pred)
		{
			for (int i = Count - 1; i >= 0; i--)
			{
				if (pred(Get(i)))
				{
					return i;
				}
			}
			return -1;
		}
		public virtual IReIterable<T> FindAll(RePredicate<T> pred)
		{
			IReList<T> res = MakeNewList();
			Foreach((t) =>
			{
				if (pred(t))
				{
					res.Add(t);
				}
			});

			return res;
		}

		public virtual void Foreach(ReAction<T> todo)
		{
			isIterating = true;
			IReIterator<T> i = MakeIterator();

			while (i.HasNext)
			{
				if (breaking)
				{
					break;
				}

				T t = i.Next;
				todo(t);
			}

			breaking = false;
			isIterating = false;
		}
		public virtual void ForeachBreak()
		{
			breaking = true;
		}
		public virtual void ForeachReversed(ReAction<T> todo)
		{
			isIterating = true;
			IReIterator<T> i = MakeIteratorEnd();

			while (i.HasPrevious)
			{
				if (breaking)
				{
					break;
				}

				T t = i.Previous;
				todo(t);
			}

			breaking = false;
			isIterating = false;
		}

		public abstract T Get(int index);
		public abstract void Set(int index, T item);

		public virtual int IndexOf(T item)
		{
			IReIterator<T> iter = MakeIterator();

			int i = 0;
			T t = iter.Peek();
			while (iter.HasNext)
			{				
				if (item.Equals(t))
				{
					return i;
				}

				t = iter.Next;
				i++;
			}

			if (item.Equals(t))
			{
				return i;
			}

			return -1;
		}
		public virtual int LastIndexOf(T item)
		{
			IReIterator<T> iter = MakeIteratorEnd();

			int i = Count - 1;
			T t = iter.Peek();
			while (iter.HasPrevious)
			{
				if (item.Equals(t))
				{
					return i;
				}

				t = iter.Previous;
				i--;
			}

			if (item.Equals(t))
			{
				return i;
			}

			return -1;
		}

		public abstract void Insert(int index, T item);
		public virtual void Insert(int index, IReCollection<T> items)
		{
			Insert(index, items.ToArray());
		}
		public virtual void Insert(int index, params T[] items)
		{
			for (int i = items.Length - 1; i >= 0; i--)
			{
				Insert(index, items[i]);
			}
		}
		public virtual void Insert(int index, ICollection<T> items)
		{
			Insert(index, items.ToArray());
		}

		public abstract IReIterator<T> MakeIterator();
		public abstract IReIterator<T> MakeIteratorEnd();

		public override T[] ToArray()
		{
			T[] arr = new T[Count];
			for (int i = 0; i < Count; i++)
			{
				arr[i] = Get(i);
			}

			return arr;
		}

		public virtual void RemoveAll(RePredicate<T> predicate)
		{
			object[] arr = ToArray() as object[];
			for (int i = 0; i < Count; i++)
			{
				if (arr[i] == null)
				{
					arr[i] = "null"; // haha lol
				}
			}

			for (int i = 0; i < Count; i++)
			{
				if (predicate(Get(i)))
				{
					arr[i] = null;
				}
			}

			Clear();
			foreach (object o in arr)
			{
				if (o == null)
				{
					continue; // skip
				}
				if (o as string == "null")
				{
					Add(default(T));
					continue;
				}

				Add((T)o);
			}
		}
		public abstract bool RemoveAt(int index);
		public override bool Remove(T removed)
		{
			return RemoveAt(IndexOf(removed));
		}
		public virtual void RemoveRange(int startId, int endId)
		{
			object[] arr = ToArray() as object[];
			for (int i = 0; i < Count; i++)
			{
				if (arr[i] == null)
				{
					arr[i] = "null"; // haha lol
				}
			}

			for (int i = startId; i < endId; i++)
			{
				arr[i] = null;
			}

			Clear();
			foreach (object o in arr)
			{
				if (o == null)
				{
					continue; // skip
				}
				if (o as string == "null")
				{
					Add(default(T)); // will be null as any cause for "null" would mean T is a ref type
					continue;
				}

				Add((T)o);
			}
		}

		public virtual void Reverse()
		{
			T[] arr = ToArray();
			Clear();

			for (int i = arr.Length; i >= 0; i--)
			{
				Add(arr[i]);
			}
		}

		public virtual void Sort(ReComparison<T> comparer)
		{
			T[] arr = ToArray();

			int leftBound = 0;
			int rightBound = arr.Length - 1;

			arr = Util.InternalMergeSort<T>(arr, leftBound, rightBound, comparer);

			Clear();
			AddAll(arr);
		}

		public virtual IList<T> ToSystemList()
		{
			List<T> sys = new List<T>();
			Foreach((t) =>
			{
				sys.Add(t);
			});
			return sys;
		}

		public virtual bool TrueForAll(RePredicate<T> pred)
		{
			bool res = true;
			Foreach((t) =>
			{
				if (!pred(t))
				{
					res = false;
					ForeachBreak();
				}
			});

			return res;
		}

		public override string ToString()
		{
			string res = "{" + (ToStringNewlines ? "\n" : " ");
			for (int i = 0; i < Count; i++)
			{
				res += (ToStringNewlines ? "  " : "") + Get(i).ToString() +
					(i == Count - 1 ? "" : ",") + (ToStringNewlines ? "\n" : " ");
			}

			return res + "}";
		}
	}
}
