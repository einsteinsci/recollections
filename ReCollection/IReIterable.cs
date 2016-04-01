using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public interface IReIterable<T> : IReCollection<T>
	{
		IReIterator<T> MakeIterator();
		IReIterator<T> MakeIteratorEnd();

		void Foreach(ReAction<T> todo);
		void ForeachBreak();

		T Find(RePredicate<T> pred);
		IReIterable<T> FindAll(RePredicate<T> pred);
	}
}
