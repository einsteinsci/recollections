using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public interface IReIterator<T>
	{
		T Next
		{ get; }
		T Previous
		{ get; }
		T Peek();
		bool HasNext
		{ get; }
		bool HasPrevious
		{ get; }
		void Remove();
		void Insert(T item);
		void Skip();
		void SkipBack();
	}
}
