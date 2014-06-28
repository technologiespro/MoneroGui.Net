﻿using Hardcodet.Wpf.TaskbarNotification;
using Jojatekok.MoneroAPI;
using Ookii.Dialogs.Wpf;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Jojatekok.MoneroGUI.Windows
{
    public partial class MainWindow : IDisposable
    {
        private bool IsDisposing { get; set; }

        private static MoneroClient MoneroClient { get; set; }
        private static Logger LoggerDaemon { get; set; }
        private static Logger LoggerWallet { get; set; }

        public static readonly RoutedUICommand CommandShowOrHideWindow = new RoutedUICommand();
        public static readonly RoutedUICommand CommandSendCoins = new RoutedUICommand();
        public static readonly RoutedUICommand CommandShowTransactions = new RoutedUICommand();

        public static readonly RoutedUICommand CommandBackupWallet = new RoutedUICommand();
        public static readonly RoutedUICommand CommandExport = new RoutedUICommand();
        public static readonly RoutedUICommand CommandExit = new RoutedUICommand();
        public static readonly RoutedUICommand CommandOptions = new RoutedUICommand();
        public static readonly RoutedUICommand CommandShowDebugWindow = new RoutedUICommand();
        public static readonly RoutedUICommand CommandShowAboutWindow = new RoutedUICommand();

        private DebugWindow DebugWindow { get; set; }

        public MainWindow()
        {
            StaticObjects.MainWindow = this;

            Icon = StaticObjects.ApplicationIconImage;
            InitializeComponent();
            TaskbarIcon.Icon = StaticObjects.ApplicationIcon;

            // Register commands which can be used from the tray
            CommandManager.RegisterClassCommandBinding(typeof(Popup), CommandBindingShowOrHideWindow);
            CommandManager.RegisterClassCommandBinding(typeof(Popup), CommandBindingSendCoins);
            CommandManager.RegisterClassCommandBinding(typeof(Popup), CommandBindingShowTransactions);
            CommandManager.RegisterClassCommandBinding(typeof(Popup), CommandBindingOptions);
            CommandManager.RegisterClassCommandBinding(typeof(Popup), CommandBindingDebugWindow);
            CommandManager.RegisterClassCommandBinding(typeof(Popup), CommandBindingExit);

            MoneroClient = StaticObjects.MoneroClient;
            LoggerDaemon = StaticObjects.LoggerDaemon;
            LoggerWallet = StaticObjects.LoggerWallet;

            StartDaemon();
            Loaded += delegate { Dispatcher.Invoke(StartWallet); };
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (IsDisposing) return;

            CommandShowOrHideWindow.Execute(null, this);
            e.Cancel = true;
        }

        private void CommandShowOrHideWindow_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            string bindingPath;

            if (Visibility == Visibility.Visible) {
                Visibility = Visibility.Collapsed;
                bindingPath = "MenuShowWindow";

            } else {
                Visibility = Visibility.Visible;
                bindingPath = "MenuHideWindow";
            }

            MenuItemShowOrHideWindow.SetBinding(
                HeaderedItemsControl.HeaderProperty,
                new Binding {
                    Path = new PropertyPath(bindingPath),
                    Source = CultureManager.ResourceProvider,
                    Mode = BindingMode.OneWay
                }
            );

            this.ActivateWindowOrLastChild();
        }

        private void CommandSendCoins_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TabItemSendCoins.IsSelected) {
                this.SetFocusedElement(TabItemSendCoins);
            }

            this.RestoreWindowStateFromMinimized();
        }

        private void CommandShowTransactions_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TabItemTransactions.IsSelected) {
                this.SetFocusedElement(TabItemTransactions);
            }

            this.RestoreWindowStateFromMinimized();
        }

        private void CommandBackupWallet_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog { RootFolder = Environment.SpecialFolder.MyComputer };
            
            if (dialog.ShowDialog() == true) {
                MoneroClient.Wallet.BackupAsync(dialog.SelectedPath);
            }
        }

        private void CommandExport_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Debug.Assert(TabControl.SelectedContent as IExportable != null, "TabControl.SelectedContent as IExportable != null");
            (TabControl.SelectedContent as IExportable).Export();
        }

        private void CommandExit_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (SettingsManager.General.IsSafeShutdownEnabled) {
                if (IsDisposing) return;

                Visibility = Visibility.Hidden;

                TaskbarIcon.ContextMenu = null;
                TaskbarIcon.ToolTipText = Properties.Resources.TaskbarShutdown;

                TaskbarIcon.ShowBalloonTip(Properties.Resources.TaskbarShutdown,
                                           Properties.Resources.TaskbarDoNotInterruptProcess,
                                           BalloonIcon.Info);

                Task.Factory.StartNew(Dispose);
                
            } else {
                IsDisposing = true;
                Application.Current.Shutdown();
            }
        }

        private void CommandOptions_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            new OptionsWindow(this).ShowDialog();

            // This is needed for the behavior of the tray icon
            if (WindowState != WindowState.Minimized) this.ActivateWindowOrLastChild();
        }

        private void CommandShowDebugWindow_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (DebugWindow == null) {
                DebugWindow = new DebugWindow();
                DebugWindow.Closed += delegate { DebugWindow = null; };
                DebugWindow.Show();

            } else {
                DebugWindow.Activate();
            }
        }

        private void CommandShowAboutWindow_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            new AboutWindow(this).ShowDialog();
        }

        private void CommandExport_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = TabControl.SelectedContent is IExportable;
        }

        private void StartDaemon()
        {
            var daemon = MoneroClient.Daemon;
            daemon.OnLogMessage += Daemon_OnLogMessage;
            daemon.NetworkInformationChanging += Daemon_NetworkInformationChanging;
            daemon.BlockchainSynced += Daemon_BlockchainSynced;

            MoneroClient.StartDaemon();
        }

        private void StartWallet()
        {
            var wallet = MoneroClient.Wallet;
            wallet.OnLogMessage += Wallet_OnLogMessage;
            wallet.PassphraseRequested += Wallet_PassphraseRequested;
            wallet.AddressReceived += Wallet_AddressReceived;
            wallet.TransactionReceived += Wallet_TransactionReceived;
            wallet.BalanceChanging += Wallet_BalanceChanging;

            MoneroClient.StartWallet();

            OverviewView.ViewModel.TransactionDataSource = wallet.Transactions;
            TransactionsView.ViewModel.DataSource = wallet.Transactions;
        }

        private static void Daemon_OnLogMessage(object sender, string e)
        {
            LoggerDaemon.Log(e);
        }

        private void Daemon_NetworkInformationChanging(object sender, NetworkInformationChangingEventArgs e)
        {
            var newValue = e.NewValue;

            var connectionCount = (ushort)(newValue.ConnectionCountIncoming + newValue.ConnectionCountOutgoing);
            var syncBarText = string.Format(Helper.InvariantCulture,
                                            Properties.Resources.StatusBarSyncTextMain,
                                            newValue.BlockHeightRemaining,
                                            newValue.BlockTimeRemaining.ToStringReadable());

            InvokeForDataChanging(() => {
                var statusBarViewModel = StatusBar.ViewModel;

                statusBarViewModel.ConnectionCount = connectionCount;
                statusBarViewModel.BlocksTotal = newValue.BlockHeightTotal;
                statusBarViewModel.BlocksDownloaded = newValue.BlockHeightDownloaded;
                statusBarViewModel.SyncBarText = syncBarText;
                statusBarViewModel.SyncBarVisibility = Visibility.Visible;
            });
        }

        private void Daemon_BlockchainSynced(object sender, EventArgs e)
        {
            InvokeForDataChanging(() => StatusBar.ViewModel.SyncBarVisibility = Visibility.Hidden);
        }

        private static void Wallet_OnLogMessage(object sender, string e)
        {
            LoggerWallet.Log(e);
        }

        private void Wallet_PassphraseRequested(object sender, PassphraseRequestedEventArgs e)
        {
            Dispatcher.Invoke(() => {
                if (e.IsFirstTime) {
                    // Let the user set the wallet's passphrase for the first time
                    var dialog = new WalletChangePassphraseWindow(this, false);
                    if (dialog.ShowDialog() == true) {
                        MoneroClient.Wallet.Passphrase = dialog.NewPassphrase;
                    } else {
                        MoneroClient.Wallet.Passphrase = null;
                    }

                } else {
                    // Request the wallet's passphrase in order to unlock it
                    var dialog = new WalletUnlockWindow(this);
                    if (dialog.ShowDialog() == true) {
                        MoneroClient.Wallet.Passphrase = dialog.Passphrase;
                    }
                }
            });
        }

        private void Wallet_AddressReceived(object sender, AddressReceivedEventArgs e)
        {
            InvokeForDataChanging(() => OverviewView.ViewModel.Address = e.Address);
        }

        private void Wallet_TransactionReceived(object sender, TransactionReceivedEventArgs e)
        {
            var transaction = e.Transaction;

            string balloonTitle;
            switch (transaction.Type) {
                case TransactionType.Receive:
                    balloonTitle = Properties.Resources.TaskbarTransactionIncoming;
                    break;

                case TransactionType.Send:
                    balloonTitle = Properties.Resources.TaskbarTransactionOutgoing;
                    break;

                default:
                    balloonTitle = Properties.Resources.TaskbarTransactionUnknownType;
                    break;
            }

            var balloonMessageExtra = string.Empty;
            if (transaction.Type == TransactionType.Unknown) {
                balloonMessageExtra = Properties.Resources.TransactionsSpendable + ": " +
                                      Dispatcher.Invoke(() => ConverterBooleanToString.Provider.Convert(transaction.IsAmountSpendable, typeof(string), null, Helper.InvariantCulture)) + Helper.NewLineString;
            }

            var balloonMessage = Properties.Resources.TextAmount + ": " + transaction.Amount.ToString(StaticObjects.StringFormatCurrencyDefault, Helper.InvariantCulture) + " " + Properties.Resources.TextCurrencyCode + Helper.NewLineString +
                                 balloonMessageExtra +
                                 Helper.NewLineString +
                                 Properties.Resources.TransactionsTransactionId + ": " + transaction.TransactionId;

            Dispatcher.Invoke(() => TaskbarIcon.ShowBalloonTip(balloonTitle, balloonMessage, BalloonIcon.Info));
        }

        private void Wallet_BalanceChanging(object sender, BalanceChangingEventArgs e)
        {
            var newValue = e.NewValue;

            InvokeForDataChanging(() => {
                var overviewViewModel = OverviewView.ViewModel;
                overviewViewModel.BalanceSpendable = newValue.Spendable;
                overviewViewModel.BalanceUnconfirmed = newValue.Unconfirmed;

                var sendCoinsViewModel = SendCoinsView.ViewModel;
                sendCoinsViewModel.BalanceSpendable = newValue.Spendable;
                sendCoinsViewModel.IsSendingEnabled = true;
            });
        }

        private void InvokeForDataChanging(Action callback)
        {
            Dispatcher.Invoke(callback, DispatcherPriority.DataBind);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing && !IsDisposing) {
                IsDisposing = true;
                
                if (MoneroClient != null) {
                    MoneroClient.Dispose();
                }

                Dispatcher.Invoke(Application.Current.Shutdown);
            }
        }
    }
}
