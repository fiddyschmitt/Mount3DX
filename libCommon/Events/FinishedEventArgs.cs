﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCommon.Events
{
    public class FinishedEventArgs : EventArgs
    {
        public bool Success;
        public string Message;
    }
}
