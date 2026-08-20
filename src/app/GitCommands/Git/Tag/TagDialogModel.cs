using GitCommands.Git.Extensions;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Git.Tag;

/// <summary>The create/delete-tag dialogs' decisions.</summary>
public static class TagDialogModel
{
    /// <summary>The operations in the dialog's dropdown order.</summary>
    public static IReadOnlyList<TagOperation> OperationChoices { get; } =
    [
        TagOperation.Lightweight,
        TagOperation.Annotate,
        TagOperation.SignWithDefaultKey,
        TagOperation.SignWithSpecificKey,
    ];

    /// <summary>An artificial target degrades to the current checkout; a zero checkout stays unset.</summary>
    public static ObjectId NormalizeTarget(ObjectId objectId, Func<ObjectId> getCurrentCheckout)
    {
        if (objectId.IsArtificial)
        {
            objectId = default;
        }

        return objectId.IsZero ? getCurrentCheckout() : objectId;
    }

    /// <summary>The remote a created tag is offered to push to.</summary>
    public static string ResolvePushRemote(string? currentRemote)
        => string.IsNullOrEmpty(currentRemote) ? "origin" : currentRemote;
}

/// <summary>What the selected tag operation allows in the create dialog.</summary>
public readonly record struct TagOptionAvailability(bool GpgKeyEnabled, bool MessageEnabled)
{
    public static TagOptionAvailability Evaluate(TagOperation operation)
        => new(
            GpgKeyEnabled: operation is TagOperation.SignWithSpecificKey,
            MessageEnabled: operation.CanProvideMessage());
}
