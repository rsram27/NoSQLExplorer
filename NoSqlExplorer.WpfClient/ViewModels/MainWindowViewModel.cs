﻿using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Messaging;
using MaterialDesignThemes.Wpf;
using NoSqlExplorer.AzureAdapter;
using NoSqlExplorer.AzureAdapter.Configuration;
using NoSqlExplorer.DockerAdapter;
using NoSqlExplorer.DockerAdapter.ConfigSection;
using NoSqlExplorer.TwitterReader;
using NoSqlExplorer.TwitterReader.Configuration;
using NoSqlExplorer.TwitterReader.Model;
using NoSqlExplorer.WpfClient.Configuration;
using NoSqlExplorer.WpfClient.Messages;

namespace NoSqlExplorer.WpfClient.ViewModels
{
  public class MainWindowViewModel : ViewModelBase
  {
    private readonly ITwitterConfigSettings _twitterConfigSettings;
    private readonly ITwitterReader _twitterReader;

    public MainWindowViewModel()
    {
      _twitterConfigSettings = new AppConfigSettings();
      _twitterReader = new TwitterReader.TwitterReader(_twitterConfigSettings);
      _twitterReader.OnNewTweet += tweet => Dispatcher.CurrentDispatcher.Invoke(() => FeedsCount++);
      RegisterMessages();
      LoadDockerInstances();
    }

    private async void LoadDockerInstances()
    {
      var dockerCfg = ConfigurationManager.GetSection(DockerConfigSection.SectionName) as DockerConfigSection;
      var azureCfg = ConfigurationManager.GetSection(AzureConfigSection.SectionName) as AzureConfigSection;
      var azureController = new AzureController(azureCfg.AzureSubscription.SubscriptionId, azureCfg.AzureSubscription.Base64encodedCertificate);
      var dockerInstanceViewModels = dockerCfg.DockerInstances.Select((i, idx) => new DockerInstanceViewModel(
        new DockerInstance(i.Host, i.Port, i.Username, i.Password), 
        azureController.GetVirtualMachineByHostnameAsync("CLC-NoSql", i.Host),
        idx + 1));
      DockerInstanceViewModels = new ObservableCollection<DockerInstanceViewModel>(dockerInstanceViewModels);
    }

    private void RegisterMessages()
    {
      Messenger.Default.Register<IsLoadingMessage>(this, m =>
      {
        IsLoading = m.IsLoading;
        IsLoadingReason = m.Reason;
      });
    }

    private bool _isLoading;
    public bool IsLoading
    {
      get { return _isLoading; }
      set { Set(ref _isLoading, value); }
    }

    private string _isLoadingReason;
    public string IsLoadingReason
    {
      get { return _isLoadingReason; }
      set { Set(ref _isLoadingReason, value); }
    }

    public SnackbarMessageQueue MessageQueue { get; } = new SnackbarMessageQueue();

    private string _pin;
    public string Pin
    {
      get { return _pin; }
      set { Set(ref _pin, value); }
    }

    private string _webAddress;
    public string WebAddress
    {
      get { return _webAddress; }
      set { Set(ref _webAddress, value); }
    }

    private OAuthTokens _tokens;
    private OAuthTokens Tokens
    {
      get { return _tokens; }
      set { Set(ref _tokens, value); }
    }

    private string _accessToken;
    public string AccessToken
    {
      get { return _accessToken; }
      set { Set(ref _accessToken, value); }
    }

    private bool _isFeedReadingRunning;
    public bool IsFeedReadingRunning
    {
      get { return _isFeedReadingRunning; }
      set { Set(ref _isFeedReadingRunning, value); }
    }

    private int _feedsCount;
    public int FeedsCount
    {
      get { return _feedsCount; }
      set { Set(ref _feedsCount, value); }
    }

    private ICommand _getPinCommand;
    public ICommand GetPinCommand => _getPinCommand ?? (_getPinCommand = new RelayCommand(GetPin));

    private ICommand _startFeedReadingCommand;
    public ICommand StartFeedReadingCommand => _startFeedReadingCommand ?? (_startFeedReadingCommand = new RelayCommand(async () => await StartReadingFeed(), () => !string.IsNullOrEmpty(Pin) && !IsFeedReadingRunning));

    private ICommand _stopFeedReadingCommand;
    public ICommand StopFeedReadingCommand => _stopFeedReadingCommand ?? (_stopFeedReadingCommand = new RelayCommand(StopReadingFeed, () => IsFeedReadingRunning));

    private async void GetPin()
    {
      try
      {
        Tokens = await Twitter.GetRequestToken(_twitterConfigSettings);
        Process.Start(new ProcessStartInfo(Twitter.GetRequestUrl(Tokens)));
      }
      catch (WebException ex) when (ex.Message.Contains("401"))
      {
        MessageQueue.Enqueue("Got an 401 unauthorized error. Did you set up the Twitter consumer key and consumer secret correctly?", "DISMISS", () => { });
      }
    }

    private async Task StartReadingFeed()
    {
      try
      {
        var tokens = await Twitter.GetAccessToken(_twitterConfigSettings, Tokens.OAuthToken, Tokens.OAuthSecret, Pin);
        Pin = string.Empty;
        if (tokens == null) return;

        if (!string.IsNullOrEmpty(tokens.UserId))
        {
          IsFeedReadingRunning = true;
          MessageQueue.Enqueue("Loading of Twitter messages started.");
          await _twitterReader.StartAsync(tokens.OAuthToken, tokens.OAuthSecret);
        }
        else
        {
          MessageQueue.Enqueue("You are not propertly authenticated with your Twitter account.", "DISMISS", () => { });
        }
      }
      catch (WebException ex) when (ex.Message.Contains("401"))
      {
        MessageQueue.Enqueue("Got an 401 unauthorized error. Are you properly authenticated and did you use a new PIN?", "DISMISS", () => { });
      }
      catch (Exception ex)
      {
        MessageQueue.Enqueue("An error occured. Are you using a correct PIN?", "DISMISS", () => { });
      }
    }

    private void StopReadingFeed()
    {
      _twitterReader.Stop();
      IsFeedReadingRunning = false;
      MessageQueue.Enqueue("Loading of Twitter messages stopped.");
    }

    private ObservableCollection<DockerInstanceViewModel> _dockerInstanceViewModels; 
    public ObservableCollection<DockerInstanceViewModel> DockerInstanceViewModels
    {
      get { return _dockerInstanceViewModels; }
      set { Set(ref _dockerInstanceViewModels, value); }
    } 
  }
}
