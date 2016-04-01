using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	public class ReHashSet<T> : ReSet<T>
	{
		const int MAX_BLOCK_SIZE = ushort.MaxValue;
		const int BLOCK_COUNT = int.MaxValue / MAX_BLOCK_SIZE;

		internal sealed class Node
		{
			public Node next;
			public Node prev;
			public T item;

			public Node(T thing)
			{
				item = thing;
			}

			public void Add(Node added)
			{
				Node n = this;
				while (n.next != null)
				{
					if (n.item.Equals(added.item))
					{
						return; // already added
					}

					n = n.next;
				}

				added.prev = n;
				n.next = added;
			}

			public override string ToString()
			{
				return "Node: " + item;
			}
		}

		internal sealed class Block
		{
			public Node this[int i]
			{
				get
				{
					return nodes[i];
				}
				set
				{
					nodes[i] = value;
				}
			}

			public Node[] nodes;
			public ReArrayList<ushort> locations;
			
			public Block()
			{
				nodes = new Node[MAX_BLOCK_SIZE];
				locations = new ReArrayList<ushort>();
			}
		}

		internal struct BlockLoc
		{
			internal ushort Block;
			internal ushort Within;

			internal BlockLoc(ushort block, ushort within)
			{
				Block = block;
				Within = within;
			}

			public override int GetHashCode()
			{
				int b = Block;
				int w = Within;

				return (b * MAX_BLOCK_SIZE) + w;
			}

			public override bool Equals(object obj)
			{
				if (!(obj is BlockLoc))
				{
					return false;
				}

				BlockLoc other = (BlockLoc)obj;
				return this.Block == other.Block && this.Within == other.Within;
			}

			public override string ToString()
			{
				return "Block " + Block + ", Node " + Within;
			}
		}

		public sealed class Iterator : IReIterator<T>
		{
			ReHashSet<T> set;

			BlockLoc loc;
			int depth;

			Node currentNode;

			public T Next
			{
				get
				{
					if (HasNext)
					{
						Skip();
					}

					return currentNode.item;
				}
			}
			public T Previous
			{
				get
				{
					if (HasPrevious)
					{
						SkipBack();
					}

					return currentNode.item;
				}
			}

			public bool HasNext
			{
				get
				{
					if (currentNode.next != null)
					{
						return true;
					}

					int currentHash = loc.GetHashCode(); // converts back to hash
					int currentHashId = set.hashes.IndexOf(currentHash);
					if (currentHashId >= set.hashes.Count - 1) // last
					{
						return false;
					}

					return true;
				}
			}
			public bool HasPrevious
			{
				get
				{
					if (currentNode.prev != null)
					{
						return true;
					}

					int currentHash = loc.GetHashCode();
					int currentHashId = set.hashes.IndexOf(currentHash);
					if (currentHashId == 0)	 // first
					{
						return false;
					}

					return true;
				}
			}

			public Iterator(int hash, int _depth, ReHashSet<T> _set)
			{
				loc = GetLocation(hash);
				depth = _depth;

				set = _set;
				currentNode = set.GetNodeOf(loc);
			}

			public T Peek()
			{
				return currentNode.item;
			}

			public void Remove()
			{
				Node rem = currentNode;
				Skip();
				if (currentNode == rem)
				{
					SkipBack();
				}

				set.Remove(rem.item);
			}
			public void Insert(T item)
			{
				set.Add(item);
			}

			public void Skip()
			{
				if (currentNode.next != null)
				{
					currentNode = currentNode.next;
					depth++;
					return;
				}
				int curHash = loc.GetHashCode();

				int curHashId = set.hashes.IndexOf(curHash);
				if (curHashId == set.hashes.Count - 1)	 // last
				{
					return;
				}

				int nextHash = set.hashes[curHashId + 1];
				loc = GetLocation(nextHash);
				currentNode = set.GetNodeOf(loc);
				depth = 0;
			}
			public void SkipBack()
			{
				if (currentNode.prev != null)
				{
					currentNode = currentNode.prev;
					depth--;
					return;
				}
				int curHash = loc.GetHashCode();

				int curHashId = set.hashes.IndexOf(curHash);
				if (curHashId == 0)	 // first
				{
					return;
				}

				int prevHash = set.hashes[curHashId - 1];
				loc = GetLocation(prevHash);
				currentNode = set.GetNodeOf(loc);
				depth = 0;
			}
		}

		Block[] blocks;
		ReArrayList<int> hashes;

		public override int Count
		{
			get
			{
				return hashes.Count;
			}
		}

		public ReHashSet() : base()
		{
			blocks = new Block[BLOCK_COUNT];
			hashes = new ReArrayList<int>();
		}

		static BlockLoc GetLocation(T item)
		{
			int hash = (item == null ? 0 : item.GetHashCode());

			return GetLocation(hash);
		}
		static BlockLoc GetLocation(int hash)
		{
			ushort block = (ushort)(hash / MAX_BLOCK_SIZE);
			ushort loc = (ushort)(hash % MAX_BLOCK_SIZE);

			return new BlockLoc(block, loc);
		}

		public override void Add(T added)
		{
			int hash = added.GetHashCode();
			BlockLoc loc = GetLocation(added);

			if (hashes.Contains(hash))
			{
				Node n = new Node(added);
				Block block = blocks[loc.Block];
				block[loc.Within].Add(n);
				return;
			}
			else
			{
				hashes.Add(hash);

				Node addedNode = new Node(added);

				Block block = blocks[loc.Block];

				if (block == null) // new block
				{
					blocks[loc.Block] = new Block();
					block = blocks[loc.Block];
				}

				block[loc.Within] = addedNode;
				block.locations.Add(loc.Within);
			}

			hashes.Sort(Util.DefComparerInt);
		}

		public override void Clear()
		{
			blocks = new Block[BLOCK_COUNT];
			hashes = new ReArrayList<int>();
		}

		private Node GetNodeOf(BlockLoc loc)
		{
			Block b = blocks[loc.Block];
			if (b == null)
			{
				return null;
			}

			return b[loc.Within];
		}

		public override bool Contains(T item)
		{
			int hash = item.GetHashCode();
			BlockLoc loc = GetLocation(item);

			Block block = blocks[loc.Block];
			if (block == null)
			{
				return false;
			}

			Node n = block[loc.Within];

			if (n == null)
			{
				return false;
			}

			while (n.next != null)
			{
				if (item.Equals(n.item))
				{
					return true;
				}

				n = n.next;
			}

			return item.Equals(n.item);
		}

		public override bool Exists(RePredicate<T> pred)
		{
			bool exists = false;
			Foreach((t) =>
			{
				if (pred(t))
				{
					exists = true;
					ForeachBreak();
				}
			});

			return exists;
		}

		public override void Foreach(ReAction<T> todo)
		{
			Iterator it = new Iterator(hashes[0], 0, this);

			T t = it.Peek();
			while (it.HasNext)
			{
				todo(t);

				t = it.Next;
			}
			todo(t);
		}

		public override IReIterator<T> MakeIterator()
		{
			return new Iterator(hashes[0], 0, this);
		}

		public override IReIterator<T> MakeIteratorEnd()
		{
			int depth = 0;
			int lastHash = hashes[hashes.Count - 1];
			Node n = GetNodeOf(GetLocation(lastHash));
			while (n.next != null)
			{
				n = n.next;
				depth++;
			}

			return new Iterator(lastHash, depth, this);
		}

		public override IReSet<T> MakeNewSet()
		{
			return new ReHashSet<T>();
		}

		public override bool Remove(T removed)
		{
			BlockLoc loc = GetLocation(removed);
			Node rem = GetNodeOf(loc);

			if (rem == null)
			{
				return false; //no such item
			}

			// unstitch
			if (rem.next != null)
			{
				rem.next.prev = null;
				rem.next = null; 
			}
			else
			{
				// last of its node string
				blocks[loc.Block].locations.Remove(loc.Within);

				if (blocks[loc.Block].locations.IsEmpty) // last of its block
				{
					blocks[loc.Block] = null;
				}
			}

			return true;
		}

		public override void RemoveAll(RePredicate<T> pred)
		{
			while (Exists(pred))
			{
				Remove(Find(pred));
			}
		}
	}
}
