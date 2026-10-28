using Fineage.Login;

if (args.Contains("--selftest"))
{
    try
    {
        var result = await LoginAcceptance.RunAsync();
        LoginAcceptance.Write(Console.Out, result);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("selftest FAIL " + ex.Message);
        Environment.ExitCode = 1;
    }

    return;
}

Environment.ExitCode = await Cli.RunAsync(args);
