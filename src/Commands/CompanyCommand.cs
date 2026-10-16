using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands;

public sealed class CompanyCommand : AsyncCommand<ApiSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ApiSettings settings)
    {
        var client = settings.CreateClient();
        using var result = await client.GetAsync("companyinformation");
        Output.Write(result);
        return 0;
    }
}
