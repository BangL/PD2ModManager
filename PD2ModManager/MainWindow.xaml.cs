using Gameloop.Vdf;
using LibGit2Sharp;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace PD2ModManager {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {

        private const int pd2AddID = 218620;
        private const string UpdatesAPIPath = "http://api.paydaymods.com/updates/retrieve/?";
        private const string UpdatesDownloadURL = "http://download.paydaymods.com/download/latest/";
        private const string UpdatesNotesUrl = "http://download.paydaymods.com/download/patchnotes/";
        //private const string ModsInfoUrl = "http://paydaymods.com/mods/{0}/{1}"; // TODO: Any chance to get these {0} id's from api?
        
        private string dirMods;
        private string dirModOverrides;
        private ObservableCollection<ModInfo> mods;

        private ErrorHandler err;

        public MainWindow() {
            InitializeComponent();
            err = new ErrorHandler();
            try {
                // show version in header
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
                this.Title = $"{this.Title} v{fvi.FileMajorPart}.{fvi.FileMinorPart}";

                // Window State/Positon
                if (!Properties.Settings.Default.FirstStart) {
                    this.Top = Properties.Settings.Default.Top;
                    this.Left = Properties.Settings.Default.Left;
                    this.Height = Properties.Settings.Default.Height;
                    this.Width = Properties.Settings.Default.Width;
                    if (Properties.Settings.Default.Maximized) {
                        this.WindowState = WindowState.Maximized;
                    }
                }

                // Get PD2 Path
                string dirPayday2 = GetPD2Path(GetSteamPath());
                if (string.IsNullOrEmpty(dirPayday2)) {
                    throw new ApplicationException("PAYDAY 2 not found.");
                }

                dirMods = Path.Combine(dirPayday2, "mods");
                if (!Directory.Exists(dirMods)) {
                    Directory.CreateDirectory(dirMods);
                }

                dirModOverrides = Path.Combine(dirPayday2, "assets", "mod_overrides");
                if (!Directory.Exists(dirModOverrides)) {
                    Directory.CreateDirectory(dirModOverrides);
                }

                RefreshModList();
            } catch (Exception ex) {
                err.Log(ex);
            }
        }

        private void RefreshModList() {
            try {

                // Get BLT Mod List
                mods = GetBLTMods();
                List<ModInfoUpdate> updates = new List<ModInfoUpdate>();
                foreach (ModInfo mod in mods) {
                    if (mod.updates != null && mod.updates.Count > 0) {
                        foreach (ModInfoUpdate update in mod.updates) {
                            if (!string.IsNullOrEmpty(update.identifier)) {
                                updates.Add(update);
                            }
                        }
                    }
                }
                foreach (ModInfo mod in mods) {
                    if (mod.libraries != null && mod.libraries.Count > 0) {
                        foreach (ModInfoUpdate library in mod.libraries) {
                            if (!string.IsNullOrEmpty(library.identifier)) {
                                ModInfoUpdate found = updates.Where(s => s.identifier.Equals(library.identifier)).FirstOrDefault();
                                if (found == null) {
                                    updates.Add(library);
                                } else {
                                    if (found.optional && !library.optional) {
                                        updates.Remove(found);
                                        updates.Add(library);
                                    }
                                }
                            }
                        }
                    }
                }

                // Build update request
                string url = UpdatesAPIPath;
                int i = 0;
                foreach (ModInfoUpdate update in updates) {
                    url = $"{url}mod[{i}]={update.identifier}{(updates.Count - 1 > i ? "&" : "")}";
                    i++;
                }

                // Request update info
                Dictionary<string, UpdateInfo> updateInfos = JsonConvert.DeserializeObject<Dictionary<string, UpdateInfo>>(APIWebRequest(url));

                // refresh update state
                foreach (ModInfo mod in mods) {
                    if (mod.state != UpdateState.Git) {
                        if (!string.IsNullOrEmpty(mod.identifier)) {
                            if (updateInfos.ContainsKey(mod.identifier)) {
                                UpdateInfo update = updateInfos[mod.identifier];
                                mod.available = update.revision;
                                mod.date = update.date;
                                if (mod.revision != update.revision) {
                                    mod.state = UpdateState.Update;
                                } else {
                                    mod.state = UpdateState.UpToDate;
                                }
                            }
                        }
                    } else {
                        string path = Path.Combine(dirMods, mod.mod);
                        using (var repo = new Repository(path)) {
                            foreach (Remote remote in repo.Network.Remotes) {
                                IEnumerable<string> refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                                Commands.Fetch(repo, remote.Name, refSpecs, null, "");
                            }
                        }
                    }
                }

                dataGrid.DataContext = mods;
            } catch (Exception ex) {
                err.Log(ex);
            }
        }
        
        public string APIWebRequest(string url) {
            try {
                WebRequest request = WebRequest.Create(url);
                request.Credentials = CredentialCache.DefaultCredentials;
                using (StreamReader stream = new StreamReader(request.GetResponse().GetResponseStream())) {
                    return stream.ReadToEnd();
                }
            } catch (Exception ex) {
                err.Log(ex);
                return null;
            }
        }

        public ObservableCollection<ModInfo> GetBLTMods() {
            try {
                ObservableCollection<ModInfo> result = new ObservableCollection<ModInfo>();
                foreach (string fullDir in Directory.GetDirectories(dirMods)) {
                    string modName = Path.GetFileName(fullDir);
                    string modTxt = Path.Combine(fullDir, "mod.txt");
                    if (File.Exists(modTxt)) {
                        using (TextReader reader = new StreamReader(modTxt)) {
                            string json = reader.ReadToEnd();
                            ModInfo modInfo = null;
                            try {
                                modInfo = JsonConvert.DeserializeObject<ModInfo>(json);
                            } catch (Exception ex) {
                                try {
                                    // workarounds for invalid json files
                                    json = Regex.Replace(json, "(\"(?:[^\"\\\\]|\\\\.)*\")|\\s+", "$1"); // minify json
                                    json = json.Replace("\",}", "\"}"); // remove commas at the end of arrays
                                    json = json.Replace("\"}]\"", "\"}],\""); // insert commas after arrays, if there are more values following

                                    // TODO: this one would also replace empty strings.. need some regex here
                                    //json = json.Replace("\"\"", "\",\""); // insert commas after values, if there are more values following

                                    modInfo = JsonConvert.DeserializeObject<ModInfo>(json);
                                } catch {
                                    err.Log(new ApplicationException($"Skipping mod \"{modName}\" due to invalid json in its mod.txt." + Environment.NewLine + $"Parser Error: {ex.Message}", ex));
                                }
                            }
                            if (modInfo != null) {
                                if (Directory.Exists(Path.Combine(fullDir, ".git"))) {
                                    modInfo.state = UpdateState.Git;
                                }
                                modInfo.mod = modName;
                                result.Add(modInfo);
                            }
                        }
                    }
                }
                return result;
            } catch (Exception ex) {
                err.Log(ex);
                return null;
            }
        }
        
        public string GetPD2Path(string steamPath) {
            try {

                // Get Steam Library Folders
                List<string> libraryFolders = new List<string>();
                libraryFolders.Add(steamPath);
                string libraryFoldersFile = Path.Combine(steamPath, "SteamApps", "libraryfolders.vdf");
                if (File.Exists(libraryFoldersFile)) {
                    using (TextReader reader = new StreamReader(libraryFoldersFile)) {
                        VObject vLibFolders = (VObject) VdfConvert.Deserialize(reader.ReadToEnd()).Children().First().Value;
                        foreach (VProperty vPropSub in vLibFolders.Children()) {
                            if (vPropSub.Key.IsNumeric()) {
                                string libraryFolder = vPropSub.Value.ToString().Replace("\\\\", "\\");
                                if (Directory.Exists(libraryFolder)) {
                                    libraryFolders.Add(libraryFolder);
                                }
                            }
                        }
                    }
                }

                // Get PD2 Path
                string pd2Path = string.Empty;
                foreach (string libraryFolder in libraryFolders) {
                    if (File.Exists(Path.Combine(libraryFolder, "SteamApps", $"appmanifest_{pd2AddID}.acf"))) {
                        pd2Path = Path.Combine(libraryFolder, "SteamApps", "common", "PAYDAY 2");
                    }
                }

                // Check PD2 Path
                if (string.IsNullOrEmpty(pd2Path) || !Directory.Exists(pd2Path)) {
                    throw new ApplicationException($"Invalid PAYDAY 2 Path: \"{pd2Path}\". Directory does not exist.");
                }
                if (!File.Exists(Path.Combine(pd2Path, "payday2_win32_release.exe"))) {
                    throw new ApplicationException("payday2_win32_release.exe not found.");
                }
            
                return pd2Path;
            } catch (Exception ex) {
                err.Log(ex);
                return null;
            }
        }

        public static string GetSteamPath() {

            // Get Steam Path
            string regPath = Path.Combine("HKEY_LOCAL_MACHINE", "SOFTWARE", Is64bit?"Wow6432Node":"", "Valve", "Steam");
            string steamPath = (string) Registry.GetValue(regPath, "InstallPath", "");

            // Check Steam Path
            if (string.IsNullOrEmpty(steamPath)) {
                throw new ApplicationException("Steam not found.");
            }
            if (!Directory.Exists(steamPath)) {
                throw new ApplicationException($"Invalid Steam Path: \"{steamPath}\". Directory does not exist.");
            }
            if (!File.Exists(Path.Combine(steamPath, "Steam.exe"))) {
                throw new ApplicationException("Steam.exe not found.");
            }

            return steamPath;
        }
        
        public static bool Is64bit {
            get {
                return IntPtr.Size == 8;
            }
        }
        
        private void btnUpdateMod_Click(object sender, RoutedEventArgs e) {
            try {
                btnUpdateAll.IsEnabled = false;
                btnUpdateMod.IsEnabled = false;

                int index = dataGrid.SelectedIndex;
                ModInfo mod = (ModInfo) dataGrid.SelectedItem;
                DownloadAndInstallMod(mod);
                RefreshModList();
                dataGrid.SelectedIndex = index;

                MessageBox.Show($"mod \"{mod.name}\" updated successfuly.");

                btnUpdateAll.IsEnabled = true;
                btnUpdateMod.IsEnabled = (dataGrid.SelectedItems.Count >= 1);
            } catch (Exception ex) {
                err.Log(ex);
            }
        }

        private void btnUpdateAll_Click(object sender, RoutedEventArgs e) {
            try {
                btnUpdateAll.IsEnabled = false;
                btnUpdateMod.IsEnabled = false;

                // do this two times for possible new dependecies
                for (int i = 0; i <= 1; i++) {
                    foreach (ModInfo mod in mods) {
                        if (!string.IsNullOrEmpty(mod.identifier)) {
                            if (mod.state == UpdateState.Update) {
                                DownloadAndInstallMod(mod);
                            }
                        }
                    }
                    RefreshModList();
                }
            
                MessageBox.Show("All mods updated successfuly.");

                btnUpdateAll.IsEnabled = true;
                btnUpdateMod.IsEnabled = (dataGrid.SelectedItems.Count >= 1);
            } catch (Exception ex) {
                err.Log(ex);
            }
        }

        private void DownloadAndInstallMod(ModInfo mod) {
            try {
                if (mod.state != UpdateState.Git) {
                    // build url
                    string url = UpdatesDownloadURL + mod.identifier;

                    // create temp file
                    string tempfile = Path.GetTempFileName();

                    // download latest mod zip
                    using (var client = new WebClient()) {
                        client.DownloadFile(url, tempfile);
                    }

                    // clean target dir
                    Directory.Delete(Path.Combine(dirMods, mod.mod), true);

                    // extract zip
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempfile, dirMods);

                    // cleanup
                    File.Delete(tempfile);
                } else {

                    /*
                     * TODO
                    
                    //get remote branch list
                    IEnumerable<Reference> references = Repository.ListRemoteReferences(Path.Combine(dirMods, mod.mod));
                    IEnumerable<string> remoteBranches = references.Where(x => x.IsRemoteTrackingBranch).Select(x => x.CanonicalName.Replace("refs/remotes/", ""));
                    foreach (string branch in remoteBranches) {

                    }

                    string path = Path.Combine(dirMods, mod.mod);
                    using (var repo = new Repository(path)) {
                        var branch = repo.Branches["origin/master"];
                        if (branch != null) {
                            Branch currentBranch = Commands.Checkout(repo, branch);
                        }
                    }
                    */
                }
            } catch (Exception ex) {
                err.Log(ex);
            }
        }

        private void dataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            try {
                btnUpdateMod.IsEnabled = false;
                btnUpdateMod.Content = "Update";
                if (dataGrid.SelectedItems.Count >= 1) {

                    // refresh update button
                    int modsToUpdate = 0;
                    int modsToReinstall = 0;
                    foreach (ModInfo mod in dataGrid.SelectedItems) {
                        switch (mod.state) {
                            case UpdateState.Update:
                                modsToUpdate++;
                                break;
                            case UpdateState.UpToDate:
                                modsToReinstall++;
                                break;
                        }
                    }
                    if (modsToReinstall > 0 || modsToUpdate > 0) {
                        btnUpdateMod.IsEnabled = true;
                        if (modsToReinstall > 0 && modsToUpdate > 0) {
                            btnUpdateMod.Content = $"Update/Reinstall {modsToUpdate + modsToReinstall} mod{(modsToUpdate + modsToReinstall > 1 ? "s" : "")}";
                        } else if (modsToReinstall > 0) {
                            btnUpdateMod.Content = $"Reinstall {modsToReinstall} mod{(modsToReinstall > 1 ? "s" : "")}";
                        } else if (modsToUpdate > 0) {
                            btnUpdateMod.Content = $"Update {modsToUpdate} mod{(modsToUpdate > 1 ? "s" : "")}";
                        }
                    }

                    // get web view for first selected mod
                    foreach (ModInfo mod in dataGrid.SelectedItems) {
                        lblModName.Visibility = Visibility.Hidden;
                        webChangelog.Visibility = Visibility.Hidden;
                        txtChangelog.Visibility = Visibility.Hidden;
                        lblChangesheader.Content = "No Data";
                        lblModName.Content = mod.name;
                        if (mod.state != UpdateState.Git) {
                            if (mod.state != UpdateState.LocalOnly) {
                                webChangelog.Source = new Uri(UpdatesNotesUrl + mod.identifier);
                                lblModName.Visibility = Visibility.Visible;
                                webChangelog.Visibility = Visibility.Visible;
                                lblChangesheader.Content = "Changelog:";
                            }
                            break;
                        } else {
                            txtChangelog.Text = "";
                            using (Repository repo = new Repository(Path.Combine(dirMods, mod.mod))) {
                                foreach (Commit c in repo.Commits.Take(10)) {
                                    txtChangelog.Text += $"commit {c.Id}\n";
                                    txtChangelog.Text += $"\tAuthor: {c.Author.Name} <{c.Author.Email}>\n";
                                    txtChangelog.Text += $"\tDate:   {c.Author.When.ToString()}\n\n";
                                    txtChangelog.Text += $"\t{c.Message.Replace("\n", "\n\t")}\n\n";
                                }
                            }
                            lblModName.Visibility = Visibility.Visible;
                            txtChangelog.Visibility = Visibility.Visible;
                            lblChangesheader.Content = "Last 10 Commits (locally):";
                            break;
                        }
                    }
                }
            } catch (Exception ex) {
                err.Log(ex);
            }
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e) {
            try {
                RefreshModList();
            } catch (Exception ex) {
                err.Log(ex);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {

            Properties.Settings.Default.FirstStart = false;

            if (WindowState == WindowState.Maximized) {
                Properties.Settings.Default.Top = RestoreBounds.Top;
                Properties.Settings.Default.Left = RestoreBounds.Left;
                Properties.Settings.Default.Height = RestoreBounds.Height;
                Properties.Settings.Default.Width = RestoreBounds.Width;
                Properties.Settings.Default.Maximized = true;
            } else {
                Properties.Settings.Default.Top = this.Top;
                Properties.Settings.Default.Left = this.Left;
                Properties.Settings.Default.Height = this.Height;
                Properties.Settings.Default.Width = this.Width;
                Properties.Settings.Default.Maximized = false;
            }
            
            Properties.Settings.Default.Save();
        }
    }
}
