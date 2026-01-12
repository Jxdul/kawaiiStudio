using System.Diagnostics;

var psi = new ProcessStartInfo
{
    FileName = "dotnet",
    Arguments = "run --project src\\KawaiiStudio.App\\KawaiiStudio.App.csproj",
    UseShellExecute = false
};

var process = Process.Start(psi);
if (process is null)
{
    Console.Error.WriteLine("Failed to start KawaiiStudio.App.");
    return;
}

process.WaitForExit();
Environment.Exit(process.ExitCode);
