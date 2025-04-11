namespace LoupixDeck.Models;

public class Macro
{
    public string Name { get; set; }
    
    public List<string> Commands { get; set; }

    public Macro()
    {
        Commands = new List<string>();
    }

    public void AddCommand(string command)
    {
        Commands.Add(command);
    }

    public void MoveCommandUp(int index)
    {
        if (index <= 0 || index >= Commands.Count) return;

        (Commands[index - 1], Commands[index]) = (Commands[index], Commands[index - 1]);
    }

    public void MoveCommandDown(int index)
    {
        if (index < 0 || index >= Commands.Count - 1) return;

        (Commands[index + 1], Commands[index]) = (Commands[index], Commands[index + 1]);
    }
}
