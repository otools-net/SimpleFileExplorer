using System;

namespace Otools.Net.Sfe.Client;

/// <summary>Something the library could not do: the base of its own exceptions.</summary>
public class SfeException : Exception
{
    /// <summary>An error explained by <paramref name="message"/>.</summary>
    /// <param name="message">What went wrong, for people.</param>
    /// <param name="innerException">The error behind it, if any.</param>
    public SfeException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
