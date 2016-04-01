using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public class ReArrayList<T> : ReList<T>,
		IReList<T>, IReCollection<T>, IReIterable<T>
	{
		public sealed class Iterator : IReIterator<T>
		{
			ReArrayList<T> owner;
			T[] array
			{
				get
				{
					return owner.array;
				}
				set
				{
					owner.array = value;
				}
			}
			int currentId;

			public bool HasNext
			{
				get
				{
					return array.Length > currentId + 1;
				}
			}
			public bool HasPrevious
			{
				get
				{
					return currentId > 0;
				}
			}

			public T Next
			{
				get
				{
					if (HasNext)
					{
						currentId++;
					}

					return array[currentId];
				}
			}
			public T Previous
			{
				get
				{
					if (HasPrevious)
					{
						currentId--;
					}

					return array[currentId];
				}
			}

			internal Iterator(ReArrayList<T> host, int startId)
			{
				owner = host;
				currentId = startId;
			}

			public void Insert(T item)
			{
				T[] copy = new T[array.Length + 1];

				for (int i = 0; i < currentId; i++)
				{
					copy[i] = array[i];
				}
				copy[currentId] = item;

				for (int i = currentId; i < array.Length; i++)
				{
					copy[i + 1] = array[i];
				}

				array = copy;
			}

			public T Peek()
			{
				return array[currentId];
			}

			public void Remove()
			{
				T[] copy = new T[array.Length - 1];

				int j = 0;
				for (int i = 0; i < array.Length; i++)
				{
					if (i != currentId)
					{
						copy[j] = array[i];
						j++;
					}
				}
			}

			public void Skip()
			{
				currentId++;
			}
			public void SkipBack()
			{
				currentId--;
			}
		}

		T[] array;

		public override int Count
		{
			get
			{
				return array.Length;
			}
		}

		public ReArrayList() : base()
		{
			array = new T[0];
		}

		public override IReList<T> MakeNewList()
		{
			return new ReArrayList<T>();
		}

		public override void Add(T added)
		{
			T[] copy = new T[Count + 1];
			for (int i = 0; i < Count; i++)
			{
				copy[i] = array[i];
			}
			copy[Count] = added;

			array = copy;
		}

		public override void Clear()
		{
			array = new T[0];
		}

		public override T Get(int index)
		{
			return array[index];
		}

		public override void Insert(int index, T item)
		{
			if (isIterating)
			{
				throw new InvalidOperationException("Cannot insert items while iterating. Adding to the end with " +
					"Add() is allowed, however.");
			}

			if (index < 0 || index > Count)
			{
				throw new IndexOutOfRangeException("index " + index.ToString() + " is out of range.");
			}

			if (index == Count)
			{
				Add(item);
				return;
			}

			T[] copy = new T[Count + 1];

			for (int i = 0; i < index; i++)
			{
				copy[i] = array[i];
			}
			copy[index] = item;
			for (int i = index; i < Count; i++)
			{
				copy[i + 1] = array[i];
			}

			array = copy;
		}

		public override IReIterator<T> MakeIterator()
		{
			return new Iterator(this, 0);
		}
		public override IReIterator<T> MakeIteratorEnd()
		{
			return new Iterator(this, Count - 1);
		}

		public override bool RemoveAt(int index)
		{
			if (isIterating)
			{
				throw new InvalidOperationException("Cannot remove items while iterating");
			}

			if (index < 0 || index >= Count || Count == 0)
			{
				return false;
			}

			T[] copy = new T[Count - 1];

			int j = 0;
			for (int i = 0; i < array.Length; i++)
			{
				if (i != index)
				{
					copy[j] = array[i];
					j++;
				}
			}

			array = copy;
			return true;
		}

		public override void Set(int index, T item)
		{
			array[index] = item;
		}
	}
}
