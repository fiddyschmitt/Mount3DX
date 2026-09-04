using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCommon.Events
{
    public class ProgressEventArgs : EventArgs
    {
        public EnumNature Nature;
        public string? Message;

        public enum EnumNature
        {
            Good,
            Neutral,
            Warning,    //something went wrong but the session carries on
            Bad
        }
    }
}
