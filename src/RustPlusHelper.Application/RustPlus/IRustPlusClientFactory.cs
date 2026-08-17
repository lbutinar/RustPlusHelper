namespace RustPlusHelper.Application.RustPlus;

/// <summary>Creates isolated Rust+ client lifecycles for connection supervisors and tests.</summary>
public interface IRustPlusClientFactory
{
    IRustPlusClient Create();
}
