namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// A lookup of an object stream's header by object number, for the reader and the validator alike: each number with its
/// place, the number in the upper half, sorted, so that the first place of a number is found by a binary search. Built
/// once for a header, it makes a lookup for each object of the stream cost a search, not a scan of the header.
/// </summary>
internal static class ObjectStreamPlaces
{
    /// <summary>Builds the lookup of the header that lists <paramref name="numbers"/>, in order.</summary>
    public static long[] Of(int[] numbers)
    {
        var places = new long[numbers.Length];

        for (var i = 0; i < numbers.Length; i++)
        {
            places[i] = ((long)numbers[i] << 32) | (uint)i;
        }

        Array.Sort(places);
        return places;
    }

    /// <summary>Gives the first place the header lists <paramref name="number"/> at, or -1 when it does not list it.</summary>
    public static int FirstIndexOf(long[] places, int number)
    {
        // The first place whose number is not below the one sought: the number's first place, if it has one.
        var sought = (long)number << 32;
        var low = 0;
        var high = places.Length;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (places[middle] < sought)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low < places.Length && (places[low] >> 32) == number ? (int)(uint)places[low] : -1;
    }
}
