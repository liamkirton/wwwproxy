///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxyClient
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxyClient.cs
//
// Created: 16/09/2007
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
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WwwProxyClient
{
    class WwwProxyClient
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        
        static ManualResetEvent exitEvent_ = null;
        static WwwProxy.WwwProxy wwwProxy_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            switch(e.SpecialKey)
            {
                case ConsoleSpecialKey.ControlC:
                    e.Cancel = true;
                    exitEvent_.Set();
                    break;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        static void Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("WwwProxyClient {0}", Assembly.GetExecutingAssembly().GetName().Version.ToString());
            Console.WriteLine("Copyright ©2008 Liam Kirton <liam@int3.ws>");
            Console.WriteLine();

            string certificate = null;
            bool debug = false;
            bool listenAny = false;
            ushort localPort = 8080;
            bool ntlmEnabled = false;
            bool pluginsEnabled = false; 
            string[] remoteProxy = null;
            string remoteProxyExceptions = null;

            for(int i = 0; i < args.Length; ++i)
            {
                if(args[i] == "-certificate")
                {
                    certificate = args[++i];
                }
                else if(args[i] == "-debug")
                {
                    debug = true;
                }
                else if(args[i] == "-listen-any")
                {
                    listenAny = true;
                }
                else if(args[i] == "-localport")
                {
                    localPort = Convert.ToUInt16(args[++i]);
                }
                else if(args[i] == "-ntlm")
                {
                    ntlmEnabled = true;
                }
                else if(args[i] == "-plugins")
                {
                    pluginsEnabled = true;
                }
                else if(args[i] == "-remote")
                {
                    remoteProxy = args[++i].Split(':');
                }
                else if(args[i] == "-remote-exceptions")
                {
                    remoteProxyExceptions = args[++i];
                }
                else
                {
                    Console.WriteLine("Usage: WwwProxyClient.exe -localport 8080 -remote 10.0.0.254:8080");
                    Console.WriteLine("                          -certificate f.cer -debug -ntlm -plugins");
                    Console.WriteLine("                          -remote-exceptions 10.0.*.*;10.254.254.254;*.int3.ws;ftp.intra.net");
                    Console.WriteLine();
                    return;
                }
            }

            wwwProxy_ = WwwProxy.WwwProxy.Instance;
            if(debug)
            {
                wwwProxy_.Debug = true; // Otherwise, determined by WwwProxy_Debug environment variable.
            } 
            
            wwwProxy_.Error += new WwwProxy.WwwProxyErrorEvent(WwwProxy_Error);
            wwwProxy_.Request += new WwwProxy.WwwProxyRequestEvent(WwwProxy_Request);
            wwwProxy_.Response += new WwwProxy.WwwProxyResponseEvent(WwwProxy_Response);

            wwwProxy_.Certificate = certificate;
            wwwProxy_.ClientCertificates = null;
            wwwProxy_.NtlmEnabled = ntlmEnabled;
            wwwProxy_.PluginsEnabled = pluginsEnabled;
            
            wwwProxy_.RemoteProxy = (remoteProxy != null) ? new IPEndPoint(IPAddress.Parse(remoteProxy[0]), Convert.ToUInt16(remoteProxy[1])) : null;
            wwwProxy_.RemoteProxyExceptions = remoteProxyExceptions;

            wwwProxy_.Start(localPort, listenAny ? IPAddress.Any : null);

            Console.WriteLine("Running. Press Ctrl+C to Quit.");
            Console.WriteLine();

            exitEvent_ = new ManualResetEvent(false);
            Console.CancelKeyPress += new ConsoleCancelEventHandler(Console_CancelKeyPress);
            exitEvent_.WaitOne();

            Console.WriteLine("Closing.");

            wwwProxy_.Stop();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        static void WwwProxy_Error(WwwProxy.ProxyRequest request, Exception exception, string error)
        {
            Console.WriteLine("WwwProxy_Error(): {0}", error);
            Console.WriteLine(exception.StackTrace);

            if(exception != null)
            {
                if(exception is SocketException)
                {
                    if(((SocketException)exception).ErrorCode == 10048)
                    {
                        exitEvent_.Set();
                    }
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        static void WwwProxy_Request(WwwProxy.ProxyRequest request)
        {
            Match urlMatch = Regex.Match(request.Header, "([A-Z]+)\\s+(.*?)\\s+HTTP/\\d\\.\\d\r$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if(urlMatch.Success)
            {
                string url = urlMatch.Groups[2].Value;
                Console.WriteLine(">>>>> WwwProxy_Request({0}, {1})", request.Id, url);
            }

            Console.WriteLine();
            Console.WriteLine(request.Header);
            if(request.Data != null)
            {
                Console.WriteLine();
                Console.WriteLine(request.Data);
            }
            Console.WriteLine();

            if(!request.Header.Contains("download.windowsupdate.com"))
            {
                wwwProxy_.Pass(request);
            }
            else
            {
                wwwProxy_.Drop(request);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        static void WwwProxy_Response(WwwProxy.ProxyRequest request, WwwProxy.ProxyResponse response)
        {
            Match urlMatch = Regex.Match(request.Header, "([A-Z]+)\\s+(.*?)\\s+HTTP/\\d\\.\\d\r$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if(urlMatch.Success)
            {
                string url = urlMatch.Groups[2].Value;
                Console.WriteLine("<<<<< WwwProxy_Response({0}, {1}, {2})", response.Id, url, response.Completable);
            }

            Console.WriteLine();
            Console.WriteLine(response.Header);
            Console.WriteLine();

            wwwProxy_.Pass(response);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////