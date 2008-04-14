///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxy
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Log.cs
//
// Created: 17/12/2007
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// This file is part of WwwProxy.
//
// WwwProxy is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// WwwProxy is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with WwwProxy.  If not, see <http://www.gnu.org/licenses/>.
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace WwwProxy
{
    public class Log
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private string file_ = null;
        private StreamWriter logOut_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        
        public Log(string file)
        {
            file_ = file;   
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Start()
        {
            if(logOut_ != null)
            {
                return;
            }

            logOut_ = new StreamWriter(file_, false);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Stop()
        {
            if(logOut_ == null)
            {
                return;
            }

            logOut_.Close();
            logOut_ = null;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Write(string caller, string s)
        {
            if(logOut_ == null)
            {
                return;
            }

            string sOut = "================================================================================\r\n" +
                          TimeStamp() + " " + caller + "\r\n" +
                          "================================================================================\r\n\r\n" +
                          new StackTrace(true).ToString() + "\r\n\r\n" +
                          "--------------------------------------------------------------------------------\r\n";
            if((s != null) && (s.Length > 0))
            {
                sOut += s + "\r\n" +
                        "--------------------------------------------------------------------------------\r\n\r\n";
            }

            lock(logOut_)
            {
                logOut_.Write(sOut);
                logOut_.Flush();
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private string TimeStamp()
        {
            DateTime timeStamp = DateTime.Now;
            return "[" +
                   timeStamp.Year + "/" +
                   ((timeStamp.Month < 10) ? "0" : "") + timeStamp.Month + "/" +
                   ((timeStamp.Day < 10) ? "0" : "") + timeStamp.Day + " " +
                   ((timeStamp.Hour < 10) ? "0" : "") + timeStamp.Hour + ":" +
                   ((timeStamp.Minute < 10) ? "0" : "") + timeStamp.Minute + ":" +
                   ((timeStamp.Second < 10) ? "0" : "") + timeStamp.Second + "]";
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
