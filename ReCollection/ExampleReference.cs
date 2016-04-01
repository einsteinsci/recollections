﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;

namespace Nerr.Collections.Generic
{

	/// <summary>
	/// Implementation notes:
	/// This uses an array-based implementation similar to Dictionary<t>, using a buckets array
	/// to map hash values to the Slots array. Items in the Slots array that hash to the same value
	/// are chained together through the "next" indices.
	///
	/// The capacity is always prime; so during resizing, the capacity is chosen as the next prime
	/// greater than double the last capacity.
	///
	/// The underlying data structures are lazily initialized. Because of the observation that,
	/// in practice, hashtables tend to contain only a few elements, the initial capacity is
	/// set very small (3 elements) unless the ctor with a collection is used.
	///
	/// The +/- 1 modifications in methods that add, check for containment, etc allow us to
	/// distinguish a hash code of 0 from an uninitialized bucket. This saves us from having to
	/// reset each bucket to -1 when resizing. See Contains, for example.
	///
	/// Set methods such as UnionWith, IntersectWith, ExceptWith, and SymmetricExceptWith modify
	/// this set.
	///
	/// Some operations can perform faster if we can assume "other" contains unique elements
	/// according to this equality comparer. The only times this is efficient to check is if
	/// other is a hashset. Note that checking that it's a hashset alone doesn't suffice; we
	/// also have to check that the hashset is using the same equality comparer. If other
	/// has a different equality comparer, it will have unique elements according to its own
	/// equality comparer, but not necessarily according to ours. Therefore, to go these
	/// optimized routes we check that other is a hashset using the same equality comparer.
	///
	/// A HashSet with no elements has the properties of the empty set. (See IsSubset, etc. for
	/// special empty set checks.)
	///
	/// A couple of methods have a special case if other is this (e.g. SymmetricExceptWith).
	/// If we didn't have these checks, we could be iterating over the set and modifying at
	/// the same time.
	/// </t></summary>
	/// <typeparam name="T"></typeparam>
	[Serializable()]
	[DebuggerTypeProxy(typeof(System.Collections.Generic.HashSetDebugView<>))]
	[DebuggerDisplay("Count = {Count}")]
	[System.Security.Permissions.HostProtection(MayLeakOnAbort = true)]
	[SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "By design")]
	public class HashSet<T> : ICollection<T>, ISerializable, IDeserializationCallback, ISet<T>
	{

		// store lower 31 bits of hash code
		private const int Lower31BitMask = 0x7FFFFFFF;
		// factor used to increase hashset capacity
		private const int GrowthFactor = 2;
		// cutoff point, above which we won't do stackallocs. This corresponds to 100 integers.
		private const int StackAllocThreshold = 100;
		// when constructing a hashset from an existing collection, it may contain duplicates,
		// so this is used as the max acceptable excess ratio of capacity to count. Note that
		// this is only used on the ctor and not to automatically shrink if the hashset has, e.g,
		// a lot of adds followed by removes. Users must explicitly shrink by calling TrimExcess.
		// This is set to 3 because capacity is acceptable as 2x rounded up to nearest prime.
		private const int ShrinkThreshold = 3;

		// constants for serialization
		private const String CapacityName = "Capacity";
		private const String ElementsName = "Elements";
		private const String ComparerName = "Comparer";
		private const String VersionName = "Version";

		private int[] m_buckets;
		private Slot[] m_slots;
		private int m_count;
		private int m_lastIndex;
		private int m_freeList;
		private IEqualityComparer<T> m_comparer;
		private int m_version;

		// temporary variable needed during deserialization
		private SerializationInfo m_siInfo;

		#region Constructors

		public HashSet()
			: this(EqualityComparer<T>.Default)
		{ }

		public HashSet(IEqualityComparer<T> comparer)
		{
			if (comparer == null)
			{
				comparer = EqualityComparer<T>.Default;
			}

			this.m_comparer = comparer;
			m_lastIndex = 0;
			m_count = 0;
			m_freeList = -1;
			m_version = 0;
		}

		public HashSet(IEnumerable<T> collection)
			: this(collection, EqualityComparer<T>.Default)
		{ }

		/// <summary>
		/// Implementation Notes:
		/// Since resizes are relatively expensive (require rehashing), this attempts to minimize
		/// the need to resize by setting the initial capacity based on size of collection.
		/// </summary>
		/// <param name="collection">
		/// <param name="comparer">
		public HashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
			: this(comparer)
		{
			if (collection == null)
			{
				throw new ArgumentNullException("collection");
			}

			// to avoid excess resizes, first set size based on collection's count. Collection
			// may contain duplicates, so call TrimExcess if resulting hashset is larger than
			// threshold
			int suggestedCapacity = 0;
			ICollection<T> coll = collection as ICollection<T>;
			if (coll != null)
			{
				suggestedCapacity = coll.Count;
			}
			Initialize(suggestedCapacity);

			this.UnionWith(collection);
			if ((m_count == 0 && m_slots.Length > HashHelpers.GetMinPrime()) ||
				(m_count > 0 && m_slots.Length / m_count > ShrinkThreshold))
			{
				TrimExcess();
			}
		}

		protected HashSet(SerializationInfo info, StreamingContext context)
		{
			// We can't do anything with the keys and values until the entire graph has been
			// deserialized and we have a reasonable estimate that GetHashCode is not going to
			// fail.  For the time being, we'll just cache this.  The graph is not valid until
			// OnDeserialization has been called.
			m_siInfo = info;
		}

		#endregion

		#region ICollection<t> methods

		/// <summary>
		/// Add item to this hashset. This is the explicit implementation of the ICollection<t>
		/// interface. The other Add method returns bool indicating whether item was added.
		/// </t></summary>
		/// <param name="item">item to add
		void ICollection<T>.Add(T item)
		{
			AddIfNotPresent(item);
		}

		/// <summary>
		/// Remove all items from this set. This clears the elements but not the underlying
		/// buckets and slots array. Follow this call by TrimExcess to release these.
		/// </summary>
		public void Clear()
		{
			if (m_lastIndex > 0)
			{
				Debug.Assert(m_buckets != null, "m_buckets was null but m_lastIndex > 0");

				// clear the elements so that the gc can reclaim the references.
				// clear only up to m_lastIndex for m_slots
				Array.Clear(m_slots, 0, m_lastIndex);
				Array.Clear(m_buckets, 0, m_buckets.Length);
				m_lastIndex = 0;
				m_count = 0;
				m_freeList = -1;
			}
			m_version++;
		}

		/// <summary>
		/// Checks if this hashset contains the item
		/// </summary>
		/// <param name="item">item to check for containment
		/// <returns>true if item contained; false if not</returns>
		public bool Contains(T item)
		{
			if (m_buckets != null)
			{
				int hashCode = InternalGetHashCode(item);
				// see note at "HashSet" level describing why "- 1" appears in for loop
				for (int i = m_buckets[hashCode % m_buckets.Length] - 1; i >= 0; i = m_slots[i].next)
				{
					if (m_slots[i].hashCode == hashCode && m_comparer.Equals(m_slots[i].value, item))
					{
						return true;
					}
				}
			}
			// either m_buckets is null or wasn't found
			return false;
		}

		/// <summary>
		/// Copy items in this hashset to array, starting at arrayIndex
		/// </summary>
		/// <param name="array">array to add items to
		/// <param name="arrayIndex">index to start at
		public void CopyTo(T[] array, int arrayIndex)
		{
			CopyTo(array, arrayIndex, m_count);
		}

		/// <summary>
		/// Remove item from this hashset
		/// </summary>
		/// <param name="item">item to remove
		/// <returns>true if removed; false if not (i.e. if the item wasn't in the HashSet)</returns>
		public bool Remove(T item)
		{
			if (m_buckets != null)
			{
				int hashCode = InternalGetHashCode(item);
				int bucket = hashCode % m_buckets.Length;
				int last = -1;
				for (int i = m_buckets[bucket] - 1; i >= 0; last = i, i = m_slots[i].next)
				{
					if (m_slots[i].hashCode == hashCode && m_comparer.Equals(m_slots[i].value, item))
					{
						if (last < 0)
						{
							// first iteration; update buckets
							m_buckets[bucket] = m_slots[i].next + 1;
						}
						else
						{
							// subsequent iterations; update 'next' pointers
							m_slots[last].next = m_slots[i].next;
						}
						m_slots[i].hashCode = -1;
						m_slots[i].value = default(T);
						m_slots[i].next = m_freeList;

						m_count--;
						m_version++;
						if (m_count == 0)
						{
							m_lastIndex = 0;
							m_freeList = -1;
						}
						else
						{
							m_freeList = i;
						}
						return true;
					}
				}
			}
			// either m_buckets is null or wasn't found
			return false;
		}

		/// <summary>
		/// Number of elements in this hashset
		/// </summary>
		public int Count
		{
			get { return m_count; }
		}

		/// <summary>
		/// Whether this is readonly
		/// </summary>
		bool ICollection<T>.IsReadOnly
		{
			get { return false; }
		}

		#endregion

		#region IEnumerable methods

		public Enumerator GetEnumerator()
		{
			return new Enumerator(this);
		}

		IEnumerator<T> IEnumerable<T>.GetEnumerator()
		{
			return new Enumerator(this);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return new Enumerator(this);
		}

		#endregion

		#region ISerializable methods

		[SecurityPermissionAttribute(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
		public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			if (info == null)
			{
				throw new ArgumentNullException("info");
			}

			// need to serialize version to avoid problems with serializing while enumerating
			info.AddValue(VersionName, m_version);
			info.AddValue(ComparerName, m_comparer, typeof(IEqualityComparer<T>));
			info.AddValue(CapacityName, m_buckets == null ? 0 : m_buckets.Length);
			if (m_buckets != null)
			{
				T[] array = new T[m_count];
				CopyTo(array);
				info.AddValue(ElementsName, array, typeof(T[]));
			}
		}

		#endregion

		#region IDeserializationCallback methods

		public virtual void OnDeserialization(Object sender)
		{

			if (m_siInfo == null)
			{
				// It might be necessary to call OnDeserialization from a container if the
				// container object also implements OnDeserialization. However, remoting will
				// call OnDeserialization again. We can return immediately if this function is
				// called twice. Note we set m_siInfo to null at the end of this method.
				return;
			}

			int capacity = m_siInfo.GetInt32(CapacityName);
			m_comparer = (IEqualityComparer<T>)m_siInfo.GetValue(ComparerName, typeof(IEqualityComparer<T>));
			m_freeList = -1;

			if (capacity != 0)
			{
				m_buckets = new int[capacity];
				m_slots = new Slot[capacity];

				T[] array = (T[])m_siInfo.GetValue(ElementsName, typeof(T[]));

				if (array == null)
				{
					throw new SerializationException(SR.GetString(SR.Serialization_MissingKeys));
				}

				// there are no resizes here because we already set capacity above
				for (int i = 0; i < array.Length; i++)
				{
					AddIfNotPresent(array[i]);
				}
			}
			else
			{
				m_buckets = null;
			}

			m_version = m_siInfo.GetInt32(VersionName);
			m_siInfo = null;
		}

		#endregion

		#region HashSet methods

		/// <summary>
		/// Add item to this HashSet. Returns bool indicating whether item was added (won't be
		/// added if already present)
		/// </summary>
		/// <param name="item">
		/// <returns>true if added, false if already present</returns>
		public bool Add(T item)
		{
			return AddIfNotPresent(item);
		}

		/// <summary>
		/// Take the union of this HashSet with other. Modifies this set.
		///
		/// Implementation note: GetSuggestedCapacity (to increase capacity in advance avoiding
		/// multiple resizes ended up not being useful in practice; quickly gets to the
		/// point where it's a wasteful check.
		/// </summary>
		/// <param name="other">enumerable with items to add
		public void UnionWith(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}

			foreach (T item in other)
			{
				AddIfNotPresent(item);
			}
		}

		/// <summary>
		/// Takes the intersection of this set with other. Modifies this set.
		///
		/// Implementation Notes:
		/// We get better perf if other is a hashset using same equality comparer, because we
		/// get constant contains check in other. Resulting cost is O(n1) to iterate over this.
		///
		/// If we can't go above route, iterate over the other and mark intersection by checking
		/// contains in this. Then loop over and delete any unmarked elements. Total cost is n2+n1.
		///
		/// Attempts to return early based on counts alone, using the property that the
		/// intersection of anything with the empty set is the empty set.
		/// </summary>
		/// <param name="other">enumerable with items to add
		// <securitykernel critical="True" ring="1">
		// <referencescritical name="Method: IntersectWithEnumerable(IEnumerable`1<T>):Void" ring="1">
		// </referencescritical></securitykernel>
		[System.Security.SecurityCritical]
		public void IntersectWith(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}

			// intersection of anything with empty set is empty set, so return if count is 0
			if (m_count == 0)
			{
				return;
			}

			// if other is empty, intersection is empty set; remove all elements and we're done
			// can only figure this out if implements ICollection<t>. (IEnumerable<t> has no count)
			ICollection<T> otherAsCollection = other as ICollection<T>;
			if (otherAsCollection != null)
			{
				if (otherAsCollection.Count == 0)
				{
					Clear();
					return;
				}

				HashSet<T> otherAsSet = other as HashSet<T>;
				// faster if other is a hashset using same equality comparer; so check
				// that other is a hashset using the same equality comparer.
				if (otherAsSet != null && AreEqualityComparersEqual(this, otherAsSet))
				{
					IntersectWithHashSetWithSameEC(otherAsSet);
					return;
				}
			}

			IntersectWithEnumerable(other);
		}

		/// <summary>
		/// Remove items in other from this set. Modifies this set.
		/// </summary>
		/// <param name="other">enumerable with items to remove
		public void ExceptWith(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}
			// this is already the enpty set; return
			if (m_count == 0)
			{
				return;
			}

			// special case if other is this; a set minus itself is the empty set
			if (other == this)
			{
				Clear();
				return;
			}

			// remove every element in other from this
			foreach (T element in other)
			{
				Remove(element);
			}
		}

		/// <summary>
		/// Takes symmetric difference (XOR) with other and this set. Modifies this set.
		/// </summary>
		/// <param name="other">enumerable with items to XOR
		// <securitykernel critical="True" ring="1">
		// <referencescritical name="Method: SymmetricExceptWithEnumerable(IEnumerable`1<T>):Void" ring="1">
		// </referencescritical></securitykernel>
		[System.Security.SecurityCritical]
		public void SymmetricExceptWith(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}

			// if set is empty, then symmetric difference is other
			if (m_count == 0)
			{
				UnionWith(other);
				return;
			}

			// special case this; the symmetric difference of a set with itself is the empty set
			if (other == this)
			{
				Clear();
				return;
			}

			HashSet<T> otherAsSet = other as HashSet<T>;
			// If other is a HashSet, it has unique elements according to its equality comparer,
			// but if they're using different equality comparers, then assumption of uniqueness
			// will fail. So first check if other is a hashset using the same equality comparer;
			// symmetric except is a lot faster and avoids bit array allocations if we can assume
			// uniqueness
			if (otherAsSet != null && AreEqualityComparersEqual(this, otherAsSet))
			{
				SymmetricExceptWithUniqueHashSet(otherAsSet);
			}
			else
			{
				SymmetricExceptWithEnumerable(other);
			}
		}

		/// <summary>
		/// Checks if this is a subset of other.
		///
		/// Implementation Notes:
		/// The following properties are used up-front to avoid element-wise checks:
		/// 1. If this is the empty set, then it's a subset of anything, including the empty set
		/// 2. If other has unique elements according to this equality comparer, and this has more
		/// elements than other, then it can't be a subset.
		///
		/// Furthermore, if other is a hashset using the same equality comparer, we can use a
		/// faster element-wise check.
		/// </summary>
		/// <param name="other">
		/// <returns>true if this is a subset of other; false if not</returns>
		// <securitykernel critical="True" ring="1">
		// <referencescritical name="Method: CheckUniqueAndUnfoundElements(IEnumerable`1<T>, Boolean):ElementCount" ring="1">
		// </referencescritical></securitykernel>
		[System.Security.SecurityCritical]
		public bool IsSubsetOf(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}

			// The empty set is a subset of any set
			if (m_count == 0)
			{
				return true;
			}

			HashSet<T> otherAsSet = other as HashSet<T>;
			// faster if other has unique elements according to this equality comparer; so check
			// that other is a hashset using the same equality comparer.
			if (otherAsSet != null && AreEqualityComparersEqual(this, otherAsSet))
			{
				// if this has more elements then it can't be a subset
				if (m_count > otherAsSet.Count)
				{
					return false;
				}

				// already checked that we're using same equality comparer. simply check that
				// each element in this is contained in other.
				return IsSubsetOfHashSetWithSameEC(otherAsSet);
			}
			else
			{
				ElementCount result = CheckUniqueAndUnfoundElements(other, false);
				return (result.uniqueCount == m_count && result.unfoundCount >= 0);
			}
		}

		/// <summary>
		/// Checks if this is a proper subset of other (i.e. strictly contained in)
		///
		/// Implementation Notes:
		/// The following properties are used up-front to avoid element-wise checks:
		/// 1. If this is the empty set, then it's a proper subset of a set that contains at least
		/// one element, but it's not a proper subset of the empty set.
		/// 2. If other has unique elements according to this equality comparer, and this has >=
		/// the number of elements in other, then this can't be a proper subset.
		///
		/// Furthermore, if other is a hashset using the same equality comparer, we can use a
		/// faster element-wise check.
		/// </summary>
		/// <param name="other">
		/// <returns>true if this is a proper subset of other; false if not</returns>
		// <securitykernel critical="True" ring="1">
		// <referencescritical name="Method: CheckUniqueAndUnfoundElements(IEnumerable`1<T>, Boolean):ElementCount" ring="1">
		// </referencescritical></securitykernel>
		[System.Security.SecurityCritical]
		public bool IsProperSubsetOf(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}

			ICollection<T> otherAsCollection = other as ICollection<T>;
			if (otherAsCollection != null)
			{
				// the empty set is a proper subset of anything but the empty set
				if (m_count == 0)
				{
					return otherAsCollection.Count > 0;
				}
				HashSet<T> otherAsSet = other as HashSet<T>;
				// faster if other is a hashset (and we're using same equality comparer)
				if (otherAsSet != null && AreEqualityComparersEqual(this, otherAsSet))
				{
					if (m_count >= otherAsSet.Count)
					{
						return false;
					}
					// this has strictly less than number of items in other, so the following
					// check suffices for proper subset.
					return IsSubsetOfHashSetWithSameEC(otherAsSet);
				}
			}

			ElementCount result = CheckUniqueAndUnfoundElements(other, false);
			return (result.uniqueCount == m_count && result.unfoundCount > 0);

		}

		/// <summary>
		/// Checks if this is a superset of other
		///
		/// Implementation Notes:
		/// The following properties are used up-front to avoid element-wise checks:
		/// 1. If other has no elements (it's the empty set), then this is a superset, even if this
		/// is also the empty set.
		/// 2. If other has unique elements according to this equality comparer, and this has less
		/// than the number of elements in other, then this can't be a superset
		///
		/// </summary>
		/// <param name="other">
		/// <returns>true if this is a superset of other; false if not</returns>
		public bool IsSupersetOf(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}

			// try to fall out early based on counts
			ICollection<T> otherAsCollection = other as ICollection<T>;
			if (otherAsCollection != null)
			{
				// if other is the empty set then this is a superset
				if (otherAsCollection.Count == 0)
				{
					return true;
				}
				HashSet<T> otherAsSet = other as HashSet<T>;
				// try to compare based on counts alone if other is a hashset with
				// same equality comparer
				if (otherAsSet != null && AreEqualityComparersEqual(this, otherAsSet))
				{
					if (otherAsSet.Count > m_count)
					{
						return false;
					}
				}
			}

			return ContainsAllElements(other);
		}

		/// <summary>
		/// Checks if this is a proper superset of other (i.e. other strictly contained in this)
		///
		/// Implementation Notes:
		/// This is slightly more complicated than above because we have to keep track if there
		/// was at least one element not contained in other.
		///
		/// The following properties are used up-front to avoid element-wise checks:
		/// 1. If this is the empty set, then it can't be a proper superset of any set, even if
		/// other is the empty set.
		/// 2. If other is an empty set and this contains at least 1 element, then this is a proper
		/// superset.
		/// 3. If other has unique elements according to this equality comparer, and other's count
		/// is greater than or equal to this count, then this can't be a proper superset
		///
		/// Furthermore, if other has unique elements according to this equality comparer, we can
		/// use a faster element-wise check.
		/// </summary>
		/// <param name="other">
		/// <returns>true if this is a proper superset of other; false if not</returns>
		// <securitykernel critical="True" ring="1">
		// <referencescritical name="Method: CheckUniqueAndUnfoundElements(IEnumerable`1<T>, Boolean):ElementCount" ring="1">
		// </referencescritical></securitykernel>
		[System.Security.SecurityCritical]
		public bool IsProperSupersetOf(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}

			// the empty set isn't a proper superset of any set.
			if (m_count == 0)
			{
				return false;
			}

			ICollection<T> otherAsCollection = other as ICollection<T>;
			if (otherAsCollection != null)
			{
				// if other is the empty set then this is a superset
				if (otherAsCollection.Count == 0)
				{
					// note that this has at least one element, based on above check
					return true;
				}
				HashSet<T> otherAsSet = other as HashSet<T>;
				// faster if other is a hashset with the same equality comparer
				if (otherAsSet != null && AreEqualityComparersEqual(this, otherAsSet))
				{
					if (otherAsSet.Count >= m_count)
					{
						return false;
					}
					// now perform element check
					return ContainsAllElements(otherAsSet);
				}
			}
			// couldn't fall out in the above cases; do it the long way
			ElementCount result = CheckUniqueAndUnfoundElements(other, true);
			return (result.uniqueCount < m_count && result.unfoundCount == 0);

		}

		/// <summary>
		/// Checks if this set overlaps other (i.e. they share at least one item)
		/// </summary>
		/// <param name="other">
		/// <returns>true if these have at least one common element; false if disjoint</returns>
		public bool Overlaps(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}
			if (m_count == 0)
			{
				return false;
			}

			foreach (T element in other)
			{
				if (Contains(element))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Checks if this and other contain the same elements. This is set equality:
		/// duplicates and order are ignored
		/// </summary>
		/// <param name="other">
		/// <returns></returns>
		// <securitykernel critical="True" ring="1">
		// <referencescritical name="Method: CheckUniqueAndUnfoundElements(IEnumerable`1<T>, Boolean):ElementCount" ring="1">
		// </referencescritical></securitykernel>
		[System.Security.SecurityCritical]
		public bool SetEquals(IEnumerable<T> other)
		{
			if (other == null)
			{
				throw new ArgumentNullException("other");
			}

			HashSet<T> otherAsSet = other as HashSet<T>;
			// faster if other is a hashset and we're using same equality comparer
			if (otherAsSet != null && AreEqualityComparersEqual(this, otherAsSet))
			{
				// attempt to return early: since both contain unique elements, if they have
				// different counts, then they can't be equal
				if (m_count != otherAsSet.Count)
				{
					return false;
				}

				// already confirmed that the sets have the same number of distinct elements, so if
				// one is a superset of the other then they must be equal
				return ContainsAllElements(otherAsSet);
			}
			else
			{
				ICollection<T> otherAsCollection = other as ICollection<T>;
				if (otherAsCollection != null)
				{
					// if this count is 0 but other contains at least one element, they can't be equal
					if (m_count == 0 && otherAsCollection.Count > 0)
					{
						return false;
					}
				}
				ElementCount result = CheckUniqueAndUnfoundElements(other, true);
				return (result.uniqueCount == m_count && result.unfoundCount == 0);
			}
		}

		public void CopyTo(T[] array) { CopyTo(array, 0, m_count); }

		public void CopyTo(T[] array, int arrayIndex, int count)
		{
			if (array == null)
			{
				throw new ArgumentNullException("array");
			}

			// check array index valid index into array
			if (arrayIndex < 0)
			{
				throw new ArgumentOutOfRangeException("arrayIndex", SR.GetString(SR.ArgumentOutOfRange_NeedNonNegNum));
			}

			// also throw if count less than 0
			if (count < 0)
			{
				throw new ArgumentOutOfRangeException("count", SR.GetString(SR.ArgumentOutOfRange_NeedNonNegNum));
			}

			// will array, starting at arrayIndex, be able to hold elements? Note: not
			// checking arrayIndex >= array.Length (consistency with list of allowing
			// count of 0; subsequent check takes care of the rest)
			if (arrayIndex > array.Length || count > array.Length - arrayIndex)
			{
				throw new ArgumentException(SR.GetString(SR.Arg_ArrayPlusOffTooSmall));
			}

			int numCopied = 0;
			for (int i = 0; i < m_lastIndex && numCopied < count; i++)
			{
				if (m_slots[i].hashCode >= 0)
				{
					array[arrayIndex + numCopied] = m_slots[i].value;
					numCopied++;
				}
			}
		}

		/// <summary>
		/// Remove elements that match specified predicate. Returns the number of elements removed
		/// </summary>
		/// <param name="match">
		/// <returns></returns>
		public int RemoveWhere(Predicate<T> match)
		{
			if (match == null)
			{
				throw new ArgumentNullException("match");
			}

			int numRemoved = 0;
			for (int i = 0; i < m_lastIndex; i++)
			{
				if (m_slots[i].hashCode >= 0)
				{
					// cache value in case delegate removes it
					T value = m_slots[i].value;
					if (match(value))
					{
						// check again that remove actually removed it
						if (Remove(value))
						{
							numRemoved++;
						}
					}
				}
			}
			return numRemoved;
		}

		/// <summary>
		/// Gets the IEqualityComparer that is used to determine equality of keys for
		/// the HashSet.
		/// </summary>
		public IEqualityComparer<T> Comparer
		{
			get
			{
				return m_comparer;
			}
		}

		/// <summary>
		/// Sets the capacity of this list to the size of the list (rounded up to nearest prime),
		/// unless count is 0, in which case we release references.
		///
		/// This method can be used to minimize a list's memory overhead once it is known that no
		/// new elements will be added to the list. To completely clear a list and release all
		/// memory referenced by the list, execute the following statements:
		///
		/// list.Clear();
		/// list.TrimExcess();
		/// </summary>
		public void TrimExcess()
		{
			Debug.Assert(m_count >= 0, "m_count is negative");

			if (m_count == 0)
			{
				// if count is zero, clear references
				m_buckets = null;
				m_slots = null;
				m_version++;
			}
			else
			{
				Debug.Assert(m_buckets != null, "m_buckets was null but m_count > 0");

				// similar to IncreaseCapacity but moves down elements in case add/remove/etc
				// caused fragmentation
				int newSize = HashHelpers.GetPrime(m_count);
				Slot[] newSlots = new Slot[newSize];
				int[] newBuckets = new int[newSize];

				// move down slots and rehash at the same time. newIndex keeps track of current
				// position in newSlots array
				int newIndex = 0;
				for (int i = 0; i < m_lastIndex; i++)
				{
					if (m_slots[i].hashCode >= 0)
					{
						newSlots[newIndex] = m_slots[i];

						// rehash
						int bucket = newSlots[newIndex].hashCode % newSize;
						newSlots[newIndex].next = newBuckets[bucket] - 1;
						newBuckets[bucket] = newIndex + 1;

						newIndex++;
					}
				}

				Debug.Assert(newSlots.Length <= m_slots.Length, "capacity increased after TrimExcess");

				m_lastIndex = newIndex;
				m_slots = newSlots;
				m_buckets = newBuckets;
				m_freeList = -1;
			}
		}

		/// <summary>
		/// Used for deep equality of HashSet testing
		/// </summary>
		/// <returns></returns>
		public static IEqualityComparer<hashset<T>> CreateSetComparer()
		{
			return new HashSetEqualityComparer<T>();
		}

		#endregion

		#region Helper methods

		/// <summary>
		/// Initializes buckets and slots arrays. Uses suggested capacity by finding next prime
		/// greater than or equal to capacity.
		/// </summary>
		/// <param name="capacity">
		private void Initialize(int capacity)
		{
			Debug.Assert(m_buckets == null, "Initialize was called but m_buckets was non-null");

			int size = HashHelpers.GetPrime(capacity);

			m_buckets = new int[size];
			m_slots = new Slot[size];
		}

		/// <summary>
		/// Expand to new capacity. New capacity is next prime greater than or equal to suggested
		/// size. This is called when the underlying array is filled. This performs no
		/// defragmentation, allowing faster execution; note that this is reasonable since
		/// AddIfNotPresent attempts to insert new elements in re-opened spots.
		/// </summary>
		/// <param name="sizeSuggestion">
		private void IncreaseCapacity()
		{
			Debug.Assert(m_buckets != null, "IncreaseCapacity called on a set with no elements");

			// Handle overflow conditions. Try to expand capacity by GrowthFactor. If that causes
			// overflow, use size suggestion of m_count and see if HashHelpers returns a value
			// greater than that. If not, capacity can't be increased so throw capacity overflow
			// exception.
			int sizeSuggestion = unchecked(m_count * GrowthFactor);
			if (sizeSuggestion < 0)
			{
				sizeSuggestion = m_count;
			}
			int newSize = HashHelpers.GetPrime(sizeSuggestion);
			if (newSize <= m_count)
			{
				throw new ArgumentException(SR.GetString(SR.Arg_HSCapacityOverflow));
			}

			// Able to increase capacity; copy elements to larger array and rehash
			Slot[] newSlots = new Slot[newSize];
			if (m_slots != null)
			{
				Array.Copy(m_slots, 0, newSlots, 0, m_lastIndex);
			}

			int[] newBuckets = new int[newSize];
			for (int i = 0; i < m_lastIndex; i++)
			{
				int bucket = newSlots[i].hashCode % newSize;
				newSlots[i].next = newBuckets[bucket] - 1;
				newBuckets[bucket] = i + 1;
			}
			m_slots = newSlots;
			m_buckets = newBuckets;

		}

		/// <summary>
		/// Adds value to HashSet if not contained already
		/// Returns true if added and false if already present
		/// </summary>
		/// <param name="value">value to find
		/// <returns></returns>
		private bool AddIfNotPresent(T value)
		{
			if (m_buckets == null)
			{
				Initialize(0);
			}

			int hashCode = InternalGetHashCode(value);
			int bucket = hashCode % m_buckets.Length;
			for (int i = m_buckets[hashCode % m_buckets.Length] - 1; i >= 0; i = m_slots[i].next)
			{
				if (m_slots[i].hashCode == hashCode && m_comparer.Equals(m_slots[i].value, value))
				{
					return false;
				}
			}
			int index;
			if (m_freeList >= 0)
			{
				index = m_freeList;
				m_freeList = m_slots[index].next;
			}
			else
			{
				if (m_lastIndex == m_slots.Length)
				{
					IncreaseCapacity();
					// this will change during resize
					bucket = hashCode % m_buckets.Length;
				}
				index = m_lastIndex;
				m_lastIndex++;
			}
			m_slots[index].hashCode = hashCode;
			m_slots[index].value = value;
			m_slots[index].next = m_buckets[bucket] - 1;
			m_buckets[bucket] = index + 1;
			m_count++;
			m_version++;
			return true;
		}

		/// <summary>
		/// Checks if this contains of other's elements. Iterates over other's elements and
		/// returns false as soon as it finds an element in other that's not in this.
		/// Used by SupersetOf, ProperSupersetOf, and SetEquals.
		/// </summary>
		/// <param name="other">
		/// <returns></returns>
		private bool ContainsAllElements(IEnumerable<T> other)
		{
			foreach (T element in other)
			{
				if (!Contains(element))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Implementation Notes:
		/// If other is a hashset and is using same equality comparer, then checking subset is
		/// faster. Simply check that each element in this is in other.
		///
		/// Note: if other doesn't use same equality comparer, then Contains check is invalid,
		/// which is why callers must take are of this.
		///
		/// If callers are concerned about whether this is a proper subset, they take care of that.
		///
		/// </summary>
		/// <param name="other">
		/// <returns></returns>
		private bool IsSubsetOfHashSetWithSameEC(HashSet<T> other)
		{

			foreach (T item in this)
			{
				if (!other.Contains(item))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// If other is a hashset that uses same equality comparer, intersect is much faster
		/// because we can use other's Contains
		/// </summary>
		/// <param name="other">
		private void IntersectWithHashSetWithSameEC(HashSet<T> other)
		{
			for (int i = 0; i < m_lastIndex; i++)
			{
				if (m_slots[i].hashCode >= 0)
				{
					T item = m_slots[i].value;
					if (!other.Contains(item))
					{
						Remove(item);
					}
				}
			}
		}

		/// <summary>
		/// Iterate over other. If contained in this, mark an element in bit array corresponding to
		/// its position in m_slots. If anything is unmarked (in bit array), remove it.
		///
		/// This attempts to allocate on the stack, if below StackAllocThreshold.
		/// </summary>
		/// <param name="other">
		// <securitykernel critical="True" ring="0">
		// <usesunsafecode name="Local bitArrayPtr of type: Int32*">
		// <referencescritical name="Method: BitHelper..ctor(System.Int32*,System.Int32)" ring="1">
		// <referencescritical name="Method: BitHelper.MarkBit(System.Int32):System.Void" ring="1">
		// <referencescritical name="Method: BitHelper.IsMarked(System.Int32):System.Boolean" ring="1">
		// </referencescritical></referencescritical></referencescritical></usesunsafecode></securitykernel>
		[System.Security.SecurityCritical]
		private unsafe void IntersectWithEnumerable(IEnumerable<T> other)
		{
			Debug.Assert(m_buckets != null, "m_buckets shouldn't be null; callers should check first");

			// keep track of current last index; don't want to move past the end of our bit array
			// (could happen if another thread is modifying the collection)
			int originalLastIndex = m_lastIndex;
			int intArrayLength = BitHelper.ToIntArrayLength(originalLastIndex);

			BitHelper bitHelper;
			if (intArrayLength <= StackAllocThreshold)
			{
				int* bitArrayPtr = stackalloc int[intArrayLength];
				bitHelper = new BitHelper(bitArrayPtr, intArrayLength);
			}
			else
			{
				int[] bitArray = new int[intArrayLength];
				bitHelper = new BitHelper(bitArray, intArrayLength);
			}

			// mark if contains: find index of in slots array and mark corresponding element in bit array
			foreach (T item in other)
			{
				int index = InternalIndexOf(item);
				if (index >= 0)
				{
					bitHelper.MarkBit(index);
				}
			}

			// if anything unmarked, remove it. Perf can be optimized here if BitHelper had a
			// FindFirstUnmarked method.
			for (int i = 0; i < originalLastIndex; i++)
			{
				if (m_slots[i].hashCode >= 0 && !bitHelper.IsMarked(i))
				{
					Remove(m_slots[i].value);
				}
			}
		}

		/// <summary>
		/// Used internally by set operations which have to rely on bit array marking. This is like
		/// Contains but returns index in slots array.
		/// </summary>
		/// <param name="item">
		/// <returns></returns>
		private int InternalIndexOf(T item)
		{
			Debug.Assert(m_buckets != null, "m_buckets was null; callers should check first");

			int hashCode = InternalGetHashCode(item);
			for (int i = m_buckets[hashCode % m_buckets.Length] - 1; i >= 0; i = m_slots[i].next)
			{
				if ((m_slots[i].hashCode) == hashCode && m_comparer.Equals(m_slots[i].value, item))
				{
					return i;
				}
			}
			// wasn't found
			return -1;
		}

		/// <summary>
		/// if other is a set, we can assume it doesn't have duplicate elements, so use this
		/// technique: if can't remove, then it wasn't present in this set, so add.
		///
		/// As with other methods, callers take care of ensuring that other is a hashset using the
		/// same equality comparer.
		/// </summary>
		/// <param name="other">
		private void SymmetricExceptWithUniqueHashSet(HashSet<T> other)
		{
			foreach (T item in other)
			{
				if (!Remove(item))
				{
					AddIfNotPresent(item);
				}
			}
		}

		/// <summary>
		/// Implementation notes:
		///
		/// Used for symmetric except when other isn't a HashSet. This is more tedious because
		/// other may contain duplicates. HashSet technique could fail in these situations:
		/// 1. Other has a duplicate that's not in this: HashSet technique would add then
		/// remove it.
		/// 2. Other has a duplicate that's in this: HashSet technique would remove then add it
		/// back.
		/// In general, its presence would be toggled each time it appears in other.
		///
		/// This technique uses bit marking to indicate whether to add/remove the item. If already
		/// present in collection, it will get marked for deletion. If added from other, it will
		/// get marked as something not to remove.
		///
		/// </summary>
		/// <param name="other">
		// <securitykernel critical="True" ring="0">
		// <usesunsafecode name="Local itemsToRemovePtr of type: Int32*">
		// <usesunsafecode name="Local itemsAddedFromOtherPtr of type: Int32*">
		// <referencescritical name="Method: BitHelper..ctor(System.Int32*,System.Int32)" ring="1">
		// <referencescritical name="Method: BitHelper.MarkBit(System.Int32):System.Void" ring="1">
		// <referencescritical name="Method: BitHelper.IsMarked(System.Int32):System.Boolean" ring="1">
		// </referencescritical></referencescritical></referencescritical></usesunsafecode></usesunsafecode></securitykernel>
		[System.Security.SecurityCritical]
		private unsafe void SymmetricExceptWithEnumerable(IEnumerable<T> other)
		{
			int originalLastIndex = m_lastIndex;
			int intArrayLength = BitHelper.ToIntArrayLength(originalLastIndex);

			BitHelper itemsToRemove;
			BitHelper itemsAddedFromOther;
			if (intArrayLength <= StackAllocThreshold / 2)
			{
				int* itemsToRemovePtr = stackalloc int[intArrayLength];
				itemsToRemove = new BitHelper(itemsToRemovePtr, intArrayLength);

				int* itemsAddedFromOtherPtr = stackalloc int[intArrayLength];
				itemsAddedFromOther = new BitHelper(itemsAddedFromOtherPtr, intArrayLength);
			}
			else
			{
				int[] itemsToRemoveArray = new int[intArrayLength];
				itemsToRemove = new BitHelper(itemsToRemoveArray, intArrayLength);

				int[] itemsAddedFromOtherArray = new int[intArrayLength];
				itemsAddedFromOther = new BitHelper(itemsAddedFromOtherArray, intArrayLength);
			}

			foreach (T item in other)
			{
				int location = 0;
				bool added = AddOrGetLocation(item, out location);
				if (added)
				{
					// wasn't already present in collection; flag it as something not to remove
					// *NOTE* if location is out of range, we should ignore. BitHelper will
					// detect that it's out of bounds and not try to mark it. But it's
					// expected that location could be out of bounds because adding the item
					// will increase m_lastIndex as soon as all the free spots are filled.
					itemsAddedFromOther.MarkBit(location);
				}
				else
				{
					// already there...if not added from other, mark for remove.
					// *NOTE* Even though BitHelper will check that location is in range, we want
					// to check here. There's no point in checking items beyond originalLastIndex
					// because they could not have been in the original collection
					if (location < originalLastIndex && !itemsAddedFromOther.IsMarked(location))
					{
						itemsToRemove.MarkBit(location);
					}
				}
			}

			// if anything marked, remove it
			for (int i = 0; i < originalLastIndex; i++)
			{
				if (itemsToRemove.IsMarked(i))
				{
					Remove(m_slots[i].value);
				}
			}
		}

		/// <summary>
		/// Add if not already in hashset. Returns an out param indicating index where added. This
		/// is used by SymmetricExcept because it needs to know the following things:
		/// - whether the item was already present in the collection or added from other
		/// - where it's located (if already present, it will get marked for removal, otherwise
		/// marked for keeping)
		/// </summary>
		/// <param name="value">
		/// <param name="location">
		/// <returns></returns>
		private bool AddOrGetLocation(T value, out int location)
		{
			Debug.Assert(m_buckets != null, "m_buckets is null, callers should have checked");

			int hashCode = InternalGetHashCode(value);
			int bucket = hashCode % m_buckets.Length;
			for (int i = m_buckets[hashCode % m_buckets.Length] - 1; i >= 0; i = m_slots[i].next)
			{
				if (m_slots[i].hashCode == hashCode && m_comparer.Equals(m_slots[i].value, value))
				{
					location = i;
					return false;   //already present
				}
			}
			int index;
			if (m_freeList >= 0)
			{
				index = m_freeList;
				m_freeList = m_slots[index].next;
			}
			else
			{
				if (m_lastIndex == m_slots.Length)
				{
					IncreaseCapacity();
					// this will change during resize
					bucket = hashCode % m_buckets.Length;
				}
				index = m_lastIndex;
				m_lastIndex++;
			}
			m_slots[index].hashCode = hashCode;
			m_slots[index].value = value;
			m_slots[index].next = m_buckets[bucket] - 1;
			m_buckets[bucket] = index + 1;
			m_count++;
			m_version++;
			location = index;
			return true;
		}

		/// <summary>
		/// Determines counts that can be used to determine equality, subset, and superset. This
		/// is only used when other is an IEnumerable and not a HashSet. If other is a HashSet
		/// these properties can be checked faster without use of marking because we can assume
		/// other has no duplicates.
		///
		/// The following count checks are performed by callers:
		/// 1. Equals: checks if unfoundCount = 0 and uniqueFoundCount = m_count; i.e. everything
		/// in other is in this and everything in this is in other
		/// 2. Subset: checks if unfoundCount >= 0 and uniqueFoundCount = m_count; i.e. other may
		/// have elements not in this and everything in this is in other
		/// 3. Proper subset: checks if unfoundCount > 0 and uniqueFoundCount = m_count; i.e
		/// other must have at least one element not in this and everything in this is in other
		/// 4. Proper superset: checks if unfound count = 0 and uniqueFoundCount strictly less
		/// than m_count; i.e. everything in other was in this and this had at least one element
		/// not contained in other.
		///
		/// An earlier implementation used delegates to perform these checks rather than returning
		/// an ElementCount struct; however this was changed due to the perf overhead of delegates.
		/// </summary>
		/// <param name="other">
		/// <param name="returnIfUnfound">Allows us to finish faster for equals and proper superset
		/// because unfoundCount must be 0.
		/// <returns></returns>
		// <securitykernel critical="True" ring="0">
		// <usesunsafecode name="Local bitArrayPtr of type: Int32*">
		// <referencescritical name="Method: BitHelper..ctor(System.Int32*,System.Int32)" ring="1">
		// <referencescritical name="Method: BitHelper.IsMarked(System.Int32):System.Boolean" ring="1">
		// <referencescritical name="Method: BitHelper.MarkBit(System.Int32):System.Void" ring="1">
		// </referencescritical></referencescritical></referencescritical></usesunsafecode></securitykernel>
		[System.Security.SecurityCritical]
		private unsafe ElementCount CheckUniqueAndUnfoundElements(IEnumerable<T> other, bool returnIfUnfound)
		{
			ElementCount result;

			// need special case in case this has no elements.
			if (m_count == 0)
			{
				int numElementsInOther = 0;
				foreach (T item in other)
				{
					numElementsInOther++;
					// break right away, all we want to know is whether other has 0 or 1 elements
					break;
				}
				result.uniqueCount = 0;
				result.unfoundCount = numElementsInOther;
				return result;
			}


			Debug.Assert((m_buckets != null) && (m_count > 0), "m_buckets was null but count greater than 0");

			int originalLastIndex = m_lastIndex;
			int intArrayLength = BitHelper.ToIntArrayLength(originalLastIndex);

			BitHelper bitHelper;
			if (intArrayLength <= StackAllocThreshold)
			{
				int* bitArrayPtr = stackalloc int[intArrayLength];
				bitHelper = new BitHelper(bitArrayPtr, intArrayLength);
			}
			else
			{
				int[] bitArray = new int[intArrayLength];
				bitHelper = new BitHelper(bitArray, intArrayLength);
			}

			// count of items in other not found in this
			int unfoundCount = 0;
			// count of unique items in other found in this
			int uniqueFoundCount = 0;

			foreach (T item in other)
			{
				int index = InternalIndexOf(item);
				if (index >= 0)
				{
					if (!bitHelper.IsMarked(index))
					{
						// item hasn't been seen yet
						bitHelper.MarkBit(index);
						uniqueFoundCount++;
					}
				}
				else
				{
					unfoundCount++;
					if (returnIfUnfound)
					{
						break;
					}
				}
			}

			result.uniqueCount = uniqueFoundCount;
			result.unfoundCount = unfoundCount;
			return result;
		}

		/// <summary>
		/// Copies this to an array. Used for DebugView
		/// </summary>
		/// <returns></returns>
		internal T[] ToArray()
		{
			T[] newArray = new T[Count];
			CopyTo(newArray);
			return newArray;
		}

		/// <summary>
		/// Internal method used for HashSetEqualityComparer. Compares set1 and set2 according
		/// to specified comparer.
		///
		/// Because items are hashed according to a specific equality comparer, we have to resort
		/// to n^2 search if they're using different equality comparers.
		/// </summary>
		/// <param name="set1">
		/// <param name="set2">
		/// <param name="comparer">
		/// <returns></returns>
		internal static bool HashSetEquals(HashSet<T> set1, HashSet<T> set2, IEqualityComparer<T> comparer)
		{
			// handle null cases first
			if (set1 == null)
			{
				return (set2 == null);
			}
			else if (set2 == null)
			{
				// set1 != null
				return false;
			}

			// all comparers are the same; this is faster
			if (AreEqualityComparersEqual(set1, set2))
			{
				if (set1.Count != set2.Count)
				{
					return false;
				}
				// suffices to check subset
				foreach (T item in set2)
				{
					if (!set1.Contains(item))
					{
						return false;
					}
				}
				return true;
			}
			else
			{	// n^2 search because items are hashed according to their respective ECs
				foreach (T set2Item in set2)
				{
					bool found = false;
					foreach (T set1Item in set1)
					{
						if (comparer.Equals(set2Item, set1Item))
						{
							found = true;
							break;
						}
					}
					if (!found)
					{
						return false;
					}
				}
				return true;
			}
		}

		/// <summary>
		/// Checks if equality comparers are equal. This is used for algorithms that can
		/// speed up if it knows the other item has unique elements. I.e. if they're using
		/// different equality comparers, then uniqueness assumption between sets break.
		/// </summary>
		/// <param name="set1">
		/// <param name="set2">
		/// <returns></returns>
		private static bool AreEqualityComparersEqual(HashSet<T> set1, HashSet<T> set2)
		{
			return set1.Comparer.Equals(set2.Comparer);
		}

		/// <summary>
		/// Workaround Comparers that throw ArgumentNullException for GetHashCode(null).
		/// </summary>
		/// <param name="item">
		/// <returns>hash code</returns>
		private int InternalGetHashCode(T item)
		{
			if (item == null)
			{
				return 0;
			}
			return m_comparer.GetHashCode(item) & Lower31BitMask;
		}

		#endregion

		// used for set checking operations (using enumerables) that rely on counting
		internal struct ElementCount
		{
			internal int uniqueCount;
			internal int unfoundCount;
		}

		internal struct Slot
		{
			internal int hashCode;		// Lower 31 bits of hash code, -1 if unused
			internal T value;
			internal int next;			// Index of next entry, -1 if last
		}

		[Serializable()]
		[System.Security.Permissions.HostProtection(MayLeakOnAbort = true)]
		public struct Enumerator : IEnumerator<T>, System.Collections.IEnumerator
		{
			private HashSet<T> set;
			private int index;
			private int version;
			private T current;

			internal Enumerator(HashSet<T> set)
			{
				this.set = set;
				index = 0;
				version = set.m_version;
				current = default(T);
			}

			public void Dispose()
			{
			}

			public bool MoveNext()
			{
				if (version != set.m_version)
				{
					throw new InvalidOperationException(SR.GetString(SR.InvalidOperation_EnumFailedVersion));
				}

				while (index < set.m_lastIndex)
				{
					if (set.m_slots[index].hashCode >= 0)
					{
						current = set.m_slots[index].value;
						index++;
						return true;
					}
					index++;
				}
				index = set.m_lastIndex + 1;
				current = default(T);
				return false;
			}

			public T Current
			{
				get
				{
					return current;
				}
			}

			Object System.Collections.IEnumerator.Current
			{
				get
				{
					if (index == 0 || index == set.m_lastIndex + 1)
					{
						throw new InvalidOperationException(SR.GetString(SR.InvalidOperation_EnumOpCantHappen));
					}
					return Current;
				}
			}

			void System.Collections.IEnumerator.Reset()
			{
				if (version != set.m_version)
				{
					throw new InvalidOperationException(SR.GetString(SR.InvalidOperation_EnumFailedVersion));
				}

				index = 0;
				current = default(T);
			}
		}
	}

}