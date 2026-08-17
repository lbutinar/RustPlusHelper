using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Infrastructure.RustPlus;

public sealed class RustPlusApiClientFactory : IRustPlusClientFactory
{
    public IRustPlusClient Create() => new RustPlusApiClient();
}
