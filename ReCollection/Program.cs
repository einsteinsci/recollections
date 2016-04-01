using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReCollections
{
	class Program
	{
		static void Main(string[] args)
		{

			Console.ReadKey();
		}

		public static void Log(string text)
		{
			string loc = "C:\\temp\\log100.txt";
			using (StreamWriter writer = new StreamWriter(loc, true))
			{
				writer.WriteLine(text);
			}

			Console.WriteLine(text);
		}
	}
}
