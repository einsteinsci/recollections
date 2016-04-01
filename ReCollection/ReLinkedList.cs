using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public class ReLinkedList<T> : ReList<T>, 
		IReList<T>, IReCollection<T>, IReIterable<T>
	{
		internal sealed class Node
		{
			public T item;

			public Node next;
			public Node prev;

			public Node(T stuff)
			{
				item = stuff;
				next = null;
				prev = null;
			}

			public override string ToString()
			{
				return "Node: " + item.ToString();
			}
		}

		public sealed class Iterator : IReIterator<T>
		{
			internal Node currentNode;

			internal Iterator(Node node)
			{
				currentNode = node;
			}

			public bool HasNext
			{
				get
				{
					return currentNode.next != null;
				}
			}
			public bool HasPrevious
			{
				get
				{
					return currentNode.prev != null;
				}
			}

			// Returns current if there is no next node
			public T Next
			{
				get
				{
					if (HasNext)
					{
						currentNode = currentNode.next;
					}
	
					return currentNode.item;
				}
			}
			// Returns current if there is no previous node
			public T Previous
			{
				get
				{
					if (HasPrevious)
					{
						currentNode = currentNode.prev;
					}

					return currentNode.item;
				}
			}

			// Look at current value without advancing
			public T Peek()
			{
				return currentNode.item;
			}

			public void Skip()
			{
				if (HasNext)
				{
					currentNode = currentNode.next;
				}
			}
			public void SkipBack()
			{
				if (HasPrevious)
				{
					currentNode = currentNode.prev;
				}
			}

			public void Insert(T item)
			{
				Node added = new Node(item);

				Node prev = currentNode.prev;
				Node next = currentNode.next;

				added.prev = prev;
				added.next = next;

				if (prev != null)
				{
					prev.next = added;
				}
				if (next != null)
				{
					next.prev = added;
				}
			}
			public void Remove()
			{
				Node prev = currentNode.prev;
				Node next = currentNode.next;

				if (prev != null)
				{
					prev.next = next;
				}
				if (next != null)
				{
					next.prev = prev;
				}

				currentNode.next = null;
				currentNode.prev = null;
				currentNode = next;
			}
		}

		private Node start;
		private Node end;

		// PROPERTIES
		public override int Count
		{
			get
			{
				Node node = start;
				int i = 0;
				while (node != null)
				{
					node = node.next;
					i++;
				}

				return i;
			}
		}

		// CONSTRUCTOR
		public ReLinkedList() : base()
		{
			start = null;
			end = null;
		}

		public override IReList<T> MakeNewList()
		{
			return new ReLinkedList<T>();
		}

		private Node GetNode(int id)
		{
			Iterator iter = MakeIterator() as Iterator;
			int i = 0;
			while (iter.HasNext)
			{
				if (i == id)
				{
					return iter.currentNode;
				}

				iter.Skip();
				i++;
			}

			// last
			if (i == id)
			{
				return iter.currentNode;
			}

			return null;
		}

		public override IReIterator<T> MakeIterator()
		{
			return new Iterator(start);
		}
		public override IReIterator<T> MakeIteratorEnd()
		{
			return new Iterator(end);
		}

		public override void Add(T item)
		{
			Node added = new Node(item);

			if (Count == 0)
			{
				start = added;
				end = added;
			}
			else
			{
				end.next = added;
				added.prev = end;
				end = added;
			}
		}
		public override void Insert(int index, T item)
		{
			Node added = new Node(item);

			if (Count == 0)
			{
				start = added;
				end = added;
				return;
			}

			if (index < 0 || index > Count)
			{
				throw new ArgumentOutOfRangeException("Cannot insert outside of valid id's.");
			}

			Node prev = GetNode(index - 1);
			Node next = GetNode(index);

			if (prev == null)  // first
			{
				added.next = start;
				start.prev = added;
				start = added;
			}
			else if (next == null)   // last (add)
			{
				end.next = added;
				added.prev = end;
				end = added;
			}
			else // middle
			{
				prev.next = added;
				added.prev = prev;
				added.next = next;
				next.prev = added;
			}
		}
		public override bool RemoveAt(int index)
		{
			if (isIterating)
			{
				throw new InvalidOperationException("Cannot remove items while iterating through them.");
			}

			Node rem = GetNode(index);

			if (rem == null)
			{
				return false;
			}

			Node prev = rem.prev;
			Node next = rem.next;

			// Stitch it up
			if (Count == 1)	// only
			{
				start = null;
				end = null;
			}
			else if (prev == null)	 // start
			{
				start = next;
				next.prev = null;
			}
			else if (next == null)	 // end
			{
				end = prev;
				prev.next = null;
			}

			rem.next = null;
			rem.prev = null;
			return true;
		}

		public override void Clear()
		{
			start = null;
			end = null;
		}

		public override void Set(int id, T item)
		{
			GetNode(id).item = item;
		}
		public override T Get(int id)
		{
			return GetNode(id).item;
		}
	}
}
