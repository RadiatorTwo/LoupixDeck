using LoupixDeck.Models;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;

namespace LoupixDeck.Services;

public class ObsController
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
                _obsConfig = new ObsConfig();
                _obsConfig.Ip = ip;
                _obsConfig.Port = port;
                _obsConfig.Password = password;
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

    public void ToggleVirtualCamera()
    {
        if (!_obs.IsConnected)
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

    public void StartStreaming()
    {
        if (!_obs.IsConnected)
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

    public void StopStreaming()
    {
        if (!_obs.IsConnected)
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

    public void StartRecording()
    {
        if (!_obs.IsConnected)
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

    public void StopRecording()
    {
        if (!_obs.IsConnected)
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

    public void PauseRecording()
    {
        if (!_obs.IsConnected)
        {
            return;
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

    public void StartReplayBuffer()
    {
        if (!_obs.IsConnected)
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

    public void StopReplayBuffer()
    {
        if (!_obs.IsConnected)
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

    public void SaveReplayBuffer()
    {
        if (!_obs.IsConnected)
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

    public void ToggleMute(string sourceName)
    {
        if (!_obs.IsConnected)
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

    public void SetVolume(string sourceName, float volume)
    {
        if (!_obs.IsConnected)
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

    public float GetInputVolume(string inputName)
    {
        if (!_obs.IsConnected)
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

    public bool IsInputMuted(string inputName)
    {
        if (!_obs.IsConnected)
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

    public void ShowSource(string sceneName, int sceneItemId)
    {
        if (!_obs.IsConnected)
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

    public void HideSource(string sceneName, int sceneItemId)
    {
        if (!_obs.IsConnected)
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

    public void ToggleSourceVisibility(string sceneName, int sceneItemId)
    {
        if (!_obs.IsConnected)
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

    public void SetScene(string sceneName)
    {
        if (!_obs.IsConnected)
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

    public string GetCurrentSceneName()
    {
        if (!_obs.IsConnected)
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

    public List<SceneBasicInfo> GetScenes()
    {
        if (!_obs.IsConnected)
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

    public bool IsStudioModeEnabled()
    {
        if (!_obs.IsConnected)
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

    public void SetStudioMode(bool enabled)
    {
        if (!_obs.IsConnected)
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