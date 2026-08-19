using RustPlusApi.Data;
using RustPlusApi.Data.Entities;
using RustPlusApi.Data.Events;
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

    [Fact]
    public void SmartDeviceInfoCarriesTheGivenEntityIdAndValue()
    {
        var source = new SmartDeviceInfo { IsActive = true };

        var result = RustPlusApiMapper.Map(4242UL, source);

        Assert.Equal(4242UL, result.EntityId);
        Assert.True(result.Value);
    }

    [Fact]
    public void StorageMonitorInfoPreservesCapacityProtectionAndItems()
    {
        var source = new StorageMonitorInfo
        {
            Capacity = 24,
            HasProtection = true,
            Items =
            [
                new StorageMonitorItemInfo { Id = -932201673, Quantity = 400, IsItemBlueprint = false }
            ]
        };

        var result = RustPlusApiMapper.Map(99UL, source);

        Assert.Equal(99UL, result.EntityId);
        Assert.Equal(24, result.Capacity);
        Assert.True(result.HasProtection);
        var item = Assert.Single(result.Items);
        Assert.Equal(-932201673, item.ItemId);
        Assert.Equal(400, item.Quantity);
        Assert.False(item.IsBlueprint);
    }

    [Fact]
    public void StorageMonitorItemsWithNullQuantityOrBlueprintDefaultSafely()
    {
        var source = new StorageMonitorInfo
        {
            Items = [new StorageMonitorItemInfo { Id = 123, Quantity = null, IsItemBlueprint = null }]
        };

        var result = RustPlusApiMapper.Map(1UL, source);

        var item = Assert.Single(result.Items);
        Assert.Equal(0, item.Quantity);
        Assert.False(item.IsBlueprint);
    }

    [Fact]
    public void EntityChangedEventArgMapsSwitchAndStorageFieldsWithoutGuessingKind()
    {
        var switchChange = new EntityChangedEventArg { Id = 7UL, Value = true };
        var storageChange = new EntityChangedEventArg
        {
            Id = 8UL,
            Capacity = 10,
            HasProtection = false,
            Items = [new StorageMonitorItemInfo { Id = -151838493, Quantity = 200, IsItemBlueprint = false }]
        };

        var switchResult = RustPlusApiMapper.Map(switchChange);
        var storageResult = RustPlusApiMapper.Map(storageChange);

        Assert.Equal(7UL, switchResult.EntityId);
        Assert.True(switchResult.Value);
        Assert.Null(switchResult.Capacity);

        Assert.Equal(8UL, storageResult.EntityId);
        Assert.Null(storageResult.Value);
        Assert.Equal(10, storageResult.Capacity);
        Assert.False(storageResult.HasProtection);
        Assert.Equal(-151838493, Assert.Single(storageResult.Items).ItemId);
    }
}
