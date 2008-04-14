///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxy
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Outbound.cs
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
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WwwProxy
{
    internal class Outbound
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal WwwProxy wwwProxy_ = null;

        internal Inbound inbound_ = null;
        internal ProxyRequest request_ = null;
        internal ProxyResponse response_ = null;
        
        internal string host_ = null;
        internal ushort port_ = 0;
        internal bool useRemoteProxy_ = false;
        
        internal bool ssl_ = false;
        internal bool sslProxyNegotiationSent_ = false; 
        
        private bool completableResponse_ = false;
        private bool responseCloseInbound_ = false;

        private Thread outboundThread_ = null;
        private ManualResetEvent outboundThreadExitEvent_ = null;
        private ManualResetEvent outboundSocketReceiveEvent_ = null;
        private ManualResetEvent sslNetworkStreamReceiveEvent_ = null;

        private IPEndPoint outboundIpEndPoint_ = null;
        private Socket outboundSocket_ = null;

        private int asyncBytesRead_ = 0;

        private NetworkStream sslNetworkStream_ = null;
        private SslStream sslStream_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal Outbound(WwwProxy wwwProxy, Inbound inbound, bool ssl, string host, ushort port)
        {
            wwwProxy_ = wwwProxy;
            inbound_ = inbound;

            ssl_ = ssl;
            host_ = host.ToLower();
            port_ = port;

            if(wwwProxy.remoteProxy_ != null)
            {
                if(wwwProxy_.remoteProxyExceptions_ != null)
                {
                    bool exceptionMatch = false; 
                    
                    bool hostIsIp = Regex.Match(host_, @"^[\*\d]{1,3}\.[\*\d]{1,3}\.[\*\d]{1,3}\.[\*\d]{1,3}$").Success;
                    string[] hostElements = host_.Split('.');

                    foreach(string e in wwwProxy_.remoteProxyExceptions_)
                    {
                        bool exceptionIsIp = Regex.Match(e, @"^[\*\d]{1,3}\.[\*\d]{1,3}\.[\*\d]{1,3}\.[\*\d]{1,3}$").Success;
                        string[] exceptionElements = e.Split('.');

                        if(hostIsIp == exceptionIsIp)
                        {
                            for(int i = 0; i < Math.Min(hostElements.Length, exceptionElements.Length); ++i)
                            {
                                string hostElement = hostElements[hostIsIp ? i : hostElements.Length - i - 1];
                                string exceptionElement = exceptionElements[exceptionIsIp ? i : exceptionElements.Length - i - 1];

                                if((hostElement != exceptionElement) && (exceptionElement != "*"))
                                {
                                    exceptionMatch = false;
                                    break;
                                }
                                else
                                {
                                    exceptionMatch = true;
                                }
                            }
                        }

                        if(exceptionMatch)
                        {
                            break;
                        }
                    }

                    if(!exceptionMatch)
                    {
                        outboundIpEndPoint_ = wwwProxy.remoteProxy_;
                        useRemoteProxy_ = true;
                    }
                }
                else
                {
                    outboundIpEndPoint_ = wwwProxy.remoteProxy_;
                    useRemoteProxy_ = true;
                }
            }

            if(outboundIpEndPoint_ == null)
            {
                if(Regex.Match(host, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$").Success)
                {
                    outboundIpEndPoint_ = new IPEndPoint(IPAddress.Parse(host_), port);
                }
                else
                {
                    outboundIpEndPoint_ = new IPEndPoint(IPAddress.Parse(System.Net.Dns.GetHostEntry(host_).AddressList[0].ToString()), port_);
                }
            }

            outboundThreadExitEvent_ = new ManualResetEvent(false);
            outboundSocketReceiveEvent_ = new ManualResetEvent(false);
            sslNetworkStreamReceiveEvent_ = new ManualResetEvent(false);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void Close()
        {
            try
            {
                if(outboundThreadExitEvent_ != null)
                {
                    outboundThreadExitEvent_.Set();
                }
                if(outboundSocketReceiveEvent_ != null)
                {
                    outboundSocketReceiveEvent_.Set();
                }
                if(sslNetworkStreamReceiveEvent_ != null)
                {
                    sslNetworkStreamReceiveEvent_.Set();
                }
            }
            catch(ObjectDisposedException)
            {

            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void Complete(ProxyResponse response)
        {
            if(response.Completable && (response.header_ != null))
            {
                string header = response.header_;
                string contents = response.contents_;

                byte[] headerBytes = null;
                byte[] contentsBytes = new byte[0];
                if(contents != null)
                {
                    contentsBytes = response.encoding_.GetBytes(contents);
                    header += "\r\nContent-Length: " + Convert.ToString(contentsBytes.Length) + "\r\n\r\n";
                    headerBytes = wwwProxy_.defaultEncoding_.GetBytes(header);
                }

                wwwProxy_.log_.Write("Outbound.Complete()", header + ((contents != null) ? contents : ""));

                byte[] responseBytes = new byte[headerBytes.Length + contentsBytes.Length];

                Array.Copy(headerBytes, 0, responseBytes, 0, headerBytes.Length);
                Array.Copy(contentsBytes, 0, responseBytes, headerBytes.Length, contentsBytes.Length);

                inbound_.StopKeepAliveTimer();

                try
                {
                    inbound_.Send(responseBytes, 0, responseBytes.Length);
                }
                catch(IOException)
                {

                }
                catch(SocketException)
                {

                }
            }

            request_ = null;

            response_.outbound_ = null;
            response_ = null;

            if(completableResponse_)
            {
                completableResponse_ = false;
                responseCloseInbound_ = false;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void Connect()
        {
            outboundThread_ = new Thread(RunSocket);
            outboundThread_.Start();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal int Receive(byte[] receiveBuffer, int offset, int size)
        {
            int bytesRead = 0;
            if(sslStream_ != null)
            {
                asyncBytesRead_ = 0;
                sslNetworkStreamReceiveEvent_.Reset();
                IAsyncResult asyncResult = null;
                try
                {
                    asyncResult = sslStream_.BeginRead(receiveBuffer, offset, size, new AsyncCallback(OnSslStreamRead), 0);
                }
                catch (IOException)
                {
                    bytesRead = 0;
                    sslNetworkStreamReceiveEvent_.Set();
                }
                
                WaitHandle[] waitHandles = new WaitHandle[2];
                waitHandles[0] = outboundThreadExitEvent_;
                waitHandles[1] = sslNetworkStreamReceiveEvent_;
                if(WaitHandle.WaitAny(waitHandles, Timeout.Infinite, false) == 0)
                {
                    bytesRead = 0;
                }
                else
                {
                    bytesRead = asyncBytesRead_;
                }
            }
            else
            {
                asyncBytesRead_ = 0;
                outboundSocketReceiveEvent_.Reset();
                IAsyncResult asyncResult = outboundSocket_.BeginReceive(receiveBuffer, offset, size, SocketFlags.None, new AsyncCallback(OnSocketReceive), 0);

                WaitHandle[] waitHandles = new WaitHandle[2];
                waitHandles[0] = outboundThreadExitEvent_;
                waitHandles[1] = outboundSocketReceiveEvent_;
                if(WaitHandle.WaitAny(waitHandles, Timeout.Infinite, false) == 0)
                {
                    bytesRead = 0;
                }
                else
                {
                    bytesRead = asyncBytesRead_;
                }
            }
            return bytesRead;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void Send(ProxyRequest request)
        {
            request_ = request;

            if(ssl_)
            {
                if((sslStream_ != null) && (sslStream_.IsAuthenticated))
                {
                    byte[] requestBytes = wwwProxy_.defaultEncoding_.GetBytes(request_.completedHeader_);
                    Send(requestBytes, 0, requestBytes.Length);
                    request_.sent_ = true;
                }
            }
            else
            {
                if((outboundSocket_ != null) && outboundSocket_.Connected)
                {
                    byte[] requestBytes = wwwProxy_.defaultEncoding_.GetBytes(request_.completedHeader_);
                    Send(requestBytes, 0, requestBytes.Length);
                    request_.sent_ = true;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void Send(byte[] sendBuffer, int offset, int size)
        {
            wwwProxy_.log_.Write("Outbound.Send()", wwwProxy_.defaultEncoding_.GetString(sendBuffer, offset, size));

            if(ssl_)
            {
                if(sslStream_ != null)
                {
                    sslStream_.Write(sendBuffer, 0, size); 
                }
            }
            else
            {
                outboundSocket_.Send(sendBuffer, offset, size, SocketFlags.None);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private bool OnRemoteCertificateValidation(Object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private int OnResponse(byte[] receiveBuffer, int bytesReceived, bool bConnectionClosed)
        {
            string proxyResponse = wwwProxy_.defaultEncoding_.GetString(receiveBuffer, 0, bytesReceived);
            int processedResponseBytes = 0;

            wwwProxy_.log_.Write("Outbound.OnResponse()", proxyResponse);

            string header = null;
            string contents = null;
            byte[] contentsBytes = null;

            string defaultCharset = "iso-8859-1";
            string charset = defaultCharset;

            Match httpResponseMatch = Regex.Match(proxyResponse, "^HTTP/\\d\\.\\d\\s+(\\d+)\\s+", RegexOptions.IgnoreCase);
            if(httpResponseMatch.Success)
            {
                ushort httpResponseCode = Convert.ToUInt16(httpResponseMatch.Groups[1].Value);
                if((httpResponseCode >= 100) && (httpResponseCode <= 199))
                {
                    inbound_.StopKeepAliveTimer();
                    inbound_.Send(receiveBuffer, 0, bytesReceived);
                    processedResponseBytes = bytesReceived;
                }
                else
                {
                    int contentsIndex = 0;

                    for(int i = 0; i < (proxyResponse.Length - 3); ++i)
                    {
                        if((receiveBuffer[i] == '\r') &&
                           (receiveBuffer[i + 1] == '\n') &&
                           (receiveBuffer[i + 2] == '\r') &&
                           (receiveBuffer[i + 3] == '\n'))
                        {
                            contentsIndex = i + 4;
                            header = wwwProxy_.defaultEncoding_.GetString(receiveBuffer, 0, i);
                            contentsBytes = new byte[bytesReceived - (contentsIndex)];
                            Array.Copy(receiveBuffer, contentsIndex, contentsBytes, 0, bytesReceived - (contentsIndex));
                            break;
                        }
                    }

                    if(header != null)
                    {
                        Match connectionCloseMatch = Regex.Match(header, "^(Proxy-)*(Connection)+:\\s+close", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        Match contentEncodingTypeMatch = Regex.Match(header, "^Content-Type:\\s+\\S+?\\s*;\\s*charset=(\\S+)?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        Match contentLengthMatch = Regex.Match(header, "^Content-Length:\\s+(\\d+)[\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        Match contentTextTypeMatch = Regex.Match(header, "^Content-Type:\\s+text|application/x-javascript", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        Match transferEncodingMatch = Regex.Match(header, "^Transfer-Encoding:\\s+chunked\\s*[\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline);

                        if(contentEncodingTypeMatch.Success)
                        {
                            charset = contentEncodingTypeMatch.Groups[1].Value.ToLower();
                        }

                        Encoding responseEncoding = null;
                        try
                        {
                            responseEncoding = Encoding.GetEncoding(charset);
                        }
                        catch(ArgumentException)
                        {
                            responseEncoding = wwwProxy_.defaultEncoding_;
                        }

                        completableResponse_ = contentTextTypeMatch.Success || (httpResponseCode >= 300);
                        if(!completableResponse_)
                        {
                            if(connectionCloseMatch.Success)
                            {
                                responseCloseInbound_ = true;
                            }

                            inbound_.StopKeepAliveTimer();
                            inbound_.Send(receiveBuffer, 0, bytesReceived);
                            processedResponseBytes = bytesReceived;
                        }
                        else
                        {
                            if(contentLengthMatch.Success)
                            {
                                int contentsLength = Convert.ToInt32(contentLengthMatch.Groups[1].Value);
                                if(contentsBytes.Length >= contentsLength)
                                {
                                    header = Regex.Replace(header, "^Content-Length:\\s+(\\d+)[\r\n]*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                    header = header.TrimEnd("\r\n".ToCharArray());

                                    contents = responseEncoding.GetString(contentsBytes, 0, contentsBytes.Length);
                                    processedResponseBytes = contentsIndex + contentsLength;
                                }
                            }
                            else if(transferEncodingMatch.Success)
                            {
                                int deChunkingIndex = 0;
                                byte[] chunkedContents = new byte[contentsBytes.Length];
                                Array.Copy(contentsBytes, chunkedContents, contentsBytes.Length);

                                string deChunkedContents = "";
                                
                                while(true)
                                {
                                    int chunkLengthEnd = -1;
                                    for(int i = deChunkingIndex; i < chunkedContents.Length; ++i)
                                    {
                                        if((chunkedContents[i] == '\r') && (chunkedContents[i + 1] == '\n'))
                                        {
                                            chunkLengthEnd = i;
                                            break;
                                        }
                                    }
                                    if(chunkLengthEnd != -1)
                                    {
                                        int chunkLength = Convert.ToInt32(responseEncoding.GetString(chunkedContents,
                                                                                                     deChunkingIndex,
                                                                                                     chunkLengthEnd - deChunkingIndex).Trim(), 16);
                                        deChunkingIndex = chunkLengthEnd + 2;

                                        if(chunkLength != 0)
                                        {
                                            if(chunkedContents.Length >= (deChunkingIndex + chunkLength))
                                            {
                                                deChunkedContents += responseEncoding.GetString(chunkedContents, deChunkingIndex, chunkLength);
                                            }
                                            else
                                            {
                                                deChunkedContents = null;
                                            }
                                        }

                                        deChunkingIndex += chunkLength;
                                        while((deChunkingIndex < chunkedContents.Length) &&
                                              ((chunkedContents[deChunkingIndex] == '\r') ||
                                               (chunkedContents[deChunkingIndex] == '\n')))
                                        {
                                            deChunkingIndex++;
                                        }

                                        if(chunkLength == 0)
                                        {
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        deChunkedContents = null;
                                        break;
                                    }
                                }

                                if(deChunkedContents != null)
                                {
                                    header = Regex.Replace(header, "^Transfer-Encoding:\\s+chunked(.*?)[\r\n]*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                    header = header.TrimEnd("\r\n".ToCharArray());
                                    contents = deChunkedContents;

                                    processedResponseBytes = contentsIndex + deChunkingIndex;
                                }
                            }
                            else
                            {
                                if(((connectionCloseMatch.Success) && (bConnectionClosed)) || (bConnectionClosed) ||
                                   (httpResponseCode == 204) || (httpResponseCode == 304)) // http://www.w3.org/Protocols/rfc2616/rfc2616-sec10.html
                                {
                                    contents = responseEncoding.GetString(contentsBytes, 0, contentsBytes.Length);
                                    processedResponseBytes = bytesReceived;
                                }
                            }
                        }

                        if(processedResponseBytes != 0)
                        {
                            if(completableResponse_)
                            {
                                header = Regex.Replace(header, "^Proxy-Connection:\\s+(.*)?[\r\n]*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                header = header.TrimEnd("\r\n".ToCharArray());
                            }

                            if(httpResponseCode == 401)
                            {
                                if(wwwProxy_.ntlmEnabled_)
                                {
                                    Match urlMatch = Regex.Match(request_.completedHeader_, "([A-Z]+)\\s+(.*?)\\s+HTTP/\\d.\\d\r$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                    if(urlMatch.Success)
                                    {
                                        try
                                        {
                                            wwwProxy_.ntlmListMutex_.WaitOne();

                                            string ntlmSite = host_ + ":" + port_;
                                            string ntlmPath = urlMatch.Groups[2].Value;

                                            Match wwwAuthenticateBasicMatch = Regex.Match(header, "^Www-Authenticate:\\s+Basic(.*)?[\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                            if(wwwAuthenticateBasicMatch.Success)
                                            {
                                                List<WwwProxyNtlm.WwwProxyNtlm> toRemove = new List<WwwProxyNtlm.WwwProxyNtlm>();
                                                foreach(WwwProxyNtlm.WwwProxyNtlm w in wwwProxy_.ntlmList_)
                                                {
                                                    if(w.Site == ntlmSite)
                                                    {
                                                        toRemove.Add(w);
                                                    }
                                                }

                                                foreach(WwwProxyNtlm.WwwProxyNtlm w in toRemove)
                                                {
                                                    wwwProxy_.ntlmList_.Remove(w);
                                                }
                                            }

                                            WwwProxyNtlm.WwwProxyNtlm wwwProxyNtlm = null;
                                            foreach(WwwProxyNtlm.WwwProxyNtlm w in wwwProxy_.ntlmList_)
                                            {
                                                if((w.Site == ntlmSite) && (w.Path == ntlmPath))
                                                {
                                                    wwwProxyNtlm = w;
                                                    break;
                                                }
                                            }

                                            Match wwwAuthenticateNegotiateMatch = Regex.Match(header, "^Www-Authenticate:\\s+Negotiate[\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                            Match wwwAuthenticateNTLMMatch = Regex.Match(header, "^Www-Authenticate:\\s+NTLM[\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                            Match wwwAuthenticateNTLMResponseMatch = Regex.Match(header, "^Www-Authenticate:\\s+NTLM\\s+([A-Za-z0-9+/=]*)[\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline);

                                            if(wwwAuthenticateNegotiateMatch.Success && wwwAuthenticateNTLMMatch.Success)
                                            {
                                                header = Regex.Replace(header, "^Www-Authenticate:\\s+Negotiate[\r\n]*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                                header = Regex.Replace(header, "(^Www-Authenticate:\\s+)NTLM([\r\n]*)", "${1}Basic Realm=\"WwwProxy NTLM\"${2}", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                                header = header.TrimEnd("\r\n".ToCharArray());

                                                if(wwwProxyNtlm == null)
                                                {
                                                    wwwProxyNtlm = new WwwProxyNtlm.WwwProxyNtlm();
                                                    wwwProxyNtlm.Site = ntlmSite;
                                                    wwwProxyNtlm.Path = ntlmPath;
                                                    wwwProxy_.ntlmList_.Add(wwwProxyNtlm);
                                                }
                                                else
                                                {
                                                    wwwProxyNtlm.Reset();
                                                }
                                            }
                                            else if(wwwAuthenticateNTLMResponseMatch.Success)
                                            {
                                                request_.completedHeader_ = Regex.Replace(request_.completedHeader_, "^(Authorization:\\s+NTLM\\s+)[a-zA-Z0-9=]+([\r\n]*)", "${1}" + wwwProxyNtlm.Continue(wwwAuthenticateNTLMResponseMatch.Groups[1].Value) + "${2}", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                                Send(request_);
                                                header = null;
                                            }
                                        }
                                        finally
                                        {
                                            wwwProxy_.ntlmListMutex_.ReleaseMutex();
                                        }
                                    }
                                }
                            }

                            if(header != null)
                            {
                                response_ = new ProxyResponse();
                                response_.encoding_ = responseEncoding;
                                response_.outbound_ = this;
                                response_.request_ = request_;
                                response_.completable_ = completableResponse_;
                                response_.header_ = header;
                                response_.contents_ = contents;
                                wwwProxy_.OnResponse(response_);
                            }
                        }
                    }
                }
            }
            else
            {
                inbound_.StopKeepAliveTimer(); 
                inbound_.Send(receiveBuffer, 0, bytesReceived);
                processedResponseBytes = bytesReceived;
            }

            return processedResponseBytes;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnSocketReceive(IAsyncResult asyncResult)
        {
            if(asyncResult.IsCompleted)
            {
                try
                {
                    asyncBytesRead_ = (Int32)outboundSocket_.EndReceive(asyncResult);
                }
                catch (IOException)
                {
                    asyncBytesRead_ = 0;
                }
                catch (ObjectDisposedException)
                {
                    asyncBytesRead_ = 0;
                }
                catch (SocketException)
                {
                    asyncBytesRead_ = 0;
                }

                try
                {
                    outboundSocketReceiveEvent_.Set();
                }
                catch(ObjectDisposedException)
                {

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
                    asyncBytesRead_ = (Int32)sslStream_.EndRead(asyncResult);
                }
                catch (IOException)
                {
                    asyncBytesRead_ = 0;
                }
                catch (ObjectDisposedException)
                {
                    asyncBytesRead_ = 0;
                }

                try
                {
                    sslNetworkStreamReceiveEvent_.Set();
                }
                catch(ObjectDisposedException)
                {

                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void RunSocket()
        {
            byte[] receiveBuffer = new byte[4096];
            int receiveBufferCount = 0;

            ushort outboundConnectionAttempt = 0;
            while(!outboundThreadExitEvent_.WaitOne(0, false))
            {
                try
                {
                    if(outboundSocket_ == null)
                    {
                        if(++outboundConnectionAttempt <= 5)
                        {
                            try
                            {
                                outboundSocket_ = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                                outboundSocket_.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 1));
                                outboundSocket_.Connect(outboundIpEndPoint_);
                            }
                            catch(Exception)
                            {
                                break;
                            }
                            
                            if(outboundSocket_.Connected)
                            {
                                outboundConnectionAttempt = 0xFFFF;
                            }
                            else
                            {
                                Thread.Sleep(1000);
                            }
                        }
                        else
                        {
                            wwwProxy_.OnError(inbound_, request_, null, "Connection Failed [" + outboundIpEndPoint_.Address.ToString() + ":" + outboundIpEndPoint_.Port + "]");
                            break;
                        }
                    }
                    else
                    {
                        if((receiveBuffer.Length - receiveBufferCount) < 4096)
                        {
                            byte[] newreceiveBuffer = new byte[receiveBuffer.Length + 4096];
                            Array.Copy(receiveBuffer, 0, newreceiveBuffer, 0, receiveBuffer.Length);
                            receiveBuffer = newreceiveBuffer;
                        }

                        if(ssl_)
                        {
                            if(useRemoteProxy_ && !sslProxyNegotiationSent_)
                            {
                                string sslProxyNegotiation = "CONNECT " + host_ + ":" + port_ + " HTTP/1.0\r\n\r\n";
                                byte[] sslProxyNegotationBytes = wwwProxy_.defaultEncoding_.GetBytes(sslProxyNegotiation);
                                outboundSocket_.Send(sslProxyNegotationBytes, 0, sslProxyNegotationBytes.Length, SocketFlags.None);
                                sslProxyNegotiationSent_ = true;

                                while((receiveBuffer.Length - receiveBufferCount) > 0)
                                {
                                    int bytesReceived = outboundSocket_.Receive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None);
                                    receiveBufferCount += bytesReceived;

                                    bool bHeaderEnd = false;
                                    for(int i = 0; i < receiveBufferCount - 3; ++i)
                                    {
                                        if((receiveBuffer[i] == '\r') &&
                                           (receiveBuffer[i + 1] == '\n') &&
                                           (receiveBuffer[i + 2] == '\r') &&
                                           (receiveBuffer[i + 3] == '\n'))
                                        {
                                            bHeaderEnd = true;
                                            receiveBufferCount -= (i + 4);
                                            break;
                                        }
                                    }

                                    if(bHeaderEnd)
                                    {
                                        break;
                                    }
                                }

                                if(receiveBufferCount == receiveBuffer.Length)
                                {
                                    break;
                                }
                            }

                            if((sslNetworkStream_ == null) || (sslStream_ == null))
                            {
                                sslNetworkStream_ = new NetworkStream(outboundSocket_);

                                sslStream_ = new SslStream(sslNetworkStream_, false, new RemoteCertificateValidationCallback(OnRemoteCertificateValidation));
                                sslStream_.AuthenticateAsClient(outboundIpEndPoint_.Address.ToString(), wwwProxy_.clientCertificates_, SslProtocols.Default, false);
                            }
                        }

                        if(outboundSocket_.Connected)
                        {
                            if((request_ != null) && !request_.sent_)
                            {
                                Send(request_);
                            }

                            int bytesReceived = Receive(receiveBuffer, receiveBufferCount, receiveBuffer.Length - receiveBufferCount);
                            receiveBufferCount += bytesReceived;

                            if(receiveBufferCount > 0)
                            {
                                while(receiveBufferCount > 0)
                                {
                                    int bytesParsed = OnResponse(receiveBuffer, receiveBufferCount, (bytesReceived == 0));
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

                                if(bytesReceived == 0)
                                {
                                    break;
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
                    wwwProxy_.OnError(inbound_, null, e, e.Message);
                }
            }

            if(outboundSocket_ != null)
            {
                if(outboundSocket_.Connected)
                {
                    outboundSocket_.Shutdown(SocketShutdown.Both);
                    outboundSocket_.Disconnect(false);
                }
                outboundSocket_.Close();
            }

            if(outboundThreadExitEvent_ != null)
            {
                outboundThreadExitEvent_.Close();
            }
            if(outboundSocketReceiveEvent_ != null)
            {
                outboundSocketReceiveEvent_.Close();
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

            inbound_.OnDisconnect(this);
            if(!completableResponse_)
            {
                request_ = null;

                if(response_ != null)
                {
                    response_.outbound_ = null;
                    response_ = null;
                }

                if(responseCloseInbound_)
                {
                    inbound_.Close();
                }
            }

            if(outboundThread_ != null)
            {
                outboundThread_ = null;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
