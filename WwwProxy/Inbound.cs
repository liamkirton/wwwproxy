///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxy
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Inbound.cs
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
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WwwProxy
{
    internal class Inbound
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal IPEndPoint inboundIpEndPoint_ = null;

        internal Mutex currentOutboundsMutex_ = null;
        internal Dictionary<Outbound, ProxyRequest> currentOutbounds_ = null;

        private WwwProxy wwwProxy_ = null;

        private Thread inboundThread_ = null;
        private ManualResetEvent inboundThreadExitEvent_ = null;
        private ManualResetEvent inboundSocketReceiveEvent_ = null;
        private ManualResetEvent sslNetworkStreamReceiveEvent_ = null;

        private Socket inboundSocket_ = null;

        private int asyncBytesRead = 0;

        private Timer keepAliveTimer_ = null;

        private bool closed_ = false;
        private bool ssl_ = false;
        private NetworkStream sslNetworkStream_ = null;
        private SslStream sslStream_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal Inbound(WwwProxy wwwProxy, Socket socket)
        {
            inboundIpEndPoint_ = (IPEndPoint)socket.RemoteEndPoint;

            wwwProxy_ = wwwProxy;
            inboundSocket_ = socket;

            currentOutboundsMutex_ = new Mutex();
            currentOutbounds_ = new Dictionary<Outbound, ProxyRequest>();

            inboundThreadExitEvent_ = new ManualResetEvent(false);
            inboundSocketReceiveEvent_ = new ManualResetEvent(false);
            sslNetworkStreamReceiveEvent_ = new ManualResetEvent(false);

            inboundThread_ = new Thread(RunSocket);
            inboundThread_.Start();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void Close()
        {
            if(closed_)
            {
                return;
            }
            closed_ = true;

            StopKeepAliveTimer();

            try
            {
                if(inboundThreadExitEvent_ != null)
                {
                    inboundThreadExitEvent_.Set();
                }
                if(inboundSocketReceiveEvent_ != null)
                {
                    inboundSocketReceiveEvent_.Set();
                }
                if(sslNetworkStreamReceiveEvent_ != null)
                {
                    sslNetworkStreamReceiveEvent_.Set();
                }
            }
            catch(ObjectDisposedException)
            {

            }

            currentOutboundsMutex_.WaitOne();
            foreach(Outbound outbound in currentOutbounds_.Keys)
            {
                outbound.Close();
            }
            currentOutbounds_.Clear();
            currentOutboundsMutex_.ReleaseMutex();
            currentOutboundsMutex_.Close();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void Complete(ProxyRequest request)
        {
            Outbound sendOutbound = null;
            try
            {
                currentOutboundsMutex_.WaitOne();
                foreach(Outbound o in currentOutbounds_.Keys)
                {
                    if(currentOutbounds_[o] == request)
                    {
                        sendOutbound = o;
                        break;
                    }
                }
                currentOutboundsMutex_.ReleaseMutex();
            }
            catch(ObjectDisposedException)
            {

            }

            request.completedHeader_ = request.header_;
            if(request.data_ != null)
            {
                request.completedHeader_ += "\r\nContent-Length: " + Convert.ToString(request.data_.Length) + "\r\n\r\n" + request.data_;
            }
            else
            {
                request.completedHeader_ += "\r\n\r\n";
            }

            if(sendOutbound != null)
            {
                if(sendOutbound.useRemoteProxy_)
                {
                    Match noUrlMatch = Regex.Match(request.completedHeader_, "([A-Z]+)\\s+/(.*?)\\s+(HTTP/\\d.\\d)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if(noUrlMatch.Success)
                    {
                        Match hostMatch = Regex.Match(request.completedHeader_, "^Host:\\s+(\\S*)?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        if(hostMatch.Success)
                        {
                            string host = hostMatch.Groups[1].Value;

                            string insert = (sendOutbound.ssl_ ? "https://" : "http://") + host + "/";
                            request.completedHeader_ = Regex.Replace(request.completedHeader_,
                                                                     "([A-Z]+)\\s+/(.*)\\s+(HTTP/\\d.\\d)",
                                                                     "$1 " + insert + "$2 $3", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        }
                    }
                }

                if(wwwProxy_.ntlmEnabled_)
                {
                    MatchCollection basicCredentials = Regex.Matches(request.completedHeader_,
                                                                         "^(Proxy-)*Authorization:\\s+Basic\\s+([a-zA-Z0-9=]*)[ \t]*[\r\n]*",
                                                                         RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if(basicCredentials.Count > 0)
                    {
                        Match urlMatch = Regex.Match(request.completedHeader_, "([A-Z]+)\\s+(.*?)\\s+HTTP/\\d.\\d\r$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        if(urlMatch.Success)
                        {
                            string ntlmSite = sendOutbound.host_ + ":" + sendOutbound.port_;
                            string ntlmPath = urlMatch.Groups[2].Value;

                            INtlm wwwProxyNtlm = null;

                            try
                            {
                                wwwProxy_.ntlmListMutex_.WaitOne();

                                foreach(INtlm w in wwwProxy_.ntlmList_)
                                {
                                    if((w.Site == ntlmSite) && (w.Path == ntlmPath))
                                    {
                                        foreach(Match basicCredentialsMatch in basicCredentials)
                                        {
                                            if((w.Type == basicCredentialsMatch.Groups[1].Value) ||
                                               ((w.Type == "WWW") && (basicCredentialsMatch.Groups[1].Value == "")))
                                            {
                                                w.Basic = basicCredentialsMatch.Groups[2].Value;
                                                wwwProxyNtlm = w;
                                                break;
                                            }
                                        }

                                        if(wwwProxyNtlm != null)
                                        {
                                            break;
                                        }
                                    }
                                }

                                if(wwwProxyNtlm == null)
                                {
                                    foreach(INtlm w in wwwProxy_.ntlmList_)
                                    {
                                        if((w.Site == ntlmSite) && (w.Basic != null))
                                        {
                                            foreach(Match basicCredentialsMatch in basicCredentials)
                                            {
                                                if((w.Basic == basicCredentialsMatch.Groups[2].Value) &&
                                                   ((w.Type == basicCredentialsMatch.Groups[1].Value) ||
                                                    ((w.Type == "WWW") && (basicCredentialsMatch.Groups[1].Value == ""))))
                                                {
                                                    INtlm cloneWwwProxyNtlm = wwwProxy_.ntlmFactory_.CreateInstance();
                                                    if(cloneWwwProxyNtlm != null)
                                                    {
                                                        cloneWwwProxyNtlm.Site = ntlmSite;
                                                        cloneWwwProxyNtlm.Path = ntlmPath;
                                                        cloneWwwProxyNtlm.Type = w.Type;
                                                        cloneWwwProxyNtlm.Basic = w.Basic;
                                                        wwwProxy_.ntlmList_.Add(cloneWwwProxyNtlm);
                                                        wwwProxyNtlm = cloneWwwProxyNtlm;
                                                    }
                                                    break;
                                                }
                                            }
                                            if(wwwProxyNtlm != null)
                                            {
                                                break;
                                            }
                                        }
                                    }
                                }

                                if(wwwProxyNtlm != null)
                                {
                                    string[] credentials = wwwProxy_.defaultEncoding_.GetString(Convert.FromBase64String(wwwProxyNtlm.Basic)).Split(':');

                                    string ntlmNegotiate = null;
                                    string domain = sendOutbound.host_;
                                    string username = credentials[0];
                                    string password = credentials[1];

                                    if(username.Contains("\\"))
                                    {
                                        int domainUsernameSeparator = username.IndexOf('\\');
                                        if((domainUsernameSeparator > 0) && (domainUsernameSeparator < (username.Length - 1)))
                                        {
                                            domain = username.Substring(0, domainUsernameSeparator);
                                            username = username.Substring(username.IndexOf('\\') + 1);
                                        }
                                    }

                                    wwwProxyNtlm.Initialise(domain, username, password);
                                    ntlmNegotiate = wwwProxyNtlm.Continue(null);
                                    if(ntlmNegotiate == null)
                                    {
                                        ntlmNegotiate = "???";
                                    }
                                    string authReplace = "^(" + (wwwProxyNtlm.Type == "Proxy" ? "Proxy-" : "") +
                                                         "Authorization:\\s+)Basic\\s+[a-zA-Z0-9=]+[ \t]*([\r\n]*)";
                                    request.completedHeader_ = Regex.Replace(request.completedHeader_,
                                                                             authReplace,
                                                                             "${1}NTLM " + ntlmNegotiate + "${2}",
                                                                             RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                }
                            }
                            finally
                            {
                                wwwProxy_.ntlmListMutex_.ReleaseMutex();
                            }
                        }
                    }
                }

                wwwProxy_.log_.Write("Inbound.Complete()", request.completedHeader_);

                sendOutbound.Connect();

                try
                {
                    sendOutbound.Send(request);
                }
                catch(IOException)
                {

                }
                catch(SocketException)
                {

                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal int Receive(byte[] receiveBuffer, int offset, int size)
        {
            int bytesRead = 0;
            if(sslStream_ != null)
            {
                asyncBytesRead = 0;
                sslNetworkStreamReceiveEvent_.Reset();
                IAsyncResult asyncResult = sslStream_.BeginRead(receiveBuffer, offset, size, new AsyncCallback(OnSslStreamRead), 0);

                WaitHandle[] waitHandles = new WaitHandle[2];
                waitHandles[0] = inboundThreadExitEvent_;
                waitHandles[1] = sslNetworkStreamReceiveEvent_;
                if(WaitHandle.WaitAny(waitHandles, Timeout.Infinite, false) == 0)
                {
                    bytesRead = 0;
                }
                else
                {
                    bytesRead = asyncBytesRead;
                }
            }
            else
            {
                asyncBytesRead = 0;
                inboundSocketReceiveEvent_.Reset();

                if(inboundSocket_.Connected)
                {
                    IAsyncResult asyncResult = inboundSocket_.BeginReceive(receiveBuffer, offset, size, SocketFlags.None, new AsyncCallback(OnSocketReceive), 0);

                    WaitHandle[] waitHandles = new WaitHandle[2];
                    waitHandles[0] = inboundThreadExitEvent_;
                    waitHandles[1] = inboundSocketReceiveEvent_;
                    if(WaitHandle.WaitAny(waitHandles, Timeout.Infinite, false) == 0)
                    {
                        bytesRead = 0;
                    }
                    else
                    {
                        bytesRead = asyncBytesRead;
                    }
                }
            }
            return bytesRead;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void Send(byte[] sendBuffer, int offset, int size)
        {
            wwwProxy_.log_.Write("Inbound.Send()", wwwProxy_.defaultEncoding_.GetString(sendBuffer, offset, size));

            if(sslStream_ != null)
            {
                sslStream_.Write(sendBuffer, 0, size);
            }
            else if(inboundSocket_ != null)
            {
                if(inboundSocket_.Connected)
                {
                    inboundSocket_.Send(sendBuffer, offset, size, SocketFlags.None);
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void StartKeepAliveTimer()
        {
            if(keepAliveTimer_ != null)
            {
                StopKeepAliveTimer();
            }
            keepAliveTimer_ = new Timer(new TimerCallback(OnKeepAliveTimer), null, 10000, 10000);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void StopKeepAliveTimer()
        {
            if(keepAliveTimer_ != null)
            {
                keepAliveTimer_.Dispose();
                keepAliveTimer_ = null;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnDisconnect(Outbound outbound)
        {
            try
            {
                currentOutboundsMutex_.WaitOne();
                currentOutbounds_.Remove(outbound);
                currentOutboundsMutex_.ReleaseMutex();
            }
            catch(ObjectDisposedException)
            {

            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private bool OnRemoteCertificateValidation(Object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal int OnRequest(byte[] receiveBuffer, int bytesReceived, bool internalRequest)
        {
            string proxyRequest = wwwProxy_.defaultEncoding_.GetString(receiveBuffer, 0, bytesReceived);
            int processedRequestBytes = 0;

            wwwProxy_.log_.Write("Inbound.OnRequest()", proxyRequest);

            Match httpMatch = Regex.Match(proxyRequest, "^([A-Z]+)\\s+(\\S*)?\\s+HTTP/\\d.\\d\r$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if(httpMatch.Success)
            {
                string defaultCharset = "iso-8859-1";
                string charset = defaultCharset;

                string httpVerb = httpMatch.Groups[1].Value;
               
                if(httpVerb != "CONNECT")
                {
                    Match getEndMatch = Regex.Match(proxyRequest, "\r\n\r\n", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if(getEndMatch.Success)
                    {
                        string requestRaw = proxyRequest.Substring(0, getEndMatch.Index);
                        string postRaw = null;
                        processedRequestBytes = getEndMatch.Index + 4;

                        Match contentEncodingTypeMatch = Regex.Match(proxyRequest, "^Content-Type:\\s+\\S+?\\s*;\\s*charset=(\\S+)?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        if(contentEncodingTypeMatch.Success)
                        {
                            charset = contentEncodingTypeMatch.Groups[1].Value.ToLower();
                        }

                        Encoding requestEncoding = null;
                        try
                        {
                            requestEncoding = Encoding.GetEncoding(charset);
                        }
                        catch(ArgumentException)
                        {
                            requestEncoding = wwwProxy_.defaultEncoding_;
                        }

                        Match contentLengthMatch = Regex.Match(requestRaw, "^Content-Length:\\s+(\\d+)[ \t]*[\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        if(contentLengthMatch.Success)
                        {
                            int contentsLength = Convert.ToInt32(contentLengthMatch.Groups[1].Value);
                            if(proxyRequest.Length >= processedRequestBytes + contentsLength)
                            {
                                postRaw = requestEncoding.GetString(receiveBuffer, processedRequestBytes, contentsLength);
                                processedRequestBytes += contentsLength;

                                for(int i = processedRequestBytes; i < bytesReceived; ++i)
                                {
                                    if((receiveBuffer[i] == '\r') || (receiveBuffer[i] == '\n'))
                                    {
                                        processedRequestBytes++;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                requestRaw = null;
                                postRaw = null;
                            }
                        }

                        if(requestRaw != null)
                        {
                            requestRaw = Regex.Replace(requestRaw, "([A-Z]+)\\s+([a-zA-Z]+://.*?/)(.*?)\\s+(HTTP/\\d.\\d)", "$1 /$3 $4", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                            requestRaw = Regex.Replace(requestRaw, "^Accept-Encoding:\\s+(.*)?[ \t]*[\r\n]*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                            requestRaw = Regex.Replace(requestRaw, "^Content-Length:\\s+(.*)?[ \t]*[\r\n]*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                            requestRaw = Regex.Replace(requestRaw, "^Proxy-Connection:\\s+(.*)?[ \t]*[\r\n]*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                            requestRaw = requestRaw.TrimEnd(" \t\r\n".ToCharArray());

                            Match hostMatch = Regex.Match(requestRaw, "^Host:\\s+(\\S*)?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                            if(hostMatch.Success)
                            {
                                string host = hostMatch.Groups[1].Value;
                                string port = ssl_ ? "443" : "80";

                                Match portMatch = Regex.Match(host, "(:)(\\d{1,5})");
                                if(portMatch.Success)
                                {
                                    port = host.Substring(portMatch.Groups[2].Index, portMatch.Groups[2].Length);
                                    host = host.Substring(0, portMatch.Groups[1].Index);
                                }

                                ProxyRequest request = new ProxyRequest();
                                request.inbound_ = this;
                                request.ssl_ = ssl_;
                                request.header_ = requestRaw;
                                request.data_ = postRaw;

                                Outbound sendOutbound = null;
                                currentOutboundsMutex_.WaitOne();
                                foreach(Outbound o in currentOutbounds_.Keys)
                                {
                                    if((o.host_ == host) &&
                                       (o.port_ == Convert.ToUInt16(port)) &&
                                       (currentOutbounds_[o] == null))
                                    {
                                        sendOutbound = o;
                                        currentOutbounds_[o] = request;
                                        break;
                                    }
                                }
                                currentOutboundsMutex_.ReleaseMutex();

                                if(sendOutbound == null)
                                {
                                    sendOutbound = new Outbound(wwwProxy_, this, ssl_, host, Convert.ToUInt16(port));

                                    currentOutboundsMutex_.WaitOne();
                                    currentOutbounds_.Add(sendOutbound, request);
                                    currentOutboundsMutex_.ReleaseMutex();
                                }

                                if(!internalRequest)
                                {
                                    wwwProxy_.OnRequest(request);
                                }
                                else
                                {
                                    wwwProxy_.Pass(request);
                                }
                                StartKeepAliveTimer();
                            }
                        }
                        else
                        {
                            processedRequestBytes = 0;
                        }
                    }
                }
                else
                {
                    string connectHost = httpMatch.Groups[2].Value;

                    string host = connectHost;
                    string port = "443";

                    Match portMatch = Regex.Match(host, "(:)(\\d{1,5})");
                    if(portMatch.Success)
                    {
                        port = host.Substring(portMatch.Groups[2].Index, portMatch.Groups[2].Length);
                        host = host.Substring(0, portMatch.Groups[1].Index);
                    }

                    Outbound outbound = new Outbound(wwwProxy_, this, true, host, Convert.ToUInt16(port));

                    currentOutboundsMutex_.WaitOne();
                    currentOutbounds_.Add(outbound, null);
                    currentOutboundsMutex_.ReleaseMutex();

                    Match connectEndMatch = Regex.Match(proxyRequest, "\r\n\r\n", RegexOptions.Multiline);
                    if(connectEndMatch.Success)
                    {
                        processedRequestBytes = connectEndMatch.Index + 4;
                    }

                    byte[] sslResponseBytes = wwwProxy_.defaultEncoding_.GetBytes("HTTP/1.0 200 OK\r\n\r\n");
                    Send(sslResponseBytes, 0, sslResponseBytes.Length);

                    ssl_ = true;
                    sslNetworkStream_ = new NetworkStream(inboundSocket_);
                    sslStream_ = new SslStream(sslNetworkStream_, false, new RemoteCertificateValidationCallback(OnRemoteCertificateValidation));

                    X509Certificate serverCertificate = new X509Certificate((wwwProxy_.certificate_ != null) ? wwwProxy_.certificate_ : "WwwProxy.cer");
                    sslStream_.AuthenticateAsServer(serverCertificate);
                }
            }
            else
            {
                int lineEnd = proxyRequest.IndexOf("\r\n");
                throw new Exception("Unhandled Request \"" + proxyRequest.Substring(0, (lineEnd > 0) ? lineEnd : proxyRequest.Length) + "\"");
            }

            return processedRequestBytes;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnKeepAliveTimer(object state)
        {
            byte[] continueBytes = wwwProxy_.defaultEncoding_.GetBytes("HTTP/1.1 100 CONTINUE\r\n\r\n");
            try
            {
                Send(continueBytes, 0, continueBytes.Length);
            }
            catch(IOException)
            {

            }
            catch(SocketException)
            {

            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnSocketReceive(IAsyncResult asyncResult)
        {
            if(asyncResult.IsCompleted)
            {
                try
                {
                    asyncBytesRead = (Int32)inboundSocket_.EndReceive(asyncResult);
                }
                catch (IOException)
                {
                    asyncBytesRead = 0;
                }
                catch (ObjectDisposedException)
                {
                    asyncBytesRead = 0;
                }
                catch (SocketException)
                {
                    asyncBytesRead = 0;
                }

                try
                {
                    inboundSocketReceiveEvent_.Set();
                }
                catch(ObjectDisposedException)
                {
                    asyncBytesRead = 0;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        
        internal void OnSslStreamRead(IAsyncResult asyncResult)
        {
            if(asyncResult.IsCompleted)
            {
                try
                {
                    asyncBytesRead = (Int32)sslStream_.EndRead(asyncResult);
                }
                catch(IOException)
                {
                    asyncBytesRead = 0;
                }
                catch(ObjectDisposedException)
                {
                    asyncBytesRead = 0;
                }

                try
                {
                    sslNetworkStreamReceiveEvent_.Set();
                }
                catch(ObjectDisposedException)
                {
                    asyncBytesRead = 0;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void RunSocket()
        {
            byte[] receiveBuffer = new byte[4096];
            int receiveBufferCount = 0;

            wwwProxy_.OnConnect(this);

            while(!inboundThreadExitEvent_.WaitOne(0, false))
            {
                try
                {
                    if(inboundSocket_ != null)
                    {
                        if((receiveBuffer.Length - receiveBufferCount) < 4096)
                        {
                            int newReceiveBufferLength = receiveBuffer.Length;
                            while((newReceiveBufferLength - receiveBufferCount) < 4096)
                            {
                                newReceiveBufferLength *= 2;
                            }
                            byte[] newReceiveBuffer = new byte[newReceiveBufferLength];
                            Array.Copy(receiveBuffer, 0, newReceiveBuffer, 0, receiveBuffer.Length);
                            receiveBuffer = newReceiveBuffer;
                        }
                        
                        if(inboundSocket_.Connected)
                        {
                            int bytesReceived = Receive(receiveBuffer, receiveBufferCount, receiveBuffer.Length - receiveBufferCount);
                            receiveBufferCount += bytesReceived;
                            if(receiveBufferCount > 0)
                            {
                                while(receiveBufferCount > 0)
                                {
                                    int bytesParsed = OnRequest(receiveBuffer, receiveBufferCount, false);
                                    if(bytesParsed > 0)
                                    {
                                        for(int i = 0; i < receiveBuffer.Length - bytesParsed; ++i)
                                        {
                                            receiveBuffer[i] = receiveBuffer[bytesParsed + i];
                                        }
                                        receiveBufferCount -= bytesParsed;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch(Exception e)
                {
                    wwwProxy_.OnError(this, null, e, e.Message);
                    break;
                }
            }

            if(inboundSocket_ != null)
            {
                if(inboundSocket_.Connected)
                {
                    inboundSocket_.Shutdown(SocketShutdown.Both);
                    inboundSocket_.Disconnect(false);
                }
                inboundSocket_.Close();
            }

            if(inboundThreadExitEvent_ != null)
            {
                inboundThreadExitEvent_.Close();
            }
            if(inboundSocketReceiveEvent_ != null)
            {
                inboundSocketReceiveEvent_.Close();
            }
            if(sslNetworkStreamReceiveEvent_ != null)
            {
                sslNetworkStreamReceiveEvent_.Close();
            }

            if(sslNetworkStream_ != null)
            {
                sslNetworkStream_.Close();
            }
            if(sslStream_ != null)
            {
                sslStream_.Close();
            }

            wwwProxy_.OnDisconnect(this);

            if(inboundThread_ != null)
            {
                inboundThread_ = null;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
