using RustPlusApi.Data;
using RustPlusApi.Data.Markers;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Infrastructure.RustPlus;

namespace RustPlusHelper.Tests;

public sealed class RustPlusApiMapperTests
{
    [Fact]
    public void ApplicationPublicContractDoesNotExposeRustPlusApiTypes()
    {
        var leakedTypes = typeof(IRustPlusClient).Assembly
            .ExportedTypes
            .SelectMany(type => type.GetMembers())
            .Select(member => member switch
            {
                System.Reflection.MethodInfo method => method.ReturnType,
                System.Reflection.PropertyInfo property => property.PropertyType,
                System.Reflection.FieldInfo field => field.FieldType,
                _ => null
            })
            .Where(type => type?.Namespace?.StartsWith("RustPlusApi", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(leakedTypes);
    }

    [Fact]
    public void OptionalServerFieldsRemainOptional()
    {
        var result = RustPlusApiMapper.Map(new ServerInfo());

        Assert.Null(result.Name);
        Assert.Null(result.MapSize);
        Assert.Null(result.WipeTimeUtc);
        Assert.Null(result.PlayerCount);
    }

    [Fact]
    public void UnknownMarkerPreservesRawTypeAndUnsignedId()
    {
        var id = ulong.MaxValue - 5;
        var source = new MapMarkers
        {
            UnknownMarkers = new Dictionary<ulong, UnknownMarker>
            {
                [id] = new()
                {
                    Id = id,
                    RawType = 777,
                    X = 123.5f,
                    Y = 456.25f,
                    Name = "Future marker"
                }
            }
        };

        var result = RustPlusApiMapper.Map(source);

        var marker = Assert.Single(result.Markers);
        Assert.Equal(id, marker.Id);
        Assert.Equal(MapMarkerKind.Unknown, marker.Kind);
        Assert.Equal(777, marker.RawType);
        Assert.Equal("Future marker", marker.Name);
    }

    [Fact]
    public void VendingOrderPreservesCurrentMultiplierFields()
    {
        var source = new MapMarkers
        {
            VendingMachineMarkers = new Dictionary<ulong, VendingMachineMarker>
            {
                [42] = new()
                {
                    Id = 42,
                    VendingMachineItems =
                    [
                        new VendingMachineItem
                        {
                            Id = -904863145,
                            StackSize = 1,
                            CurrencyId = -932201673,
                            CostPerStack = 85,
                            StackSizeAmount = 3,
                            PriceMultiplier = 1.25f,
                            ReceivedQuantityMultiplier = 0.5f
                        }
                    ]
                }
            }
        };

        var result = RustPlusApiMapper.Map(source);

        var order = Assert.Single(Assert.Single(result.Markers).VendingOrders!);
        Assert.Equal(1.25f, order.PriceMultiplier);
        Assert.Equal(0.5f, order.ReceivedQuantityMultiplier);
    }
}
