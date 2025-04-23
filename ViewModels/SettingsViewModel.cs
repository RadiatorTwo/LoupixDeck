using System.Windows.Input;
using Avalonia.Media;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using OBSWebsocketDotNet.Communication;

namespace LoupixDeck.ViewModels;

public class SettingsViewModel : DialogViewModelBase<DialogResult>
{
    public LoupedeckConfig Config { get; }
    private readonly IObsController _obs;
    public ICommand SaveObsCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand ShowGeneralCommand { get; }
    public ICommand ShowObsCommand { get; }

    public SettingsViewModel(LoupedeckConfig config,IObsController obs)
    {
        Config = config;
        SaveObsCommand = new RelayCommand(SaveObs);
        TestConnectionCommand = new RelayCommand(TestConnection);
        ShowGeneralCommand = new RelayCommand(ShowGeneral);
        ShowObsCommand = new RelayCommand(ShowObs);

        IsGeneralSelected = true;
        IsObsSelected = false;

        ConnectionTestVisible = true;

        ObsConfig = ObsConfig.LoadConfig();
        _obs = obs;

        _obs.Connected += ObsConnected;
        _obs.Disconnected += ObsDisconnected;
    }

    private void ObsConnected(object sender, EventArgs e)
    {
        ConnectionResult = "Successfully connected";
        ConnectionTestVisible = true;
        TextColor = Colors.Green;
    }

    private void ObsDisconnected(object sender, ObsDisconnectionInfo e)
    {
        ConnectionResult = $"Error: {e.WebsocketDisconnectionInfo.CloseStatusDescription}";
        ConnectionTestVisible = true;
        TextColor = Colors.Red;
    }

    private bool _connectionTestVisible;

    public bool ConnectionTestVisible
    {
        get => _connectionTestVisible;
        set => SetProperty(ref _connectionTestVisible, value);
    }

    private Color _textColor = Colors.Blue;

    public Color TextColor
    {
        get => _textColor;
        set => SetProperty(ref _textColor, value);
    }

    private string _connectionResult;

    public string ConnectionResult
    {
        get => _connectionResult;
        set => SetProperty(ref _connectionResult, value);
    }

    private bool _isGeneralSelected;

    public bool IsGeneralSelected
    {
        get => _isGeneralSelected;
        set => SetProperty(ref _isGeneralSelected, value);
    }

    private bool _isObsSelected;

    public bool IsObsSelected
    {
        get => _isObsSelected;
        set => SetProperty(ref _isObsSelected, value);
    }

    public ObsConfig ObsConfig { get; }

    private void SaveObs()
    {
        ObsConfig.SaveConfig();
    }

    private void TestConnection()
    {
        _obs.Connect(ObsConfig.Ip, ObsConfig.Port, ObsConfig.Password);
    }

    private void ShowGeneral()
    {
        IsGeneralSelected = true;
        IsObsSelected = false;
    }

    private void ShowObs()
    {
        IsGeneralSelected = false;
        IsObsSelected = true;
    }
}