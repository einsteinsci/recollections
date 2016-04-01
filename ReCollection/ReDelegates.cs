namespace ReCollections
{
	public delegate void ReAction<T>(T item);
	// positive if item1 is bigger, negative if item2 is bigger, and zero if they are equal
	public delegate int ReComparison<T>(T item1, T item2);
	public delegate bool RePredicate<T>(T input);
}