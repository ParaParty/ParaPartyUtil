using System.Collections.Generic;

namespace Paraparty.Kotlinize
{
    public static class Collections
    {
        /// <summary>
        /// Create a list by given item.
        /// </summary>
        /// <param name="items"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static List<T> ListOf<T>(params T[] items)
            => new List<T>().Also(s => s.AddRange(items));

    }
}
