using LoupixDeck.Commands.Base;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

[Command("System.ObsStartRecord", "Start Recording", "OBS")]
public class ObsStartRecordCommand(IObsController obs) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        obs.StartRecording();
        return Task.CompletedTask;
    }
}

[Command("System.ObsStopRecord", "Stop Recording", "OBS")]
public class ObsStopRecordCommand(IObsController obs) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        obs.StopRecording();
        return Task.CompletedTask;
    }
}

[Command("System.ObsPauseRecord", "Pause Recording", "OBS")]
public class ObsPauseRecordCommand(IObsController obs) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        obs.PauseRecording();
        return Task.CompletedTask;
    }
}

[Command("System.ObsVirtualCam", "Toggle Virtual Camera", "OBS")]
public class ObsVirtualCamCommand(IObsController obs) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        obs.ToggleVirtualCamera();
        return Task.CompletedTask;
    }
}

[Command("System.ObsStartReplay", "Start Replay", "OBS")]
public class ObsStartReplayCommand(IObsController obs) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        obs.StartReplayBuffer();
        return Task.CompletedTask;
    }
}

[Command("System.ObsStopReplay", "Stop Replay", "OBS")]
public class ObsStopReplayCommand(IObsController obs) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        obs.StopReplayBuffer();
        return Task.CompletedTask;
    }
}

[Command("System.ObsSaveReplay", "Save Replay", "OBS")]
public class ObsSaveReplayCommand(IObsController obs) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        obs.SaveReplayBuffer();
        return Task.CompletedTask;
    }
}

[Command(
    "System.ObsSetScene",
    "Set Scene",
    "OBS",
    "({SceneName})",
    ["SceneName"],
    [typeof(string)])]
public class ObsSetSceneCommand(IObsController obs) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var sceneName = parameters[0];

        await obs.SetScene(sceneName);
    }
}