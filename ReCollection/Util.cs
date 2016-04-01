using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	internal static class Util
	{
		public static ReComparison<int> DefComparerInt
		{
			get
			{
				return (a, b) => a - b;
			}
		}

		public static T[] InternalMergeSort<T>(T[] inputArray, int left, int right, ReComparison<T> comparer)
		{
			T[] array = inputArray;
			int mid = 0;
			if (left < right)
			{
				mid = (left + right) / 2;
				InternalMergeSort(array, left, mid, comparer);
				InternalMergeSort(array, (mid + 1), right, comparer);
				return MergeSortedArray(array, left, mid, right, comparer);
			}

			return array;
		}

		public static T[] MergeSortedArray<T>(T[] inputArray, int left, int mid, int right, ReComparison<T> comparer)
		{
			int index = 0;
			int total_elements = right - left + 1;	 //BODMAS rule
			int right_start = mid + 1;
			int temp_location = left;
			T[] tempArray = new T[total_elements];

			while ((left <= mid) && right_start <= right)
			{
				if (comparer(inputArray[left], inputArray[right_start]) < 0)
				{
					tempArray[index++] = inputArray[left++];
				}
				else
				{
					tempArray[index++] = inputArray[right_start++];
				}
			}
			if (left > mid)
			{
				for (int j = right_start; j <= right; j++)
					tempArray[index++] = inputArray[right_start++];
			}
			else
			{
				for (int j = left; j <= mid; j++)
					tempArray[index++] = inputArray[left++];
			}
			// Array.Copy(tempArray, 0, inputArray, temp_location, total_elements);
			// just another way of accomplishing things (in-built copy)
			for (int i = 0, j = temp_location; i < total_elements; i++, j++)
			{
				inputArray[j] = tempArray[i];
			}

			return inputArray;
		}
	}
}
