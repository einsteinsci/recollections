using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public abstract class ReSet<T> : IReSet<T>, IReIterable<T>
	{
		protected bool breaking;
		protected bool isIterating;

		public abstract int Count { get; }

		public ReSet()
		{
			breaking = false;
			isIterating = false;
		}

		public abstract IReSet<T> MakeNewSet();

		public abstract void Add(T added);
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

		public abstract bool Remove(T removed);
		public abstract void RemoveAll(RePredicate<T> pred);

		public abstract void Clear();

		public abstract bool Contains(T item);
		public abstract bool Exists(RePredicate<T> pred);

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
		public virtual IReIterable<T> FindAll(RePredicate<T> pred)
		{
			IReSet<T> res = MakeNewSet();
			Foreach((t) =>
			{
				if (pred(t))
				{
					res.Add(t);
				}
			});

			return res;
		}

		public abstract void Foreach(ReAction<T> todo);
		public virtual void ForeachBreak()
		{
			breaking = true;
		}

		public virtual IReSet<T> Union(IReSet<T> other)
		{
			IReSet<T> set = MakeNewSet();
			set.AddAll(this);
			set.AddAll(other);

			return other;
		}
		public virtual IReSet<T> Xor(IReSet<T> other)
		{
			IReSet<T> set = MakeNewSet();
			this.Union(other).Foreach((t) =>
			{
				if (this.Contains(t) && other.Contains(t))
				{
					return;
				}

				set.Add(t);
			});

			return set;
		}
		public virtual IReSet<T> Intersect(IReSet<T> other)
		{
			IReSet<T> set = MakeNewSet();
			Foreach((t) =>
			{
				if (this.Contains(t) && other.Contains(t))
				{
					set.Add(t);
				}
			});

			return set;
		}

		public virtual bool IsSubsetOf(IReSet<T> other)
		{
			bool res = true;
			Foreach((t) =>
			{
				if (!other.Contains(t))
				{
					res = false;
					ForeachBreak();
				}
			});

			return res;
		}
		public virtual bool IsSupersetOf(IReSet<T> other)
		{
			return other.IsSubsetOf(this);
		}

		public abstract IReIterator<T> MakeIterator();
		public abstract IReIterator<T> MakeIteratorEnd();

		public virtual T[] ToArray()
		{
			T[] arr = new T[Count];
			int i = 0;
			Foreach((t) =>
			{
				arr[i] = t;
				i++;
			});

			return arr;
		}
		public virtual IReList<T> ToList()
		{
			IReList<T> list = new ReArrayList<T>();
			Foreach((t) =>
			{
				list.Add(t);
			});

			return list;
		}
		public virtual ISet<T> ToSystemSet()
		{
			ISet<T> set = new SortedSet<T>();
			Foreach((t) =>
			{
				set.Add(t);
			});

			return set;
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
			return ToList().ToString();
		}
	}
}
