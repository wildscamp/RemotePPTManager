using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Wilds.IO;

namespace Wilds.Apps.RemotePPTManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private FolderWatcher folderWatcher;

        private string searchDirectory;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded_1(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrEmpty(Properties.Settings.Default.SettingsUpgraded))
            {
                // upgrade settings from previous version
                Properties.Settings.Default.Upgrade();

                Properties.Settings.Default.SettingsUpgraded = "true";
                Properties.Settings.Default.Save();
            }

            string version = System.Reflection.Assembly.GetEntryAssembly().GetName().Version.ToString();
            lblVersion.Content = String.Format("v{0}", version);

            folderWatcher = new FolderWatcher();
            folderWatcher.CheckInterval = 1000;
            folderWatcher.Filter = "^[^~](.*?.pptx)$";
            folderWatcher.FilterIsRegex = true;
            folderWatcher.Changed += folderWatcher_Changed;

            string folderToWatch = Properties.Settings.Default.FolderToWatch;

            if (!String.IsNullOrEmpty(folderToWatch) && System.IO.Directory.Exists(folderToWatch))
            {
                this.SetSearchDirectory(folderToWatch);

                if (Properties.Settings.Default.AutoWatch == "true")
                {
                    this.chkWatchOnStart.IsChecked = true;
                    this.StartStopWatching();
                }

                if (Properties.Settings.Default.AutoLaunchLastFile == "true")
                {
                    this.chkAutoLaunchLastFile.IsChecked = true;

                    this.LaunchLastFile(false);
                }

                this.SetLastLaunchedFile(Properties.Settings.Default.LastLaunchedFile, Properties.Settings.Default.LastLaunchTime);
            } // else silently ignore as if the file doesn't exist in the setting.
        }

        private void SetLastLaunchedFile(string file, string lastLaunchTime)
        {
            if (!String.IsNullOrEmpty(file))
            {
                this.lblLastLunchedFile.Content = file;

                if (!String.IsNullOrEmpty(lastLaunchTime))
                {
                    this.lblLastLunchedFile.ToolTip = String.Format("Last Launched: {0}", lastLaunchTime);

                    if (!lastLaunchTime.Equals(Properties.Settings.Default.LastLaunchTime))
                    {
                        Properties.Settings.Default.LastLaunchTime = lastLaunchTime;
                        Properties.Settings.Default.Save();
                    }
                }

                if (System.IO.File.Exists(file))
                {
                    this.btnLaunchFile.IsEnabled = true;
                }

                if (!file.Equals(Properties.Settings.Default.LastLaunchedFile))
                {
                    Properties.Settings.Default.LastLaunchedFile = file;
                    Properties.Settings.Default.Save();
                }
            }
        }

        private void LaunchLastFile(bool displayError)
        {
            if (!String.IsNullOrEmpty(Properties.Settings.Default.LastLaunchedFile) && System.IO.File.Exists(Properties.Settings.Default.LastLaunchedFile))
            {
                this.RelaunchPPT(Properties.Settings.Default.LastLaunchedFile);
                this.WindowState = System.Windows.WindowState.Minimized;
            }
            else if (displayError)
            {
                MessageBox.Show("Could not launch the last file because it no longer exists.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        void folderWatcher_Changed(object sender, FolderWatcherChangedEventArgs e)
        {
            //Console.WriteLine("Changed - Count: {0}", e.Changes.Count());

            //foreach (FolderWatcherChangeEvent arg in e.Changes)
            //{
            //    Console.WriteLine("\tAction: {0}; Path: {1}", arg.Action, arg.File.FullPath);
            //}

            if (e.Changes.Where(x => x.Action == FolderWatcherAction.Created || x.Action == FolderWatcherAction.Modified).Count() > 0)  // only do something on file creation or modification
            {

                FolderWatcherChangeEvent file = e.Changes.Where(x => x.Action == FolderWatcherAction.Created || x.Action == FolderWatcherAction.Modified).OrderBy(x => x.File.DateModified).Last();

                //Console.WriteLine("Relaunch: {0}", file.File.FileName);

                this.RelaunchPPT(file.File.FullPath);
            }
        }

        private void RelaunchPPT(string file)
        {

            // kill all instances of PowerPoint

            Process[] processlist = Process.GetProcesses();

            var pptProcess = from p in processlist
                                where p.ProcessName.Equals("POWERPNT", StringComparison.CurrentCultureIgnoreCase)
                                select p;

            if (pptProcess.Count() > 0)
            {
                foreach (Process p in pptProcess)
                {
                    p.Kill();
                }
            }

            // re-launch PPT w/ updated file and auto launch show.

            Process newPptProcess = new Process();

            newPptProcess.StartInfo.FileName = "powerpnt";
            newPptProcess.StartInfo.Arguments = String.Format("/s \"{0}\"", file);

            newPptProcess.Start();

            // This method can be called from a different thread (when events are raised), and therefore requires brokering to update the UI to prevent
            // runtime errors.
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                this.SetLastLaunchedFile(file, DateTime.Now.ToString());
            }));
        }

        private void btnStartStopWatching_Click(object sender, RoutedEventArgs e)
        {
            this.StartStopWatching();
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            WPFFolderBrowser.WPFFolderBrowserDialog wfbd = new WPFFolderBrowser.WPFFolderBrowserDialog("Please select a folder to watch...");

            if ((bool)wfbd.ShowDialog())
            {
                this.SetSearchDirectory(wfbd.FileName);

                Properties.Settings.Default.FolderToWatch = wfbd.FileName;

                Properties.Settings.Default.Save();
            }


        }

        private void SetSearchDirectory(string Directory)
        {
            searchDirectory = Directory;

            folderWatcher.FolderToWatch = searchDirectory;

            lblFileToWatch.Content = searchDirectory;
        }

        private void StartStopWatching()
        {
            if (String.IsNullOrEmpty(searchDirectory) || !System.IO.Directory.Exists(searchDirectory))
            {
                System.Windows.MessageBox.Show("Please select a directory to watch before attempting to watch it.", "No Directory Selected");

                return;
            }

            if (!folderWatcher.Watch)
            {
                this.btnBrowse.IsEnabled = false;

                folderWatcher.Watch = true;

                this.btnStartStopWatching.Content = "Stop Watching";
            } else {
                folderWatcher.Watch = false;

                this.btnStartStopWatching.Content = "Start Watching";

                this.btnBrowse.IsEnabled = true;
            }
        }

        private void Window_Closing_1(object sender, System.ComponentModel.CancelEventArgs e)
        {
            folderWatcher.Watch = false;
        }

        private void chkWatchOnStart_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AutoWatch = "true";

            Properties.Settings.Default.Save();
        }

        private void chkWatchOnStart_Unchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AutoWatch = "false";

            Properties.Settings.Default.Save();
        }

        private void chkAutoLaunchLastFile_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AutoLaunchLastFile = "true";

            Properties.Settings.Default.Save();
        }

        private void chkAutoLaunchLastFile_Unchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AutoLaunchLastFile = "false";

            Properties.Settings.Default.Save();
        }

        private void btnLaunchFile_Click(object sender, RoutedEventArgs e)
        {
            this.LaunchLastFile(true);
        }

        
    }
}
