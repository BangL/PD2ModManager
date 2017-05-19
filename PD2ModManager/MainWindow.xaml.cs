using Gameloop.Vdf;
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

namespace PD2ModManager {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {

        private const int pd2AddID = 218620;
        private const string UpdatesAPIPath = "http://api.paydaymods.com/updates/retrieve/?";
        private const string UpdatesDownloadURL = "http://download.paydaymods.com/download/latest/{0}";
        private const string UpdatesNotesUrl = "http://download.paydaymods.com/download/patchnotes/{0}";
        
        private string dirMods;
        private string dirModOverrides;
        private ObservableCollection<ModInfo> mods;

        public MainWindow() {
            InitializeComponent();
            
            // show version in header
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            this.Title = string.Format("{0} v{1}.{2}", this.Title, fvi.FileMajorPart, fvi.FileMinorPart);

            // Get PD2 Path
            string dirPayday2 = GetPD2Path(GetSteamPath());
            if (string.IsNullOrEmpty(dirPayday2)) {
                throw new ApplicationException("PAYDAY 2 not found.");
            }

            dirMods = System.IO.Path.Combine(dirPayday2, "mods");
            if (!Directory.Exists(dirMods)) {
                Directory.CreateDirectory(dirMods);
            }

            dirModOverrides = System.IO.Path.Combine(dirPayday2, "assets", "mod_overrides");
            if (!Directory.Exists(dirModOverrides)) {
                Directory.CreateDirectory(dirModOverrides);
            }

            RefreshModList();
        }

        private void RefreshModList() {

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
                url = string.Format("{0}mod[{1}]={2}{3}", url, i, update.identifier, updates.Count - 1 > i ? "&" : "");
                i++;
            }

            // Request update info
            Dictionary<string, UpdateInfo> updateInfos = JsonConvert.DeserializeObject<Dictionary<string, UpdateInfo>>(APIWebRequest(url));

            // refresh update state
            foreach (ModInfo mod in mods) {
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
            }

            dataGrid.DataContext = mods;
        }
        
        public string APIWebRequest(string url) {
            WebRequest request = WebRequest.Create(url);
            request.Credentials = CredentialCache.DefaultCredentials;
            return new StreamReader(request.GetResponse().GetResponseStream()).ReadToEnd();
        }

        public ObservableCollection<ModInfo> GetBLTMods() {
            ObservableCollection<ModInfo> result = new ObservableCollection<ModInfo>();
            foreach (string fullDir in Directory.GetDirectories(dirMods)) {
                string modName = System.IO.Path.GetFileName(fullDir);
                string modTxt = System.IO.Path.Combine(fullDir, "mod.txt");
                if (File.Exists(modTxt)) {
                    using (TextReader reader = new StreamReader(modTxt)) {
                        string json = reader.ReadToEnd();
                        ModInfo modInfo = null;
                        try {
                            modInfo = JsonConvert.DeserializeObject<ModInfo>(json);
                        } catch (Exception ex) {
                            
                            // workarounds for invalid json files
                            json = Regex.Replace(json, "(\"(?:[^\"\\\\]|\\\\.)*\")|\\s+", "$1"); // minify json
                            json = json.Replace("\",}", "\"}"); // remove commas at the end of arrays
                            json = json.Replace("\"}]\"", "\"}],\""); // insert commas after arrays, if there are more values following
                            modInfo = JsonConvert.DeserializeObject<ModInfo>(json);

                        }
                        if (modInfo != null) {
                            modInfo.mod = modName;
                            result.Add(modInfo);
                        }
                    }
                }
            }
            return result;
        }
        
        public string GetPD2Path(string steamPath) {

            // Get Steam Library Folders
            List<string> libraryFolders = new List<string>();
            libraryFolders.Add(steamPath);
            string libraryFoldersFile = System.IO.Path.Combine(steamPath, "SteamApps", "libraryfolders.vdf");
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
                if (File.Exists(System.IO.Path.Combine(libraryFolder, "SteamApps", string.Format("appmanifest_{0}.acf", pd2AddID)))) {
                    pd2Path = System.IO.Path.Combine(libraryFolder, "SteamApps", "common", "PAYDAY 2");
                }
            }

            // Check PD2 Path
            if (string.IsNullOrEmpty(pd2Path) || !Directory.Exists(pd2Path)) {
                throw new ApplicationException(string.Format("Invalid PAYDAY 2 Path: \"{0}\". Directory does not exist.", pd2Path));
            }
            if (!File.Exists(System.IO.Path.Combine(pd2Path, "payday2_win32_release.exe"))) {
                throw new ApplicationException("payday2_win32_release.exe not found.");
            }
            
            return pd2Path;
        }

        public static string GetSteamPath() {

            // Get Steam Path
            string regPath = System.IO.Path.Combine("HKEY_LOCAL_MACHINE", "SOFTWARE", Is64bit?"Wow6432Node":"", "Valve", "Steam");
            string steamPath = (string) Registry.GetValue(regPath, "InstallPath", "");

            // Check Steam Path
            if (string.IsNullOrEmpty(steamPath)) {
                throw new ApplicationException("Steam not found.");
            }
            if (!Directory.Exists(steamPath)) {
                throw new ApplicationException(string.Format("Invalid Steam Path: \"{0}\". Directory does not exist.", steamPath));
            }
            if (!File.Exists(System.IO.Path.Combine(steamPath, "Steam.exe"))) {
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
            btnUpdateAll.IsEnabled = false;
            btnUpdateMod.IsEnabled = false;

            int index = dataGrid.SelectedIndex;
            ModInfo mod = (ModInfo) dataGrid.SelectedItem;
            DownloadAndInstallMod(mod);
            RefreshModList();
            dataGrid.SelectedIndex = index;

            MessageBox.Show(string.Format("mod \"{0}\" updated successfuly.", mod.name));

            btnUpdateAll.IsEnabled = true;
            btnUpdateMod.IsEnabled = true;
        }

        private void btnUpdateAll_Click(object sender, RoutedEventArgs e) {
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
            btnUpdateMod.IsEnabled = true;
        }

        private void DownloadAndInstallMod(ModInfo mod) {

            // build url
            string url = string.Format(UpdatesDownloadURL, mod.identifier );

            // create temp file
            string tempfile = System.IO.Path.GetTempFileName();

            // download latest mod zip
            using (var client = new WebClient()) {
                client.DownloadFile(url, tempfile);
            }

            // clean target dir
            Directory.Delete(System.IO.Path.Combine(dirMods, mod.mod), true);

            // extract zip
            System.IO.Compression.ZipFile.ExtractToDirectory(tempfile, dirMods);

            // cleanup
            File.Delete(tempfile);

        }

        private void dataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (dataGrid.SelectedItems.Count == 1) {
                ModInfo entry = (ModInfo) dataGrid.SelectedItem;
                switch (entry.state) {
                    case UpdateState.LocalOnly:
                        btnUpdateMod.Content = "Update";
                        btnUpdateMod.Visibility = Visibility.Hidden;
                        break;
                    case UpdateState.Update:
                        btnUpdateMod.Content = "Update";
                        btnUpdateMod.Visibility = Visibility.Visible;
                        break;
                    case UpdateState.UpToDate:
                        btnUpdateMod.Content = "Reinstall";
                        btnUpdateMod.Visibility = Visibility.Visible;
                        break;
                }
            } else {
                btnUpdateMod.Visibility = Visibility.Hidden;
            }
        }
    }
}
