using RemoveMultiplayerPlayerLimit.src;
using System.Collections.Generic;
using System.IO;

namespace RemoveMultiplayerPlayetLimit.src.Extensions
{
    public static class ListExt
    {
        public static bool TryGetNext<T>(this List<T> list, T value, out T next)
        {
            return TryGetAfter(list, value, 1, out next);
        }

        public static bool TryGetAfter<T>(this List<T> list, T value, int num, out T after)
        {
            after = default;

            var index = list.IndexOf(value);

            if (index != -1 && list.Count > index + num)
            {
                after = list[index + num];

                return true;
            }

            return false;
        }

        public static bool TryGetLast<T>(this List<T> list, T value, out T last)
        {
            return TryGetBefore(list, value, 1, out last);
        }

        public static bool TryGetBefore<T>(this List<T> list, T value, int num, out T before)
        {
            before = default;

            var index = list.IndexOf(value);

            if (index != -1 && index - num >= 0)
            {
                before = list[index - num];

                return true;
            }

            return false;
        }
    }
}
