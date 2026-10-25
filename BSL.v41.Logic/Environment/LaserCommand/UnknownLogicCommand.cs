using BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;

namespace BSL.v41.Logic.Environment.LaserCommand;

/// <summary>
/// Keeps an unregistered client command visible to the home-mode diagnostics
/// without ever allowing it to execute. The command payload is decoded using
/// the common LogicCommand header so the surrounding message remains aligned.
/// </summary>
public sealed class UnknownLogicCommand : LogicCommand
{
    private readonly int _commandType;

    public UnknownLogicCommand(int commandType)
    {
        _commandType = commandType;
    }

    public override bool CanExecute(LogicHomeMode logicHomeMode)
    {
        return false;
    }

    public override int GetCommandType()
    {
        return _commandType;
    }
}
