using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public interface IReCollection<T>
	{
		int Count
		{ get; }

		void Add(T added);
		bool Remove(T removed);
		void Clear();
		T[] ToArray();

		bool Exists(RePredicate<T> pred);
	}
}
