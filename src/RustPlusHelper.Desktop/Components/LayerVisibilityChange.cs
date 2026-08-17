using RustPlusHelper.Application.Map;

namespace RustPlusHelper.Desktop.Components;

public sealed record LayerVisibilityChange(MapLayerKind Kind, bool IsVisible);
