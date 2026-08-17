using ProtoBuf;

namespace RustPlusHelper.Infrastructure.Map;

[ProtoContract]
internal sealed class WorldData
{
    [ProtoMember(1)]
    public uint Size { get; set; }

    [ProtoMember(2)]
    public List<MapData> Maps { get; } = [];

    [ProtoMember(3)]
    public List<PrefabData> Prefabs { get; } = [];

    [ProtoMember(4)]
    public List<PathData> Paths { get; } = [];
}

[ProtoContract]
internal sealed class MapData
{
    [ProtoMember(1)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(2)]
    public byte[] Data { get; set; } = [];
}

[ProtoContract]
internal sealed class PrefabData
{
    [ProtoMember(1)]
    public string Category { get; set; } = string.Empty;

    [ProtoMember(2)]
    public uint Id { get; set; }

    [ProtoMember(3)]
    public VectorData? Position { get; set; }

    [ProtoMember(4)]
    public VectorData? Rotation { get; set; }

    [ProtoMember(5)]
    public VectorData? Scale { get; set; }
}

[ProtoContract]
internal sealed class VectorData
{
    [ProtoMember(1)]
    public float X { get; set; }

    [ProtoMember(2)]
    public float Y { get; set; }

    [ProtoMember(3)]
    public float Z { get; set; }
}

[ProtoContract]
internal sealed class PathData
{
    [ProtoMember(1)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(2)]
    public bool Spline { get; set; }

    [ProtoMember(3)]
    public bool Start { get; set; }

    [ProtoMember(4)]
    public bool End { get; set; }

    [ProtoMember(5)]
    public float Width { get; set; }

    [ProtoMember(6)]
    public float InnerPadding { get; set; }

    [ProtoMember(7)]
    public float OuterPadding { get; set; }

    [ProtoMember(8)]
    public float InnerFade { get; set; }

    [ProtoMember(9)]
    public float OuterFade { get; set; }

    [ProtoMember(10)]
    public float RandomScale { get; set; }

    [ProtoMember(11)]
    public float MeshOffset { get; set; }

    [ProtoMember(12)]
    public float TerrainOffset { get; set; }

    [ProtoMember(13)]
    public int Splat { get; set; }

    [ProtoMember(14)]
    public int Topology { get; set; }

    [ProtoMember(15)]
    public List<VectorData> Nodes { get; } = [];
}
