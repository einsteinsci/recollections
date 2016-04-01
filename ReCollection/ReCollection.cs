using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public abstract class ReCollection<T> : IReCollection<T>
	{
		// Show newlines when converting to string
		public static bool ToStringNewlines
		{ get; set; }
		protected static bool hasInitialized = false;

		public abstract int Count { get; }
		public virtual bool IsEmpty
		{
			get
			{
				return Count == 0;
			}
		}

		public abstract void Add(T added);
		public abstract bool Remove(T removed);
		public abstract void Clear();
		public abstract T[] ToArray();

		protected ReCollection()
		{
			if (!hasInitialized)
			{
				Initialize();
			}
		}

		// STATIC INITIALIZER
		protected static void Initialize()
		{
			ToStringNewlines = false;

			hasInitialized = true;
		}

		public abstract bool Exists(RePredicate<T> pred);
	}
}
