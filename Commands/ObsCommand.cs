using LoupixDeck.Commands.Base;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

[Command("System.ObsStartRecord")]
public class ObsStartRecordCommand(ObsController obs) : IExecutableCommand
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

[Command("System.ObsStopRecord")]
public class ObsStopRecordCommand(ObsController obs) : IExecutableCommand
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

[Command("System.ObsPauseRecord")]
public class ObsPauseRecordCommand(ObsController obs) : IExecutableCommand
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

[Command("System.ObsVirtualCam")]
public class ObsVirtualCamCommand(ObsController obs) : IExecutableCommand
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

[Command("System.ObsStartReplay")]
public class ObsStartReplayCommand(ObsController obs) : IExecutableCommand
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

[Command("System.ObsStopReplay")]
public class ObsStopReplayCommand(ObsController obs) : IExecutableCommand
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

[Command("System.ObsSaveReplay")]
public class ObsSaveReplayCommand(ObsController obs) : IExecutableCommand
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

[Command("System.ObsSetScene")]
public class ObsSetSceneCommand(ObsController obs) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }
        
        var sceneName = parameters[0];
        
        obs.SetScene(sceneName);
        return Task.CompletedTask;
    }
}