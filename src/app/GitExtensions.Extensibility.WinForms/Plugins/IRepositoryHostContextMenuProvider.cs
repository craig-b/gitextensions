namespace GitExtensions.Extensibility.Plugins;

/// <summary>
///  WinForms-side companion of <see cref="IRepositoryHostPlugin"/>, allowing the plugin to
///  contribute entries to a <see cref="ContextMenuStrip"/>.
/// </summary>
public interface IRepositoryHostContextMenuProvider
{
    void ConfigureContextMenu(ContextMenuStrip contextMenu);
}
