﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Newtonsoft.Json.Linq;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DoritoPatcherWPF
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string SettingsFileName = "dewrito_prefs.yaml";
        private readonly bool embedded = true;
        private readonly SHA1 hasher = SHA1.Create();
        private readonly bool silentStart;
        private readonly string[] skipFileExtensions = {".bik"};

        private readonly string[] skipFiles =
        {
            "eldorado.exe", "game.cfg", "tags.dat", "font_package.bin", "binkw32.dll",
            "crash_reporter.exe", "game_local.cfg"
        };

        private readonly string[] skipFolders = {".inn.meta.dir", ".inn.tmp.dir", "Frost", "tpi", "bink"};
        public string BasePath = Directory.GetCurrentDirectory();
        private Dictionary<string, string> fileHashes;
        private List<string> filesToDownload;
        private bool isPlayEnabled;
        private bool isUpdating;
        private JToken latestUpdate;
        private string latestUpdateVersion;
        private DewritoSettings settings;
        private JObject settingsJson;
        private SettingsViewModel settingsViewModel;
        private JObject updateJson;
        private Thread validateThread;

        public MainWindow()
        {
            var e = Environment.GetCommandLineArgs();

            foreach (var entry in e)
            {
                if (entry.StartsWith("-"))
                {
                    switch (entry)
                    {
                        case "-silent":
                            silentStart = true;
                            break;
                    }
                }
            }

            AppDomain.CurrentDomain.AssemblyResolve +=
                ResolveAssembly;

            // proceed starting app...

            InitializeComponent();

            var fade = (Storyboard) TryFindResource("fade");
            fade.Begin(); // Start animation
        }

        /* --- Titlebar Control --- */

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MinButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /* --- Content Panel Control --- */

        private void switchPanel(string panel, bool animationComplete)
        {
            if (!animationComplete)
            {
                var fadePanels = (Storyboard) TryFindResource("fadePanels");
                fadePanels.Completed += (sender, e) => switchPanelAnimationComplete(sender, e, panel);
                fadePanels.Begin(); // Start fadeout
            }
            else
            {
                ChangelogGrid.Visibility = Visibility.Hidden;
                Settings.Visibility = Visibility.Hidden;
                Customization.Visibility = Visibility.Hidden;
                mainButtons.Visibility = Visibility.Hidden;
                Debug.Visibility = Visibility.Hidden;
                Browser.Visibility = Visibility.Hidden;

                switch (panel)
                {
                    case "main":
                        mainButtons.Visibility = Visibility.Visible;
                        break;
                    case "browser":
                        Browser.Visibility = Visibility.Visible;
                        break;
                    case "settings":
                        Settings.Visibility = Visibility.Visible;
                        break;
                    case "custom":
                        Customization.Visibility = Visibility.Visible;
                        break;
                    case "changelog":
                        ChangelogGrid.Visibility = Visibility.Visible;
                        break;
                    case "debug":
                        Debug.Visibility = Visibility.Visible;
                        break;
                    default:
                        mainButtons.Visibility = Visibility.Visible;
                        break;
                }
                var showPanels = (Storyboard) TryFindResource("showPanels");
                showPanels.Begin(); // Start fadein
            }
        }

        private void switchPanelAnimationComplete(object sender, EventArgs e, string panel)
        {
            switchPanel(panel, true);
        }

        private void btnChangelog_Click(object sender, EventArgs e)
        {
            switchPanel("changelog", false);
        }

        private void btnDebug_Click(object sender, EventArgs e)
        {
            switchPanel("debug", false);
        }

        private void btnOkDebug_Click(object sender, EventArgs e)
        {
            switchPanel("main", false);
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            switchPanel("main", false);
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            switchPanel("settings", false);
        }

        private void btnNo_Update(object sender, EventArgs e)
        {
            if (isUpdating == true)
            {
                var StartGameNoUpdate = new MsgBoxConfirm("Are you sure you would like to bypass the update process? There could be unforeseeable consequences.");
                if (StartGameNoUpdate.ShowDialog() == false)
                {
                    if (StartGameNoUpdate.confirm)
                    {
                        isPlayEnabled = true;
                    }
                }
            }
            if (isPlayEnabled && isUpdating == true)
            {
                var sInfo = new ProcessStartInfo(BasePath + "/eldorado.exe");
                sInfo.Arguments = "-launcher";
                if (settingsViewModel.LaunchParams.WindowedMode)
                {
                    sInfo.Arguments += " -window";
                }
                if (settingsViewModel.LaunchParams.Fullscreen)
                {
                    sInfo.Arguments += " -fullscreen";
                }
                if (settingsViewModel.LaunchParams.NoVSync)
                {
                    sInfo.Arguments += " -no_vsync";
                }
                if (settingsViewModel.LaunchParams.DX9Ex)
                {
                    sInfo.Arguments += " -3d9ex";
                }
                else
                {
                    sInfo.Arguments += " -nod3d9ex";
                }
                if (settingsViewModel.LaunchParams.FPSCounter)
                {
                    sInfo.Arguments += " -show_fps";
                }

                sInfo.Arguments += " -width " + settingsViewModel.LaunchParams.Width;
                sInfo.Arguments += " -height " + settingsViewModel.LaunchParams.Height;

                try
                {
                    Process.Start(sInfo);
                }
                catch
                {
                    //MessageBox.Show("Game executable not found.");
                    var MainWindow = new MsgBoxOk("Game executable not found.");
                    MainWindow.Show();
                    MainWindow.Focus();
                }
            }
            else
            {
                return;
            }
        }

        private void btnApply2_Click(object sender, EventArgs e)
        {
            switchPanel("main", false);
        }

        private void btnCustomization_Click(object sender, EventArgs e)
        {
            switchPanel("custom", false);
        }

        private void btnApply_Click(object sender, EventArgs e)
        {
            switchPanel("main", false);
        }

        private void helmetOpen(object sender, EventArgs e)
        {
            cmbChest.Visibility = Visibility.Hidden;
            cmbShoulders.Visibility = Visibility.Hidden;
            cmbArms.Visibility = Visibility.Hidden;
            cmbLegs.Visibility = Visibility.Hidden;
        }

        private void helmetClose(object sender, EventArgs e)
        {
            cmbChest.Visibility = Visibility.Visible;
            cmbShoulders.Visibility = Visibility.Visible;
            cmbArms.Visibility = Visibility.Visible;
            cmbLegs.Visibility = Visibility.Visible;
        }

        private void chestOpen(object sender, EventArgs e)
        {
            cmbShoulders.Visibility = Visibility.Hidden;
            cmbArms.Visibility = Visibility.Hidden;
            cmbLegs.Visibility = Visibility.Hidden;
        }

        private void chestClose(object sender, EventArgs e)
        {
            cmbShoulders.Visibility = Visibility.Visible;
            cmbArms.Visibility = Visibility.Visible;
            cmbLegs.Visibility = Visibility.Visible;
        }

        private void shouldersOpen(object sender, EventArgs e)
        {
            cmbArms.Visibility = Visibility.Hidden;
            cmbLegs.Visibility = Visibility.Hidden;
        }

        private void shouldersClose(object sender, EventArgs e)
        {
            cmbArms.Visibility = Visibility.Visible;
            cmbLegs.Visibility = Visibility.Visible;
        }

        private void armsOpen(object sender, EventArgs e)
        {
            cmbLegs.Visibility = Visibility.Hidden;
        }

        private void armsClose(object sender, EventArgs e)
        {
            cmbLegs.Visibility = Visibility.Visible;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (silentStart)
            {
                WindowState = WindowState.Minimized;
            }
            using (var wc = new WebClient())
            {
                try
                {
                    ChangelogContent.Text = wc.DownloadString("http://167.114.156.21:81/honline/changelog.data");
                }
                catch
                {
                    ChangelogContent.Text = "You are offline. No changelog available.";
                }
            }

            LoadSettings();
            SaveSettings();
            isUpdating = true;
            try
            {
                settingsJson = JObject.Parse(File.ReadAllText("dewrito.json"));

                if (settingsJson["gameFiles"] == null || settingsJson["updateServiceUrl"] == null)
                {
                    SetStatus("Error reading dewrito.json: gameFiles or updateServiceUrl is missing.", Color.FromRgb(255, 0, 0));
                    SetStatusLabels("ERROR", true);
                    

                    var MainWindow = new MsgBoxOk("Could not read the dewrito.json updater configuration.");
                    MainWindow.Show();
                    MainWindow.Focus();
                    return;
                }
            }
            catch
            {
                SetStatus("Failed to read dewrito.json updater configuration.", Color.FromRgb(255, 0, 0));
                SetStatusLabels("ERROR", true);
                
                var MainWindow = new MsgBoxOk("Could not read the dewrito.json updater configuration.");
                MainWindow.Show();
                MainWindow.Focus();
                return;
            }

            // CreateHashJson();
            validateThread = new Thread(BackgroundThread);
            validateThread.Start();
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            var parentAssembly = Assembly.GetExecutingAssembly();

            var name = args.Name.Substring(0, args.Name.IndexOf(',')) + ".dll";
            var resourceName = parentAssembly.GetManifestResourceNames()
                .First(s => s.EndsWith(name));

            using (var stream = parentAssembly.GetManifestResourceStream(resourceName))
            {
                var block = new byte[stream.Length];
                stream.Read(block, 0, block.Length);
                return Assembly.Load(block);
            }
        }

        private void BackgroundThread()
        {
            if (!CompareHashesWithJson())
            {
                return;
            }

            SetStatus("Game files validated, contacting update server...", Color.FromRgb(255, 255, 255));
            
            if (!ProcessUpdateData())
            {
                bool confirm = false;

                SetStatus("Failed to retrieve update information from set update server: " + settingsJson["updateServiceUrl"].ToString(), Color.FromRgb(255, 0, 0));

                if (settingsJson["updateServiceUrl"].ToString() != "http://167.114.156.21:81/honline/update.json")
                {
                    SetStatus("Set update server is not default server...", Color.FromRgb(255, 255, 255));
                    SetStatus("Attempting to contact the default update server...", Color.FromRgb(255, 255, 255));

                    Application.Current.Dispatcher.Invoke((Action)delegate
                    {
                        var confirmWindow = new MsgBoxConfirm("Failed to retrieve update information. Do you want to try updating from the default server?");

                        if (confirmWindow.ShowDialog() == false)
                        {
                            if (confirmWindow.confirm)
                            {
                                settingsJson["updateServiceUrl"] = "http://167.114.156.21:81/honline/update.json";
                                if (!ProcessUpdateData())
                                {
                                    SetStatus("Failed to connect to the default update server.", Color.FromRgb(255, 0, 0));
                                    btnAction.Content = "PLAY";
                                    isPlayEnabled = true;
                                    btnAction.IsEnabled = true;

                                    var MainWindow = new MsgBoxOk("Failed to connect to the default update server, you can still play the game if your files aren't invalid.");
                                    MainWindow.Show();
                                    MainWindow.Focus();
                                    return;
                                }
                                else
                                {
                                    confirm = true;
                                }
                            }
                            else
                            {
                                SetStatus("Update server connection manually canceled.", Color.FromRgb(255, 0, 0));
                                btnAction.Content = "PLAY";
                                isPlayEnabled = true;
                                btnAction.IsEnabled = true;

                                var MainWindow = new MsgBoxOk("Update server connection manually canceled, you can still play the game if your files aren't invalid.");
                                MainWindow.Show();
                                MainWindow.Focus();
                                return;
                            }
                        }
                    });
                }
                else
                {
                    Application.Current.Dispatcher.Invoke((Action)delegate
                    {
                        SetStatus("Failed to retrieve update information from the default update server.", Color.FromRgb(255, 0, 0));
                        btnAction.Content = "PLAY";
                        isPlayEnabled = true;
                        btnAction.IsEnabled = true;

                        var MainWindow = new MsgBoxOk("Could not connect to the default update server, you can still play the game if your files aren't invalid.");
                        MainWindow.Show();
                        MainWindow.Focus();
                    });
                }

                if (!confirm)
                {
                    return;
                }
            }

            if (filesToDownload.Count <= 0)
            {
                SetStatus("You have the latest version! (" + latestUpdateVersion + ")", Color.FromRgb(0, 255, 0));
                isUpdating = false;

                btnAction.Dispatcher.Invoke(
                    new Action(
                        () =>
                        {
                            btnAction.Content = "PLAY GAME";

                            isPlayEnabled = true;

                            var fade = (Storyboard) TryFindResource("fade");
                            fade.Stop(); // Start animation
                            btnAction.IsEnabled = true;
                        }));

                if (silentStart)
                {
                    btnAction_Click(new object(), new RoutedEventArgs());
                }
                return;
            }

            SetStatus("An update is available. (" + latestUpdateVersion + ")", Color.FromRgb(255, 255, 0));
            isUpdating = true;
            btnAction.Dispatcher.Invoke(
                new Action(
                    () =>
                    {
                        btnAction.Content = "UPDATE";

                        var fade = (Storyboard) TryFindResource("fade");
                        fade.Stop(); // Stop
                        /*
                            Storyboard fadeStat = (Storyboard)TryFindResource("fadeStat");
                            fadeStat.Stop();	// Start
                            Storyboard fadeServer = (Storyboard)TryFindResource("fadeServer");
                            fadeServer.Stop();	// Start 
                             */
                        btnAction.IsEnabled = true;
                    }));
            if (silentStart)
            {
                //MessageBox.Show("Sorry, you need to update before the game can be started silently.", "ElDewrito Launcher");
                var MainWindow = new MsgBoxOk("Sorry, you need to update before the game can be started silently.");

                MainWindow.Show();
                MainWindow.Focus();
            }
        }

        private bool ProcessUpdateData()
        {
            try
            {
                var updateData = settingsJson["updateServiceUrl"].ToString().Replace("\"", "");
                if (updateData.StartsWith("http"))
                {
                    using (var wc = new WebClient())
                    {
                        try
                        {
                            updateData = wc.DownloadString(updateData);
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    if (!File.Exists(updateData))
                        return false;

                    updateData = File.ReadAllText(updateData);
                }

                updateJson = JObject.Parse(updateData);

                latestUpdate = null;
                foreach (var x in updateJson)
                {
                    if (x.Value["releaseNo"] == null || x.Value["gitRevision"] == null ||
                        x.Value["baseUrl"] == null || x.Value["files"] == null)
                    {
                        return false; // invalid update data
                    }

                    if (latestUpdate == null ||
                        int.Parse(x.Value["releaseNo"].ToString().Replace("\"", "")) >
                        int.Parse(latestUpdate["releaseNo"].ToString().Replace("\"", "")))
                    {
                        latestUpdate = x.Value;
                        latestUpdateVersion = x.Key + "-" + latestUpdate["gitRevision"].ToString().Replace("\"", "");
                    }
                }

                if (latestUpdate == null)
                    return false;

                var patchFiles = new List<string>();
                foreach (var file in latestUpdate["patchFiles"])
                    // each file mentioned here must match original hash or have a file in the _dewbackup folder that does
                {
                    var fileName = (string) file;
                    var fileHash = (string) settingsJson["gameFiles"][fileName];
                    if (!fileHashes.ContainsKey(fileName) &&
                        !fileHashes.ContainsKey(Path.Combine("_dewbackup", fileName)))
                    {
                        SetStatus("Original file data for file \"" + fileName + "\" not found.",
                            Color.FromRgb(255, 0, 0));
                        SetStatus("Please redo your Halo Online installation with the original HO files.",
                            Color.FromRgb(255, 0, 0), false);
                        return false;
                    }

                    if (fileHashes.ContainsKey(fileName)) // we have the file
                    {
                        if (fileHashes[fileName] != fileHash &&
                            (!fileHashes.ContainsKey(Path.Combine("_dewbackup", fileName)) ||
                             fileHashes[Path.Combine("_dewbackup", fileName)] != fileHash))
                        {
                            SetStatus(
                                "File \"" + fileName +
                                "\" was found but isn't original, and a valid backup of the original data wasn't found.",
                                Color.FromRgb(255, 0, 0));
                            SetStatus("Please redo your Halo Online installation with the original HO files.",
                                Color.FromRgb(255, 0, 0), false);
                            return false;
                        }
                    }
                    else
                    {
                        // we don't have the file
                        if (!fileHashes.ContainsKey(fileName + ".orig") &&
                            (!fileHashes.ContainsKey(Path.Combine("_dewbackup", fileName)) ||
                             fileHashes[Path.Combine("_dewbackup", fileName)] != fileHash))
                        {
                            SetStatus("Original file data for file \"" + fileName + "\" not found.",
                                Color.FromRgb(255, 0, 0));
                            SetStatus("Please redo your Halo Online installation with the original HO files.",
                                Color.FromRgb(255, 0, 0), false);
                            return false;
                        }
                    }

                    patchFiles.Add(fileName);
                }

                IDictionary<string, JToken> files = (JObject) latestUpdate["files"];

                filesToDownload = new List<string>();
                foreach (var x in files)
                {
                    var keyName = x.Key;
                    if (!fileHashes.ContainsKey(keyName) && fileHashes.ContainsKey(keyName.Replace(@"\", @"/")))
                        // linux system maybe?
                        keyName = keyName.Replace(@"\", @"/");

                    if (!fileHashes.ContainsKey(keyName) || fileHashes[keyName] != x.Value.ToString().Replace("\"", ""))
                    {
                        SetStatus("File \"" + keyName + "\" is missing or a newer version was found.",
                            Color.FromRgb(255, 0, 0));
                        var name = x.Key;
                        if (patchFiles.Contains(keyName))
                            name += ".bspatch";
                        filesToDownload.Add(name);
                    }
                }

                SetUpdateLabel(latestUpdateVersion);

                return true;
            }
            catch (WebException)
            {
                return false;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        private bool CompareHashesWithJson()
        {
            if (fileHashes == null)
                HashFilesInFolder(BasePath);

            IDictionary<string, JToken> files = (JObject) settingsJson["gameFiles"];

            foreach (var x in files)
            {
                var keyName = x.Key;
                if (!fileHashes.ContainsKey(keyName) && fileHashes.ContainsKey(keyName.Replace(@"\", @"/")))
                    // linux system maybe?
                    keyName = keyName.Replace(@"\", @"/");

                if (!fileHashes.ContainsKey(keyName))
                {
                    if (skipFileExtensions.Contains(Path.GetExtension(keyName)))
                        continue;

                    SetStatus("Failed to find required game file \"" + x.Key + "\"", Color.FromRgb(255, 0, 0));
                    SetStatus("Please redo your Halo Online installation with the original HO files.",
                        Color.FromRgb(255, 0, 0), false);
                    SetStatusLabels("Error", true);

                    var fade = (Storyboard) TryFindResource("fade");
                    fade.Stop(); // Stop animation
                    SetStatusLabels("Error", true);

                    return false;
                }

                if (fileHashes[keyName] != x.Value.ToString().Replace("\"", ""))
                {
                    if (skipFileExtensions.Contains(Path.GetExtension(keyName)) ||
                        skipFiles.Contains(Path.GetFileName(keyName)))
                        continue;

                    SetStatus("Game file \"" + keyName + "\" data is invalid.", Color.FromRgb(255, 0, 0));
                    SetStatus("Your hash: " + fileHashes[keyName], Color.FromRgb(255, 0, 0), false);
                    SetStatus("Expected hash: " + x.Value.ToString().Replace("\"", ""), Color.FromRgb(255, 0, 0), false);
                    SetStatus("Please redo your Halo Online installation with the original HO files.",
                        Color.FromRgb(255, 0, 0), false);
                    return false;
                }
            }

            return true;
        }

        private void CreateHashJson()
        {
            if (fileHashes == null)
                HashFilesInFolder(BasePath);

            var builder = new StringBuilder();
            builder.AppendLine("{");
            foreach (var kvp in fileHashes)
            {
                builder.AppendLine(String.Format("    \"{0}\": \"{1}\",", kvp.Key.Replace(@"\", @"\\"), kvp.Value));
            }
            builder.AppendLine("}");
            var json = builder.ToString();
            json = json;

            SetStatus(json, Color.FromRgb(255, 255, 0));
        }

        private void HashFilesInFolder(string basePath, string dirPath = "")
        {
            if (fileHashes == null)
                fileHashes = new Dictionary<string, string>();

            if (String.IsNullOrEmpty(dirPath))
            {
                dirPath = basePath;
                SetStatus("Validating game files...", Color.FromRgb(255, 255, 255));
            }

            foreach (var folder in Directory.GetDirectories(dirPath))
            {
                var dirName = Path.GetFileName(folder);
                if (skipFolders.Contains(dirName))
                    continue;
                HashFilesInFolder(basePath, folder);
            }

            foreach (var file in Directory.GetFiles(dirPath))
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                    {
                        var hash = hasher.ComputeHash(stream);
                        var hashStr = BitConverter.ToString(hash).Replace("-", "");

                        var fileKey = file.Replace(basePath, "");
                        if ((fileKey.StartsWith(@"\") || fileKey.StartsWith("/")) && fileKey.Length > 1)
                            fileKey = fileKey.Substring(1);
                        if (!fileHashes.ContainsKey(fileKey))
                            fileHashes.Add(fileKey, hashStr);
                    }
                }
                catch
                {
                }
            }
        }

        private void SetStatus(string status, Color color, bool updateLabel = true)
        {
            if (UpdateContent.Dispatcher.CheckAccess())
            {
                UpdateContent.Document.Blocks.Add(
                    new Paragraph(new Run(status) {Foreground = new SolidColorBrush(color)}));
            }
            else
            {
                UpdateContent.Dispatcher.Invoke(new Action(() => SetStatus(status, color, updateLabel)));
            }
        }

        private void SetStatusLabels(string status, bool error, bool updateLabel = true)
        {
            if (btnAction.Dispatcher.CheckAccess())
            {
                btnAction.Content = status.ToUpper();

                if (error)
                {
                    btnAction.Foreground = Brushes.Gray;
                }
            }
            else
            {
                btnAction.Dispatcher.Invoke(new Action(() => SetStatusLabels(status, true, updateLabel)));
            }
        }

        private void SetUpdateLabel(string version)
        {
            if (lblVersion.Dispatcher.CheckAccess())
            {
                lblVersion.Text = version;
            }
            else
            {
                lblVersion.Dispatcher.Invoke(new Action(() => SetUpdateLabel(version)));
            }
        }

        private void btnAction_Click(object sender, RoutedEventArgs e)
        {
            if (isPlayEnabled)
            {
                var sInfo = new ProcessStartInfo(BasePath + "/eldorado.exe");
                sInfo.Arguments = "-launcher";
                if (settingsViewModel.LaunchParams.WindowedMode)
                {
                    sInfo.Arguments += " -window";
                }
                if (settingsViewModel.LaunchParams.Fullscreen)
                {
                    sInfo.Arguments += " -fullscreen";
                }
                if (settingsViewModel.LaunchParams.NoVSync)
                {
                    sInfo.Arguments += " -no_vsync";
                }
                if (settingsViewModel.LaunchParams.DX9Ex)
                {
                    sInfo.Arguments += " -3d9ex";
                }
                else
                {
                    sInfo.Arguments += " -nod3d9ex";
                }
                if (settingsViewModel.LaunchParams.FPSCounter)
                {
                    sInfo.Arguments += " -show_fps";
                }

                sInfo.Arguments += " -width " + settingsViewModel.LaunchParams.Width;
                sInfo.Arguments += " -height " + settingsViewModel.LaunchParams.Height;

                try
                {
                    Process.Start(sInfo);
                }
                catch
                {
                    //MessageBox.Show("Game executable not found.");
                    var MainWindow = new MsgBoxOk("Game executable not found.");

                    MainWindow.Show();
                    MainWindow.Focus();
                }
            }
            else if (btnAction.Content == "UPDATE")
            {
                foreach (var file in filesToDownload)
                {
                    SetStatus("Downloading file \"" + file + "\"...", Color.FromRgb(255, 255, 0));
                    var url = latestUpdate["baseUrl"].ToString().Replace("\"", "") + file;
                    var destPath = Path.Combine(BasePath, file);
                    var dialog = new FileDownloadDialog(this, url, destPath);
                    var result = dialog.ShowDialog();
                    if (result.HasValue && result.Value)
                    {
                        // TOD: Refactor this. It's hacky
                    }
                    else
                    {
                        SetStatus("Download for file \"" + file + "\" failed.", Color.FromRgb(255, 0, 0));
                        SetStatus("Error: " + dialog.Error.Message, Color.FromRgb(255, 0, 0), false);
                        if (dialog.Error.InnerException != null)
                            SetStatus("Error: " + dialog.Error.InnerException.Message, Color.FromRgb(255, 0, 0), false);
                        return;
                    }
                }

                if (filesToDownload.Contains("DewritoUpdater.exe"))
                {
                    //MessageBox.Show("Update complete! Please restart the launcher.", "ElDewrito Launcher");
                    //Application.Current.Shutdown();
                    var MainWindow = new MsgBox("Update complete! Please restart the launcher.");

                    MainWindow.Show();
                    MainWindow.Focus();
                }

                btnAction.Content = "PLAY GAME";
                isPlayEnabled = true;
                //imgAction.Source = new BitmapImage(new Uri(@"/Resourves/playEnabled.png", UriKind.Relative));
                SetStatus("Update successful. You have the latest version! (" + latestUpdateVersion + ")",
                    Color.FromRgb(0, 255, 0));
            }
        }

        private void btnIRC_Click(object sender, RoutedEventArgs e)
        {
            var sInfo = new ProcessStartInfo("http://irc.lc/gamesurge/eldorito");
            Process.Start(sInfo);
        }

       
        private void browserServer_Click(object sender, RoutedEventArgs e)
        {
           /*
           embeddedBrowser.Source = new Uri("https://stats.halo.click/servers");
           */
       }

       private void browserStat_Click(object sender, RoutedEventArgs e)
       {
           /*
           embeddedBrowser.Source = new Uri("https://stats.halo.click/");
           */
      }

      private void browserFile_Click(object sender, RoutedEventArgs e)
      {
          /*
          embeddedBrowser.Source = new Uri("https://haloshare.net/");
          */
     }

     private void browserHome_Click(object sender, RoutedEventArgs e)
     {
         /*
         switchPanel("main", false);
         */
    }

    private void btnServer_Click(object sender, RoutedEventArgs e)
    {
        /*
       if (embedded)
       {
           embeddedBrowser.Source = new Uri("https://stats.halo.click/servers");
           switchPanel("browser", false);
       }
       else
       {
           var sInfo = new ProcessStartInfo("https://stats.halo.click/servers");
           Process.Start(sInfo);
       }
         */
   }

   private void btnStats_Click(object sender, RoutedEventArgs e)
   {
       /*
       if (embedded)
       {
           embeddedBrowser.Source = new Uri("https://stats.halo.click");
           switchPanel("browser", false);
       }
       else
       {
           var sInfo = new ProcessStartInfo("https://stats.halo.click/");
           Process.Start(sInfo);
       }
        */
   }

   private void btnFile_Click(object sender, RoutedEventArgs e)
   {
       /*
       if (embedded)
       {
           embeddedBrowser.Source = new Uri("blamfile://haloshare.net?type=forge&id=1");
           switchPanel("browser", false);
       }
       else
       {
           var sInfo = new ProcessStartInfo("https://haloshare.net/");
           Process.Start(sInfo);
       }
       */
    }

        private void btnReddit_Click(object sender, RoutedEventArgs e)
        {
            var sInfo = new ProcessStartInfo("https://www.reddit.com/r/HaloOnline/");
            Process.Start(sInfo);
        }

        private void btnInfo_Click(object sender, RoutedEventArgs e)
        {
        }

        private void LoadSettings()
        {
            // Load settings from the YAML file
            settings = null;
            try
            {
                using (var stream = File.OpenText(SettingsFileName))
                {
                    var deserializer = new Deserializer(namingConvention: new CamelCaseNamingConvention());
                    settings = deserializer.Deserialize<DewritoSettings>(stream);
                }
            }
            catch (IOException)
            {
            }
            catch (YamlException)
            {
            }
            if (settings == null)
                settings = new DewritoSettings(); // Use defaults if an error occurred

            // Create the view model and listen for changes
            settingsViewModel = new SettingsViewModel(settings);
            settingsViewModel.PropertyChanged += SettingsChanged;
            settingsViewModel.Player.PropertyChanged += SettingsChanged;
            settingsViewModel.Player.Armor.PropertyChanged += SettingsChanged;
            settingsViewModel.Player.Colors.PropertyChanged += SettingsChanged;
            settingsViewModel.Video.PropertyChanged += SettingsChanged;
            settingsViewModel.Host.PropertyChanged += SettingsChanged;
            settingsViewModel.Input.PropertyChanged += SettingsChanged;
            settingsViewModel.LaunchParams.PropertyChanged += SettingsChanged;

            // Set the data context for the settings tabs
            Customization.DataContext = settingsViewModel;
            Settings.DataContext = settingsViewModel;
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            sldFov.Value = 90;
            chkCenter.IsChecked = false;
            chkRaw.IsChecked = true;
            sldTimer.Value = 5;
            //sldMax.Value = 16;
        }

        private void SaveSettings()
        {
            settingsViewModel.Save(settings);
            try
            {
                using (var writer = new StreamWriter(File.Open(SettingsFileName, FileMode.Create, FileAccess.Write)))
                {
                    var serializer = new Serializer(SerializationOptions.EmitDefaults, new CamelCaseNamingConvention());
                    serializer.Serialize(writer, settings);
                }
            }
            catch (IOException)
            {
            }
            catch (YamlException)
            {
            }
        }

        private void SettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            SaveSettings();
        }
    }
}