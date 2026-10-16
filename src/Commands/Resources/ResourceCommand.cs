using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Resources;

/// <summary>
/// The generic commands are registered once per resource with the <see cref="Resource"/> as
/// command data (<c>WithData</c>), so one class serves <c>customers list</c>, <c>invoices list</c>
/// and the rest.
/// </summary>
public static class ResourceCommand
{
    public static Resource ResourceOf(CommandContext context) =>
        context.Data switch
        {
            Resource r => r,
            ActionData a => a.Resource,
            _ => throw new InvalidOperationException("Command registered without its resource.")
        };
}

public sealed record ActionData(Resource Resource, ResourceAction Action);
