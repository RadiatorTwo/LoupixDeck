using System.Text;
using System.Text.RegularExpressions;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: AssetGenerator <assetRoot> <outputFile> <assemblyName>");
    return 1;
}

var root = args[0];
var output = args[1];
var assembly = args[2];

var sb = new StringBuilder();
sb.AppendLine("namespace LoupixDeck;");
sb.AppendLine("public static class Assets");
sb.AppendLine("{");
sb.AppendLine("    public static readonly Dictionary<string, string> All = new()");
sb.AppendLine("    {");

foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
    var name = Path.GetFileNameWithoutExtension(relative);
    var safe = Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");

    var resourcePath = $"avares://{assembly}/Assets/{relative}";
    sb.AppendLine($@"        [""{safe}""] = ""{resourcePath}"",");
}

sb.AppendLine("    };");
sb.AppendLine("}");

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, sb.ToString());
return 0;