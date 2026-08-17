namespace RustPlusHelper.Application.RustPlus;

/// <summary>A connection failure whose message has already been stripped of player credentials.</summary>
public sealed class RustPlusConnectionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
