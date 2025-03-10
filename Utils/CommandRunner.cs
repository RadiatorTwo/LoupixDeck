using System.Diagnostics;

namespace LoupixDeck.Utils;

public abstract class CommandRunner
{
    public static void ExecuteCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process();
            process.StartInfo = psi;

            process.Start();
            
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
                Console.WriteLine($"Output: {output}");

            if (!string.IsNullOrWhiteSpace(error))
                Console.WriteLine($"Error: {error}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
        }
    }
}