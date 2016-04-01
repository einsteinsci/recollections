using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	interface IReSortedSet<T> : IReSet<T>
	{
		ReComparison<T> Comparer
		{ get; }

		T Lowest
		{ get; }
		T Highest
		{ get; }

		IReSortedSet<T> ValuesBetween(T lowBound, T highBound);
	}
}
