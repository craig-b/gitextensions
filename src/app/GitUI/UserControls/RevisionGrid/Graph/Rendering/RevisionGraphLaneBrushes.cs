namespace GitUI.UserControls.RevisionGrid.Graph.Rendering;

/// <summary>
///  The WinForms half of the lane colors: GDI brushes for the portable
///  <see cref="RevisionGraphLaneColor"/> palette (split out in the RevisionGrid spike so the
///  graph model is platform-neutral).
/// </summary>
internal static class RevisionGraphLaneBrushes
{
    internal static Brush NonRelativeBrush { get; } = new SolidBrush(RevisionGraphLaneColor.NonRelativeColor);

    private static readonly List<Brush> _presetGraphBrushes =
        [.. RevisionGraphLaneColor.PresetGraphColors.Select(color => (Brush)new SolidBrush(color))];

    internal static Brush GetBrushForLane(int laneColor)
    {
        return _presetGraphBrushes[laneColor];
    }
}
