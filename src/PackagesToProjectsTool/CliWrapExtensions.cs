using System.Text;
using CliWrap;

namespace SonicGD.PackagesToProjectsTool;

public static class CliWrapExtensions
{
    public static async Task<string[]> ExecuteCommandAsync(this Command command,
        CancellationToken cancellationToken = default)
    {
        var stdOutBuffer = new StringBuilder();
        var stdErrBuffer = new StringBuilder();
        var result = await command.WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErrBuffer))
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync(cancellationToken);
        if (result.ExitCode != 0)
        {
            var err = stdErrBuffer.ToString();
            if (err.Length == 0 && stdOutBuffer.Length > 0)
            {
                err = stdOutBuffer.ToString();
            }

            throw new InvalidOperationException(err);
        }

        return stdOutBuffer.ToString().Split(Environment.NewLine);
    }
}
