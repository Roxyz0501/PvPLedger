using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;
using System.Text.RegularExpressions;
var gameDir = args[0];
using var game = new GameData(Path.Combine(gameDir, "sqpack"), new LuminaOptions { DefaultExcelLanguage = Language.English });
foreach (var row in game.GetExcelSheet<ContentType>()) Console.WriteLine($"Type {row.RowId}: {row.Name}");
foreach (var id in new uint[] {557,558,559}) Console.WriteLine($"Log {id}: {game.GetExcelSheet<LogMessage>().GetRow(id).Text}");
var binary = File.ReadAllBytes(Path.Combine(gameDir, "ffxiv_dx11.exe"));
foreach (Match m in Regex.Matches(File.ReadAllText(args[1]), "const string (\\w+Signature) = \"([^\"]+)\""))
{
    var pattern = m.Groups[2].Value.Split(' ').Select(x => x == "??" ? -1 : Convert.ToInt32(x, 16)).ToArray();
    int hits = 0;
    for (int i = 0; i <= binary.Length - pattern.Length; i++)
    {
        if (binary[i] != pattern[0]) continue;
        int j = 1; for (; j < pattern.Length; j++) if (pattern[j] >= 0 && binary[i+j] != pattern[j]) break;
        if (j == pattern.Length) hits++;
    }
    Console.WriteLine($"{m.Groups[1].Value}: {hits} matches");
    if (hits != 1) Environment.ExitCode = 1;
}
