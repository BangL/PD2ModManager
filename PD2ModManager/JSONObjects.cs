﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace PD2ModManager {

    public class ModInfo {
        public string name { get; set; }
        public string mod { get; set; }
        public string description { get; set; }
        public string author { get; set; }
        public string contact { get; set; }
        public string version { get; set; }
        public string priority { get; set; }
        public string available { get; set; }
        public UpdateState state { get; set; }
        public DateTime date { get; set; }
        public List<ModInfoUpdate> updates { get; set; }
        public List<ModInfoUpdate> libraries { get; set; }
        public List<ModInfoScript> persist_scripts { get; set; }
        public List<ModInfoHook> pre_hooks { get; set; }
        public List<ModInfoHook> hooks { get; set; }
        public List<ModInfoKeybind> keybinds { get; set; }

        private ModInfoUpdate main_update {
            get {
                if (updates != null) {
                    // our main update usually has no install_dir
                    return updates.Where(s => string.IsNullOrEmpty(s.install_dir)).FirstOrDefault();
                }
                return null;
            }
        }

        public string identifier {
            get {
                if (state != UpdateState.Git && main_update != null) {
                    return main_update.identifier;
                }
                return "";
            }
        }

        public string revision {
            get {
                if (state != UpdateState.Git && main_update != null) {
                    return main_update.revision;
                }
                return "";
            }
        }

        public string dateString {
            get {
                if (date > DateTime.MinValue) {
                    return date.ToString();
                } else {
                    return "";
                }
            }
        }

        public string stateString {
            get {
                return state.GetDescription();
            }
        }
    }

    public enum UpdateState {
        [Description("Local mod")]
        LocalOnly = 0,
        [Description("Up to date")]
        UpToDate = 1,
        [Description("Update available")]
        Update = 2,
        [Description("Git managed")]
        Git = 4
    }

    public class ModInfoUpdate {
        public string revision { get; set; }
        public string identifier { get; set; }
        public string display_name { get; set; }
        public string install_dir { get; set; }
        public string install_folder { get; set; }
        public bool optional { get; set; }
    }

    public class ModInfoHook {
        public string hook_id { get; set; }
        public string script_path { get; set; }
    }

    public class ModInfoScript {
        public string global { get; set; }
        public string script_path { get; set; }
    }

    public class ModInfoKeybind {
        public string keybind_id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public string script_path { get; set; }
        public bool run_in_menu { get; set; }
        public bool run_in_game { get; set; }
        public bool localized { get; set; }
    }

    public class UpdateInfo {
        public string name { get; set; }
        public DateTime date { get; set; }
        public string author { get; set; }
        public string revision { get; set; }
        public string revision_id { get; set; }
    }

}
