namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Runs work on a thread of its own, on a small stack, which a read nested deep runs short of, or on a large one, which
/// it does not: the suite's only threads that ask for a size of stack.
/// </summary>
/// <remarks>
/// <para>
/// A thread is not always given the stack it asks for. glibc keeps the stacks of the threads that ended, and gives a new
/// thread one of them rather than mapping another: one of the size asked for when it keeps one, else the smallest that
/// is larger, as long as it is no more than four times the size asked for, guard page included. A thread that asked for
/// 256 KB once one of 1 MB had ended ran on that thread's stack, where a read that runs short of 256 KB goes through
/// uncut: a test of what the reader does as its stack runs short passed or failed with the stacks the tests before it
/// had left (#168, #312).
/// </para>
/// <para>
/// So no thread of the suite asks for a stack larger than the small one and no more than four times it with its guard
/// page, and the large one is larger than that: no stack a large thread leaves is given to a small one. Nor is the large
/// one a quarter or less of the 8 MB a thread that asks for no size is given under Linux's default limit, which the
/// thread pool's threads leave as they retire: a large thread given such a stack holds what a test nests to overflow a
/// walk that takes a call a level, as 8 MB held a page tree path of 55,000 nodes written a call a node. A size of stack
/// is asked for here, one of these two, and nowhere else in the suite.
/// </para>
/// </remarks>
internal static class Stacks
{
    /// <summary>The small stack, 256 KB, which a read nested deep runs short of.</summary>
    public const int Small = 256 * 1024;

    /// <summary>
    /// The large stack, 1.5 MB: more than four times <see cref="Small"/> with its guard page, and less than a quarter of
    /// the 8 MB a thread that asks for no size is given.
    /// </summary>
    public const int Large = 1536 * 1024;

    /// <summary>
    /// Runs <paramref name="work"/> on a thread whose stack is <paramref name="size"/> bytes, <see cref="Small"/> or
    /// <see cref="Large"/>, and gives what it gave; what it threw fails the test.
    /// </summary>
    public static T Run<T>(int size, Func<T> work)
    {
        size.Should().BeOneOf([Small, Large], "a thread that asks for another size could be given a stack another thread left");

        T result = default!;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failure = exception;
                }
            },
            maxStackSize: size);

        thread.Start();
        thread.Join();

        failure.Should().BeNull();
        return result;
    }
}
