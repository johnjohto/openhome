namespace OpenHome.Core;

/// <summary>
/// Thrown when a stored Pokémon cannot be converted into the target save's entity
/// format (e.g. withdrawing into an older generation than the origin game).
/// </summary>
public sealed class UnsupportedConversionException : Exception
{
    public UnsupportedConversionException(string message) : base(message) { }
    public UnsupportedConversionException(string message, Exception inner) : base(message, inner) { }
}
