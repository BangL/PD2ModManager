using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PD2ModManager {
    public static class StringExtensions {
        public static bool IsNumeric(this string input) {
            decimal number;
            return decimal.TryParse(input, out number);
        }
    }
}
