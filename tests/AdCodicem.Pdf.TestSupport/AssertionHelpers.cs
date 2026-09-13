using System.Runtime.CompilerServices;

namespace AdCodicem.Pdf.TestSupport;

/// <summary>Small assertion helpers that keep test code reading as prose.</summary>
public static class AssertionHelpers
{
    /// <summary>
    /// Asserts that a value is not null and returns it, so a chain can continue.
    /// </summary>
    /// <remarks>
    /// Navigating a PDF object graph in a test means a run of nullable lookups. Asserting and returning in
    /// one step keeps the test about the behaviour rather than about null handling.
    /// </remarks>
    public static T Required<T>(
        this T? value,
        string because = "",
        [CallerArgumentExpression(nameof(value))] string? expression = null)
        where T : class
    {
        value.Should().NotBeNull(because.Length > 0 ? because : $"{expression} should have a value");
        return value!;
    }

    /// <summary>Asserts that an action throws <typeparamref name="TException"/>.</summary>
    public static void FluentThrow<TException>(Action action)
        where TException : Exception
    {
        action.Should().Throw<TException>();
    }
}
