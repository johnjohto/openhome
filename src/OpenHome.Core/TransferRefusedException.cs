namespace OpenHome.Core;

/// <summary>
/// Thrown when strict transfer mode refuses a withdraw because the Pokémon cannot
/// legally enter the target game. Maps to HTTP 422, same as an unsupported conversion.
/// </summary>
public sealed class TransferRefusedException : Exception
{
    public TransferRefusedException(string message) : base(message) { }
}
