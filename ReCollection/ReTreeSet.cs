using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public class ReTreeSet<T> : ReSet<T>
		where T : IComparable<T>
	{
		internal class Node
		{
			public Node parent;
			public Node less;
			public Node more;

			public T item;

			public Node(T stuff)
			{
				item = stuff;
			}

			public int CountFromThis()
			{
				int res = 1;
				if (less != null)
				{
					res += CountFromThis();
				}
				if (more != null)
				{
					res += CountFromThis();
				}

				return res;
			}

			public void Add(T added)
			{
				AddNode(new Node(added));
			}

			public bool AddNode(Node node)
			{
				if (node == null)
				{
					return false;
				}

				if (node.item.CompareTo(item) > 0)
				{
					if (more == null)
					{
						more = node;
						more.parent = this;
						return true;
					}

					more.AddNode(node);
					return true;
				}
				else if (node.item.CompareTo(item) < 0)
				{
					if (less == null)
					{
						less = node;
						less.parent = this;
						return true;
					}

					less.AddNode(node);
					return true;
				}
				else // already exists, and it's this.
				{
					return false;
				}
			}

			public Node GetNode(T thing)
			{
				if (item.CompareTo(thing) > 0)
				{
					if (more == null)
					{
						return null;
					}

					return more.GetNode(item);
				}
				else if (item.CompareTo(thing) < 0)
				{
					if (less == null)
					{
						return null;
					}

					return less.GetNode(thing);
				}
				else
				{
					return this;
				}
			}

			public bool Exists(RePredicate<T> pred)
			{
				if (pred(item))
				{
					return true;
				}

				if (less == null && more == null)
				{
					return false; // end of line
				}

				bool res = false;
				if (less != null)
				{
					res |= less.Exists(pred);
				}
				if (more != null)
				{
					res |= more.Exists(pred);
				}
				return res;
			}

			public Node Next()
			{
				// Going down (all the way)
				if (more != null)
				{
					Node n = more;

					while (n.less != null)
					{
						n = n.less;
					}

					return n;
				}

				// Going up
				Node p = parent;
				Node ch = this;

				while (p != null && ch == p.more)
				{
					ch = p;
					p = p.parent;
				}

				return p;
			}
			public Node Previous()
			{
				// Going down (all the way)
				if (less != null)
				{
					Node n = less;

					while (n.more != null)
					{
						n = n.more;
					}

					return n;
				}

				// Going up
				Node p = parent;
				Node ch = this;

				while (p != null && ch == p.less)
				{
					ch = p;
					p = p.parent;
				}

				return p;
			}

			public override string ToString()
			{
				return "Node: " + item.ToString();
			}
		}

		public class Iterator : IReIterator<T>
		{
			Node node;

			public bool HasNext
			{
				get
				{
					return node.Next() != null;
				}
			}

			public bool HasPrevious
			{
				get
				{
					return node.Previous() != null;
				}
			}

			public T Next
			{
				get
				{
					if (HasNext)
					{
						node = node.Next();
					}

					return node.item;
				}
			}

			public T Previous
			{
				get
				{
					if (HasPrevious)
					{
						node = node.Previous();
					}

					return node.item;
				}
			}

			internal Iterator(Node start)
			{
				node = start;
			}

			public void Insert(T item)
			{
				Node root = node;
				while (root.parent != null)
				{
					root = root.parent;
				}

				root.Add(item);
			}

			public T Peek()
			{
				return node.item;
			}

			public void Remove()
			{
				Node less = node.less;
				Node more = node.more;

				if (node == null)
				{
					return;
				}

				if (node.parent.less == node)
				{
					node.parent.less = null;
				}
				else if (node.parent.more == node)
				{
					node.parent.more = null;
				}

				node.parent.AddNode(less);
				node.parent.AddNode(more);
				return;
			}

			public void Skip()
			{
				if (HasNext)
				{
					node = node.Next();
				}
			}

			public void SkipBack()
			{
				if (HasPrevious)
				{
					node = node.Previous();
				}
			}

			public override string ToString()
			{
				return "Iterator: " + node.item.ToString();
			}
		}

		Node root;

		public override int Count
		{
			get
			{
				if (root == null)
				{
					return 0;
				}

				return root.CountFromThis();
			}
		}

		public T Lowest
		{
			get
			{
				Node n = LowestNode;
				if (n == null)
				{
					return default(T);
				}

				return n.item;
			}
		}
		private Node LowestNode
		{
			get
			{
				Node n = root;

				if (n == null)
				{
					return null;
				}

				while (n.less != null)
				{
					n = n.less;
				}

				return n;
			}
		}

		public T Highest
		{
			get
			{
				Node n = HighestNode;

				if (n == null)
				{
					return default(T);
				}

				return n.item;
			}
		}
		private Node HighestNode
		{
			get
			{
				Node n = root;

				if (n == null)
				{
					return null;
				}

				while (n.more != null)
				{
					n = n.more;
				}

				return n;
			}
		}

		public ReComparison<T> Comparator
		{ get; set; }

		public ReTreeSet() : base()
		{
			root = null;
		}

		private Node GetNode(T item)
		{
			return root.GetNode(item);
		}

		public override IReSet<T> MakeNewSet()
		{
			return new ReTreeSet<T>();
		}

		public override void Add(T added)
		{
			if (isIterating)
			{
				throw new InvalidOperationException("Cannot add item while iterating.");
			}

			if (root == null)
			{
				root = new Node(added);
				return;
			}

			root.Add(added);
		}

		public override bool Remove(T removed)
		{
			if (isIterating)
			{
				throw new InvalidOperationException("Cannot remove item while iterating.");
			}

			Node rem = GetNode(removed);
			if (rem == null)
			{
				return false;
			}

			Node less = rem.less;
			Node more = rem.more;

			if (rem.parent.less == rem)
			{
				rem.parent.less = null;
			}
			else if (rem.parent.more == rem)
			{
				rem.parent.more = null;
			}

			rem.parent.AddNode(less);
			rem.parent.AddNode(more);
			return true;
		}

		public override void RemoveAll(RePredicate<T> pred)
		{
			while (Exists(pred))
			{
				T next = Find(pred);
				Remove(next);
			}
		}

		public override void Clear()
		{
			root = null;
		}

		public override bool Contains(T item)
		{
			return GetNode(item) != null;
		}

		public override bool Exists(RePredicate<T> pred)
		{
			return root.Exists(pred);
		}

		public override void Foreach(ReAction<T> todo)
		{
			isIterating = true;
			Node n = LowestNode;

			if (n == null)
			{
				return;
			}

			IReIterator<T> iter = new Iterator(n);
			T t = iter.Peek();
			while (iter.HasNext)
			{
				todo(t);
				t = iter.Next;
			}
			todo(t);

			isIterating = false;
		}

		public override IReIterator<T> MakeIterator()
		{
			return new Iterator(LowestNode);
		}

		public override IReIterator<T> MakeIteratorEnd()
		{
			return new Iterator(HighestNode);
		}
	}
}
