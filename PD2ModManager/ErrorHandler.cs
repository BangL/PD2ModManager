using System;
using System.Drawing;

namespace PD2ModManager {
    class ErrorHandler {
        public void Log(Exception ex) {
            new ErrorWindow(ex.Message, "Error", SystemIcons.Error).ShowDialog();
        }
    }
}