using jjevol.Data;
using System;
using System.Collections.Generic;

namespace jjevol
{
    public struct Int2Key : IEquatable<Int2Key>
    {
        public int Item1 { get; }
        public int Item2 { get; }

        public Int2Key(int item1, int item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        public override bool Equals(object obj)
        {
            return obj is Int2Key other && Equals(other);
        }

        public bool Equals(Int2Key other)
        {
            return Item1 == other.Item1 && Item2 == other.Item2;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1, Item2);
        }
    }
}