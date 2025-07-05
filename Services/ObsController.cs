using LoupixDeck.Models;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;

namespace LoupixDeck.Services;

public interface IObsController
{
    event EventHandler Connected;
    event EventHandler<ObsDisconnectionInfo> Disconnected;
    void Connect(string ip = "", int port = 0, string password = "");

    Task ConnectAndWaitAsync(string ip = "", int port = 0, string password = "",
        CancellationToken cancellationToken = default);

    void Disconnect();
    Task ToggleVirtualCamera();
    Task StartStreaming();
    Task StopStreaming();
    Task StartRecording();
    Task StopRecording();
    Task PauseRecording();
    Task StartReplayBuffer();
    Task StopReplayBuffer();
    Task SaveReplayBuffer();
    Task ToggleMute(string sourceName);
    Task SetVolume(string sourceName, float volume);
    Task<float> GetInputVolume(string inputName);
    Task<bool> IsInputMuted(string inputName);
    Task ShowSource(string sceneName, int sceneItemId);
    Task HideSource(string sceneName, int sceneItemId);
    Task ToggleSourceVisibility(string sceneName, int sceneItemId);
    Task SetScene(string sceneName);
    Task<string> GetCurrentSceneName();
    Task<List<SceneBasicInfo>> GetScenes();
    Task<bool> IsStudioModeEnabled();
    Task SetStudioMode(bool enabled);
}

public class ObsController : IObsController
{
    private readonly OBSWebsocket _obs = new();
    private ObsConfig _obsConfig;

    public event EventHandler Connected;
    public event EventHandler<ObsDisconnectionInfo> Disconnected;

    public void Connect(string ip = "", int port = 0, string password = "")
    {
        try
        {
            if (_obs.IsConnected)
            {
                Disconnect();
            }

            if (!string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(ip) && port > 0)
            {
                _obsConfig = new ObsConfig
                {
                    Ip = ip,
                    Port = port,
                    Password = password
                };
            }
            else
            {
                _obsConfig = ObsConfig.LoadConfig();
                _obs.Connected += Obs_Connected;
                _obs.Disconnected += Obs_Disconnected;
            }

            _obs.ConnectAsync(_obsConfig.Url, _obsConfig.Password);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to OBS: {ex.Message}");
        }
    }

    public async Task ConnectAndWaitAsync(
        string ip = "",
        int port = 0,
        string password = "",
        CancellationToken cancellationToken = default)
    {
        if (_obs.IsConnected)
            return;

        if (!string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(ip) && port > 0)
        {
            _obsConfig = new ObsConfig
            {
                Ip = ip,
                Port = port,
                Password = password
            };
        }
        else
        {
            _obsConfig = ObsConfig.LoadConfig();
        }

        var tcs = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Register events
        _obs.Connected += OnConnected;
        _obs.Disconnected += OnDisconnected;

        // Attempt to connect (not awaitable!)
        _obs.ConnectAsync(_obsConfig.Url, _obsConfig.Password);

        var timeout = TimeSpan.FromSeconds(2);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, cancellationToken);

        var token = linkedCts.Token;

        await using (linkedCts.Token.Register(() => tcs.TrySetCanceled(token)))
        {
            await tcs.Task.ConfigureAwait(false);
        }

        return;

        void OnConnected(object _, EventArgs __)
        {
            Unsubscribe();
            tcs.TrySetResult();
        }

        void OnDisconnected(object _, ObsDisconnectionInfo info)
        {
            Unsubscribe();
            tcs.TrySetException(
                new InvalidOperationException(
                    $"{info.ObsCloseCode}/{info.WebsocketDisconnectionInfo} - {info.DisconnectReason}"));
        }

        void Unsubscribe()
        {
            _obs.Connected -= OnConnected;
            _obs.Disconnected -= OnDisconnected;
        }
    }

    private async Task<bool> CheckConnection()
    {
        if (_obs.IsConnected) return _obs.IsConnected;
        
        try
        {
            // Avoid capturing the current SynchronizationContext to prevent
            // deadlocks when called from a synchronously blocked context.
            await ConnectAndWaitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to OBS: {ex.Message}");
            return false;
        }

        return _obs.IsConnected;
    }

    private void Obs_Connected(object sender, EventArgs e)
    {
        Console.WriteLine("OBS Connected");
        Connected?.Invoke(this, e);
    }

    private void Obs_Disconnected(object sender, ObsDisconnectionInfo e)
    {
        Console.WriteLine($"OBS Disconnected: {e.DisconnectReason}");
        Disconnected?.Invoke(this, e);
    }

    public void Disconnect()
    {
        if (_obs == null || !_obs.IsConnected) return;

        _obs.Disconnect();
    }

    #region Streaming Functions

    public async Task ToggleVirtualCamera()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.ToggleVirtualCam();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error toggling Virtual Camera: {ex.Message}");
        }
    }

    public async Task StartStreaming()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.StopStream();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting stream: {ex.Message}");
        }
    }

    public async Task StopStreaming()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.StartStream();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping stream: {ex.Message}");
        }
    }

    #endregion

    #region Recording Functions

    public async Task StartRecording()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.StartRecord();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting recording: {ex.Message}");
            throw;
        }
    }

    public async Task StopRecording()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.StopRecord();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping recording: {ex.Message}");
            throw;
        }
    }

    public async Task PauseRecording()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            Connect();
        }

        try
        {
            _obs.PauseRecord();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error pausing recording: {ex.Message}");
            throw;
        }
    }

    public async Task StartReplayBuffer()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.StartReplayBuffer();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting replay buffer: {ex.Message}");
            throw;
        }
    }

    public async Task StopReplayBuffer()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.StopReplayBuffer();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping replay buffer: {ex.Message}");
            throw;
        }
    }

    public async Task SaveReplayBuffer()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.SaveReplayBuffer();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving replay buffer: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Audio Functions

    public async Task ToggleMute(string sourceName)
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.ToggleInputMute(sourceName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error toggling mute for '{sourceName}': {ex.Message}");
            throw;
        }
    }

    public async Task SetVolume(string sourceName, float volume)
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.SetInputVolume(sourceName, volume);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting volume for '{sourceName}': {ex.Message}");
            throw;
        }
    }

    public async Task<float> GetInputVolume(string inputName)
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return 0f;
        }

        try
        {
            var result = _obs.GetInputVolume(inputName);
            return result.VolumeMul;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting volume for '{inputName}': {ex.Message}");
            throw;
        }
    }

    public async Task<bool> IsInputMuted(string inputName)
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            var result = _obs.GetInputMute(inputName);
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting mute status for '{inputName}': {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Scene Items

    public async Task ShowSource(string sceneName, int sceneItemId)
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.SetSceneItemEnabled(sceneName, sceneItemId, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error showing source '{sceneItemId}' in scene '{sceneName}': {ex.Message}");
            throw;
        }
    }

    public async Task HideSource(string sceneName, int sceneItemId)
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.SetSceneItemEnabled(sceneName, sceneItemId, false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error hiding source '{sceneItemId}' in scene '{sceneName}': {ex.Message}");
            throw;
        }
    }

    public async Task ToggleSourceVisibility(string sceneName, int sceneItemId)
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var enabled = _obs.GetSceneItemEnabled(sceneName, sceneItemId);
            // bool currentState = item.SceneItemEnabled;
            _obs.SetSceneItemEnabled(sceneName, sceneItemId, !enabled);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Error toggling visibility of source '{sceneItemId}' in scene '{sceneName}': {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Scene Functions

    public async Task SetScene(string sceneName)
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.SetCurrentProgramScene(sceneName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting Scene: {ex.Message}");
            throw;
        }
    }

    public async Task<string> GetCurrentSceneName()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return string.Empty;
        }

        try
        {
            var result = _obs.GetCurrentProgramScene();
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting current scene: {ex.Message}");
            throw;
        }
    }

    public async Task<List<SceneBasicInfo>> GetScenes()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return [];
        }

        try
        {
            var sceneList = _obs.GetSceneList();
            return sceneList.Scenes;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting Scenes: {ex.Message}");
            return [];
        }
    }

    #endregion

    #region Other Functions

    public async Task<bool> IsStudioModeEnabled()
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            var result = _obs.GetStudioModeEnabled();
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting studio mode status: {ex.Message}");
            throw;
        }
    }

    public async Task SetStudioMode(bool enabled)
    {
        if (!await CheckConnection().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _obs.SetStudioModeEnabled(enabled);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting studio mode: {ex.Message}");
            throw;
        }
    }

    #endregion
}