using System.Management.Automation;
using System.Runtime.Serialization;
using Microsoft.PowerShell.Host.ISE;
using NLog;
using NLog.Targets;
using Ookii.Dialogs.Wpf;
using PsISEProjectExplorer.Enums;
using PsISEProjectExplorer.UI.Helpers;
using PsISEProjectExplorer.UI.IseIntegration;
using PsISEProjectExplorer.UI.ViewModel;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PsISEProjectExplorer
{
    /// <summary>
    /// Interaction logic for ProjectExplorerWindow.xaml
    /// </summary>
    public partial class ProjectExplorerWindow : IAddOnToolHostObject
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static string LogFileName;
        private readonly MainViewModel mainViewModel;
        private Point dragStartPoint;
        private ObjectModelRoot hostObject;
        private bool isContextMenuOpened;

        public string SelectedNode { get; set; }
        public String PendingScript { get; set; }
        public String CheckinScript { get; set; }
        public String UndoScript { get; set; }
        public String HistoryScript { get; set; }
        public String CheckoutScript { get; set; }
        public String AddToControlScript { get; set; }
        public String GetLatestScript { get; set; }

        // Entry point to the ISE object model.
        public ObjectModelRoot HostObject
        {
            get { throw new InvalidOperationException("Should not use HostObject in user control - please use IseIntegrator class."); }
            set
            {
                this.hostObject = value;
                this.mainViewModel.IseIntegrator = new IseIntegrator(this.hostObject);
            }
        }

        public ProjectExplorerWindow()
        {
            SelectedNode = "none";
            this.ConfigureLogging();
            this.mainViewModel = new MainViewModel();
            this.mainViewModel.ActiveDocumentSyncEvent += OnActiveDocumentSyncEvent;
            this.DataContext = this.mainViewModel;
            InitializeComponent();
            this.Dispatcher.UnhandledException += DispatcherUnhandledExceptionHandler;
        }

        private static void DispatcherUnhandledExceptionHandler(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            Exception e = args.Exception;
            Logger.Error(e, "Unhandled Dispatcher exception");

            StringBuilder sources = new StringBuilder().Append("Sources: ");
            string firstSource = null;
            var innerException = e.InnerException;

            while (innerException != null)
            {
                if (firstSource == null)
                {
                    firstSource = innerException.Source;
                }

                sources.Append(innerException.Source).Append(",");
                innerException = innerException.InnerException;
            }

            Logger.Error(sources.ToString());
            args.Handled = true;
        }

        private void OnActiveDocumentSyncEvent(object sender, IseEventArgs args)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (this.mainViewModel.SyncWithActiveDocument)
                {
                    this.LocateFileInTree();
                }
            });
        }

        public void GoToDefinition()
        {
            Application.Current.Dispatcher.Invoke(() => this.mainViewModel.GoToDefinition());
        }

        public void FindAllOccurrences()
        {
            Application.Current.Dispatcher.Invoke(() => this.mainViewModel.FindAllOccurrences());
        }

        public void LocateFileInTree()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string path = this.mainViewModel.IseIntegrator.SelectedFilePath;
                if (path == null)
                {
                    return;
                }

                var selectedItem = this.SearchResults.SelectedItem as TreeViewEntryItemModel;
                if (selectedItem != null && selectedItem.Path.StartsWith(path))
                {
                    return;
                }

                TreeViewEntryItemModel item = this.mainViewModel.TreeViewModel.FindTreeViewEntryItemByPath(path);
                if (item == null)
                {
                    return;
                }

                SearchResults.ExpandAndSelectItem(item);
            });
        }

        public void FindInFiles()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                this.mainViewModel.FindInFiles();
                this.TextBoxSearchText.Focus();
            });
        }

        public void CloseAllButThis()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                this.mainViewModel.IseIntegrator.CloseAllButThis();
            });
        }

        private void ChangeWorkspace_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                SelectedPath = this.mainViewModel.WorkspaceDirectoryModel.CurrentWorkspaceDirectory,
                Description = "Please select the new workspace folder.",
                UseDescriptionForTitle = true
            };

            bool? dialogResult = dialog.ShowDialog();
            if (dialogResult != null && dialogResult.Value)
            {
                if (dialog.SelectedPath == Path.GetPathRoot(dialog.SelectedPath))
                {
                    MessageBoxHelper.ShowError("Cannot use root directory ('" + dialog.SelectedPath + "'). Please select another path.");
                }
                else
                {
                    this.mainViewModel.WorkspaceDirectoryModel.SetWorkspaceDirectory(dialog.SelectedPath);
                    this.mainViewModel.WorkspaceDirectoryModel.AutoUpdateRootDirectory = false;
                }
            }
        }

        private void RefreshDirectoryStructure_Click(object sender, RoutedEventArgs e)
        {
            this.mainViewModel.ReindexSearchTree();
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            this.SearchResults.CollapseAll();
        }

        private void ConfigureLogging()
        {
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            LogFileName = Path.Combine(assemblyFolder, "NLog.config");
            var config = new NLog.Config.XmlLoggingConfiguration(LogFileName);
            LogManager.Configuration = config;

            var targets = config.AllTargets;
            if (targets != null && targets.Any() && targets.First() is FileTarget)
            {
                LogFileName = ((FileTarget)targets.First()).FileName.Render(new LogEventInfo());
            }
        }

        private void SearchResults_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = this.SearchResults.FindItemFromSource((DependencyObject)e.OriginalSource);
            if (item == null && this.SearchResults.SelectedItem != null)
            {
                ((TreeViewEntryItemModel)this.SearchResults.SelectedItem).IsSelected = false;
            }
            else
            {
                //this.SelectedNode = this.SearchResults.SelectedItem != null ? ((TreeViewEntryItemModel)this.SearchResults.SelectedItem).Path : "Empty";
                this.SelectedNode = item != null ? ((TreeViewEntryItemModel)item.DataContext).Path : "Empty";
            }
            
            this.dragStartPoint = e.GetPosition(null);

            if (e.ClickCount > 1)
            {
                this.mainViewModel.TreeViewModel.OpenItem((TreeViewEntryItemModel)this.SearchResults.SelectedItem, this.mainViewModel.SearchOptions);
                e.Handled = true;
            }
        }

        private void SearchResults_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            TreeViewEntryItemModel item;
            if (!this.SearchResults.SelectItemFromSource((DependencyObject)e.OriginalSource))
            {
                this.SearchResults.ContextMenu = this.SearchResults.Resources["EmptyContext"] as ContextMenu;

                item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
                if (item != null)
                {
                    item.IsSelected = false;
                }

                return;
            }

            item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            if (item == null)
            {
                // should not happen
                this.SearchResults.ContextMenu = null;
            }
            else
                switch (item.NodeType)
                {
                    case NodeType.Directory:
                        this.SearchResults.ContextMenu = this.SearchResults.Resources["DirectoryContext"] as ContextMenu;
                        break;
                    case NodeType.File:
                        this.SearchResults.ContextMenu = this.SearchResults.Resources["FileContext"] as ContextMenu;
                        break;
                    default:
                        this.SearchResults.ContextMenu = null;
                        break;
                }
        }

        private void SearchResults_KeyUp(object sender, KeyEventArgs e)
        {
            var selectedItem = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            if (e.Key == Key.Enter)
            {
                this.mainViewModel.TreeViewModel.OpenItem(selectedItem, this.mainViewModel.SearchOptions);
            }
        }

        private void TextBoxSearchClear_Click(object sender, RoutedEventArgs e)
        {
            this.mainViewModel.SearchText = string.Empty;
        }

        private void SearchResults_AddDirectory(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            this.mainViewModel.TreeViewModel.AddNewTreeItem(item, NodeType.Directory);
        }

        private void SearchResults_AddFile(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            this.mainViewModel.TreeViewModel.AddNewTreeItem(item, NodeType.File);
        }

        private void SearchResults_Rename(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            if (item == null)
            {
                return;
            }

            this.mainViewModel.TreeViewModel.StartEditingTreeItem(item);
        }

        private void SearchResults_Delete(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            if (item == null)
            {
                return;
            }

            this.mainViewModel.TreeViewModel.DeleteTreeItem(item);
        }

        private void SearchResults_EditKeyDown(object sender, KeyEventArgs e)
        {
            var item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            if (item == null)
            {
                return;
            }

            var newValue = ((TextBox)sender).Text;

            switch (e.Key)
            {
                case Key.Escape:
                    this.mainViewModel.TreeViewModel.EndTreeEdit(newValue, false, item, !this.mainViewModel.SearchInFiles);
                    e.Handled = true;
                    return;
                case Key.Enter:
                    this.mainViewModel.TreeViewModel.EndTreeEdit(newValue, true, item, !this.mainViewModel.SearchInFiles);
                    e.Handled = true;
                    return;
            }
        }

        private void SearchResults_KeyDown(object sender, KeyEventArgs e)
        {
            var item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            if (item == null || item.IsBeingEdited)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Delete:
                    this.mainViewModel.TreeViewModel.DeleteTreeItem(item);
                    e.Handled = true;
                    return;
                case Key.F2:
                    this.mainViewModel.TreeViewModel.StartEditingTreeItem(item);
                    e.Handled = true;
                    return;
                case Key.Enter:
                    this.mainViewModel.TreeViewModel.OpenItem((TreeViewEntryItemModel)this.SearchResults.SelectedItem, this.mainViewModel.SearchOptions);
                    e.Handled = true;
                    return;
            }
        }

        private void SearchResults_EndEdit(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            if (item == null)
            {
                return;
            }

            var newValue = ((TextBox)sender).Text;
            this.mainViewModel.TreeViewModel.EndTreeEdit(newValue, true, item, !this.mainViewModel.SearchInFiles);
        }

        private void SearchResults_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var selectedItem = this.SearchResults.SelectedItem as TreeViewEntryItemModel;
                if ((selectedItem != null && selectedItem.IsBeingEdited) || this.isContextMenuOpened)
                {
                    return;
                }

                var mousePos = e.GetPosition(null);
                var diff = this.dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance
                    || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var treeView = sender as TreeView;
                    if (treeView == null)
                    {
                        return;
                    }

                    var treeViewItem = treeView.FindItemFromSource((DependencyObject)e.OriginalSource);
                    if (treeViewItem == null)
                    {
                        return;
                    }

                    var item = treeView.SelectedItem as TreeViewEntryItemModel;
                    if (item == null)
                    {
                        return;
                    }

                    var dragData = new DataObject(item);
                    DragDrop.DoDragDrop(treeViewItem, dragData, DragDropEffects.Move);
                }
            }
        }

        private void SearchResults_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TreeViewEntryItemModel)))
            {
                var item = e.Data.GetData(typeof(TreeViewEntryItemModel)) as TreeViewEntryItemModel;
                var treeView = sender as TreeView;

                if (treeView == null || item == null)
                {
                    return;
                }

                var treeViewItem = treeView.FindItemFromSource((DependencyObject)e.OriginalSource);

                TreeViewEntryItemModel dropTarget = null;

                if (treeViewItem != null)
                {
                    dropTarget = treeViewItem.Header as TreeViewEntryItemModel;
                    if (dropTarget == null)
                    {
                        return;
                    }
                }

                if (item != dropTarget)
                {
                    this.mainViewModel.TreeViewModel.MoveTreeItem(item, dropTarget, this.mainViewModel.WorkspaceDirectoryModel.CurrentWorkspaceDirectory);
                }
            }
        }

        private void SearchResults_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TreeViewEntryItemModel)))
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void SearchResults_ContextMenuClosed(object sender, RoutedEventArgs e)
        {
            this.isContextMenuOpened = false;
        }

        private void SearchResults_ContextMenuOpened(object sender, RoutedEventArgs e)
        {
            this.isContextMenuOpened = true;
        }

        private void SearchResults_OpenInExplorer(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewEntryItemModel)this.SearchResults.SelectedItem;
            if (item == null)
            {
                return;
            }

            try
            {
                switch (item.NodeType)
                {
                    case NodeType.Directory:
                        Process.Start(item.Path);
                        break;
                    case NodeType.File:
                        Process.Start("explorer.exe", "/select, \"" + item.Path + "\"");
                        break;
                }

            }
            catch (Exception ex)
            {
                MessageBoxHelper.ShowError(string.Format("Cannot open path: '{0}' - {1}.", item.Path, ex.Message));
            }
        }

        private void SearchResults_PendingChanges(object sender, RoutedEventArgs e)
        {
            hostObject.CurrentPowerShellTab.Invoke(this.PendingScript);
        }

        private void SearchResults_CheckinChanges(object sender, RoutedEventArgs e)
        {
            hostObject.CurrentPowerShellTab.Invoke(this.CheckinScript);
        }

        private void SearchResults_Checkout(object sender, RoutedEventArgs e)
        {
            hostObject.CurrentPowerShellTab.Invoke(this.CheckoutScript);
        }

        private void SearchResults_AddToControl(object sender, RoutedEventArgs e)
        {
            hostObject.CurrentPowerShellTab.Invoke(this.AddToControlScript);
        }

        private void SearchResults_History(object sender, RoutedEventArgs e)
        {
            hostObject.CurrentPowerShellTab.Invoke(this.HistoryScript);
        }

        private void SearchResults_UndoChanges(object sender, RoutedEventArgs e)
        {
            hostObject.CurrentPowerShellTab.Invoke(this.UndoScript);
        }

        private void SearchResults_GetLatestVersion(object sender, RoutedEventArgs e)
        {
            hostObject.CurrentPowerShellTab.Invoke(this.GetLatestScript);
        }

    }
}
