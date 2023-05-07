namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetRiddleCommand : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => _shouldRemoveNextCommand;

    public string Title => "riddle";

    public string Text => _text;

    public string Description => "Three chances to guess correctly";

    public bool HiddenCommand => false;

    public bool AcceptsCommands => true;

    private string _text;

    private readonly Random _random = new();

    private int attemptsLeft = 3;

    private string answer;

    private string _answerPrompt;

    private bool _shouldRemoveNextCommand;
    private int _riddleNumber;
    private readonly StringBuilder _riddle = new();
    private TelnetSession _session = null;

    public void OnAdd(TelnetSession session)
    {
        _session = session;
        _riddleNumber = _random.Next(1, 6); // Choose a random riddle number between 1 and 5
        UpdateText();
    }

    /// <summary>
    /// Forces network to send update to client with changed text.
    /// </summary>
    private void UpdateText()
    {
        // Reusing same string builder so we clear it each time.
        _riddle.Clear();

        _riddle.Append("Riddle me this, riddle me that. Three chances to guess my riddle!\r\n");
        switch (_riddleNumber)
        {
            case 1:
                _riddle.Append(_session.WordWrapText("What has a heart that doesn't beat?"));
                answer = "artichoke";
                break;
            case 2:
                _riddle.Append(_session.WordWrapText("What is full of holes but still holds water?"));
                answer = "sponge";
                break;
            case 3:
                _riddle.Append(_session.WordWrapText("What can travel around the world while staying in a corner?"));
                answer = "stamp";
                break;
            case 4:
                _riddle.Append(_session.WordWrapText("I am not alive, but I can grow. I don't have lungs, but I need air. I don't have a mouth, but water kills me. What am I?"));
                answer = "fire";
                break;
            case 5:
                _riddle.Append(_session.WordWrapText("The more you take, the more you leave behind. What am I?"));
                answer = "footsteps";
                break;
            default:
                _riddle.Append(_session.WordWrapText("Error: Unknown riddle"));
                answer = string.Empty;
                break;
        }

        // Only use this prompt on first time!
        if (attemptsLeft == 3)
        {
            _answerPrompt = $"What is your answer? Guesses left {attemptsLeft}\r\n";
        }

        _riddle.Append(_answerPrompt);

        _text = _riddle.ToString();
    }

    public void Destroy() { }

    public void Tick() { }

    public void ProcessCommand(string command)
    {
        // We use contains because someone could say "a wolf", when answer is "wolf" and that should still be valid.
        if (command.Contains(answer))
        {
            _answerPrompt = "Correct! You win!\r\n";
            _shouldRemoveNextCommand = true;
        }
        else if (!command.Contains(answer) && attemptsLeft > 0)
        {
            attemptsLeft--;

            if (attemptsLeft <= 0)
            {
                // Out of answers!
                _answerPrompt = $"You ran out of attempts. The answer was '{answer}'.\r\n";
                _shouldRemoveNextCommand = true;
            }
            else
            {
                _answerPrompt = $"Wrong answer. You have {attemptsLeft} attempts left.\r\n";
            }
        }

        // Forces rebuild of display text sent to user.
        UpdateText();
    }
}
