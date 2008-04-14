///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxy
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy.cs
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
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WwwProxy
{
    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public delegate void WwwProxyErrorEvent(ProxyRequest request, Exception exception, string error);

    public delegate void WwwProxyRequestEvent(ProxyRequest request);
    public delegate void WwwProxyResponseEvent(ProxyRequest request, ProxyResponse response);

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    struct OnErrorParams
    {
        public Inbound inbound;
        public ProxyRequest request;

        public Exception exception;
        public string error;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public class WwwProxy
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private static WwwProxy wwwProxyInstance_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public event WwwProxyErrorEvent Error;

        public event WwwProxyRequestEvent PreRequest;
        public event WwwProxyRequestEvent Request;
        public event WwwProxyResponseEvent PreResponse;
        public event WwwProxyResponseEvent Response;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public Log log_ = null;

        internal Encoding defaultEncoding_ = null;
        internal Plugins plugins_ = null;

        internal bool debug_ = false;
        internal bool ntlmEnabled_ = false;
        internal bool pluginsEnabled_ = false;
        
        internal string certificate_ = null;
        internal X509CertificateCollection clientCertificates_ = null;
        
        internal IPEndPoint remoteProxy_ = null;
        internal string[] remoteProxyExceptions_ = null;

        internal Mutex ntlmListMutex_ = null;
        internal List<WwwProxyNtlm.WwwProxyNtlm> ntlmList_ = null;
        
        private Thread threadListener_ = null;
        private Thread threadNotifier_ = null;

        private ManualResetEvent tcpListenerAcceptEvent_ = null;
        private ManualResetEvent threadExitEvent_ = null;
        private ManualResetEvent threadNotifyEvent_ = null;

        private TcpListener tcpListener_ = null;
        private ushort tcpListenerPort_ = 0;

        private Mutex currentInboundListMutex_ = null;
        private List<Inbound> currentInboundList_ = null;

        private Mutex notifyQueueMutex_ = null;
        private Queue<object> notifyQueue_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public static WwwProxy Instance
        {
            get
            {
                if(wwwProxyInstance_ == null)
                {
                    wwwProxyInstance_ = new WwwProxy();
                }
                return wwwProxyInstance_;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public string Certificate
        {
            get
            {
                return certificate_;
            }
            set
            {
                certificate_ = value;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public X509CertificateCollection ClientCertificates
        {
            get
            {
                return clientCertificates_;
            }
            set
            {
                clientCertificates_ = value;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public bool Debug
        {
            get
            {
                return debug_;
            }
            set
            {
                debug_ = value;

                if(debug_)
                {
                    log_.Start();
                }
                else
                {
                    log_.Stop();
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        
        public bool NtlmEnabled
        {
            get
            {
                return ntlmEnabled_;
            }
            set
            {
                ntlmEnabled_ = value;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public bool PluginsEnabled
        {
            get
            {
                return pluginsEnabled_;
            }
            set
            {
                pluginsEnabled_ = value;

                if(pluginsEnabled_)
                {
                    if(plugins_ == null)
                    {
                        plugins_ = new Plugins();
                        plugins_.Load();
                    }
                    plugins_.Enable();
                }
                else if(plugins_ != null)
                {
                    plugins_.Disable();
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        
        public IPEndPoint RemoteProxy
        {
            get
            {
                return remoteProxy_;
            }
            set
            {
                remoteProxy_ = value;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public string RemoteProxyExceptions
        {
            get
            {
                string value = "";
                foreach(string s in remoteProxyExceptions_)
                {
                    if(value.Length > 0)
                    {
                        value += ";";
                    }
                    value += s;
                }
                return value;
            }
            set
            {
                remoteProxyExceptions_ = (value != null) ? value.ToLower().Split(';') : null;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        
        WwwProxy()
        {
            string wwwProxyDebug = System.Environment.GetEnvironmentVariable("WwwProxy_Debug");
            if((wwwProxyDebug != null) && (wwwProxyDebug != ""))
            {
                debug_ = true;
            }

            defaultEncoding_ = Encoding.GetEncoding("iso-8859-1");

            currentInboundListMutex_ = new Mutex();
            currentInboundList_ = new List<Inbound>();

            notifyQueueMutex_ = new Mutex();
            notifyQueue_ = new Queue<object>();

            ntlmListMutex_ = new Mutex();
            ntlmList_ = new List<WwwProxyNtlm.WwwProxyNtlm>();

            tcpListenerAcceptEvent_ = new ManualResetEvent(false);
            threadExitEvent_ = new ManualResetEvent(false);
            threadNotifyEvent_ = new ManualResetEvent(false);
            
            log_ = new Log("WwwProxy_Debug.log");
            if(debug_)
            {
                log_.Start();
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Start(ushort localPort)
        {
            if((threadListener_ != null) || (threadNotifier_ != null))
            {
                Stop();
            }

            log_.Write("WwwProxy.Start", "Port=" + Convert.ToString(localPort));

            threadNotifyEvent_.Reset();
            threadExitEvent_.Reset();

            tcpListenerPort_ = localPort;
            
            threadListener_ = new Thread(RunListener);
            threadListener_.Start();
            threadNotifier_ = new Thread(RunNotifier);
            threadNotifier_.Start();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Stop()
        {
            if(threadExitEvent_ != null)
            {
                threadExitEvent_.Set();
            } 
            if(threadNotifyEvent_ != null)
            {
                threadNotifyEvent_.Set();
            }
            if(tcpListenerAcceptEvent_ != null)
            {
                tcpListenerAcceptEvent_.Set();
            }
            
            currentInboundListMutex_.WaitOne();
            foreach(Inbound i in currentInboundList_)
            {
                i.Close();
            }
            currentInboundList_.Clear();
            currentInboundListMutex_.ReleaseMutex();

            ntlmListMutex_.WaitOne();
            foreach(WwwProxyNtlm.WwwProxyNtlm w in ntlmList_)
            {
                w.Dispose();
            }
            ntlmList_.Clear();
            ntlmListMutex_.ReleaseMutex();
        
            notifyQueue_.Clear();

            if(threadNotifier_ != null)
            {
                if(!threadNotifier_.Join(10000))
                {
                    threadNotifier_.Abort();
                }
                threadNotifier_ = null;
            }
            if(threadListener_ != null)
            {
                if(!threadListener_.Join(10000))
                {
                    threadNotifier_.Abort();
                }
                threadListener_ = null;
            }

            log_.Write("WwwProxy.Stop", "");
            log_.Stop();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Drop(ProxyRequest request)
        {
            if(request.inbound_ != null)
            {
                request.inbound_.Close();
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Pass(ProxyRequest request)
        {
            if(request.inbound_ != null)
            {
                request.inbound_.Complete(request);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Drop(ProxyResponse response)
        {
            if(response.outbound_ != null)
            {
                response.outbound_.Close();
                if(response.outbound_.inbound_ != null)
                {
                    response.outbound_.inbound_.Close();
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Pass(ProxyResponse response)
        {
            if(response.completable_ && (response.outbound_ != null))
            {
                response.outbound_.Complete(response);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnConnect(Inbound inbound)
        {
            
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnDisconnect(Inbound inbound)
        {
            inbound.Close();

            currentInboundListMutex_.WaitOne();
            currentInboundList_.Remove(inbound);
            currentInboundListMutex_.ReleaseMutex();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnAcceptSocket(IAsyncResult asyncResult)
        {
            if(asyncResult.IsCompleted)
            {
                try
                {
                    Socket acceptSocket = tcpListener_.EndAcceptSocket(asyncResult);
                    acceptSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 1));

                    currentInboundListMutex_.WaitOne();
                    currentInboundList_.Add(new Inbound(this, acceptSocket));
                    currentInboundListMutex_.ReleaseMutex();
                }
                catch(ObjectDisposedException)
                {

                }

                tcpListenerAcceptEvent_.Set();
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void DoError(object o)
        {
            if(Error != null)
            {
                OnErrorParams onErrorParams = (OnErrorParams)o;
                log_.Write("WwwProxy.Error", onErrorParams.error);
                Error(onErrorParams.request,
                      onErrorParams.exception,
                      onErrorParams.error);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnError(Inbound inbound, ProxyRequest request, Exception exception, string error)
        {
            string s = error + "\r\n";
            if(exception != null)
            {
                s += exception.Message + "\r\n";
                s += exception.StackTrace + "\r\n";
            }
            log_.Write("WwwProxy.OnError", s);

            OnErrorParams onErrorParams;
            onErrorParams.inbound = inbound;
            onErrorParams.request = request;
            onErrorParams.exception = exception;
            onErrorParams.error = error;

            Thread onErrorThread = new Thread(new ParameterizedThreadStart(DoError));
            onErrorThread.Start(onErrorParams);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnRequest(ProxyRequest request)
        {
            if(request.Header != null)
            {
                notifyQueueMutex_.WaitOne();
                notifyQueue_.Enqueue(request);
                threadNotifyEvent_.Set();
                notifyQueueMutex_.ReleaseMutex();
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal void OnResponse(ProxyResponse response)
        {
            if((response.request_ != null) && (response.request_.Header != null))
            {
                notifyQueueMutex_.WaitOne();
                notifyQueue_.Enqueue(response);
                threadNotifyEvent_.Set();
                notifyQueueMutex_.ReleaseMutex();
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void RunListener()
        {
            try
            {
                tcpListener_ = new TcpListener(IPAddress.Loopback, tcpListenerPort_);
                tcpListener_.Start();

                WaitHandle[] waitHandles = new WaitHandle[2];
                waitHandles[0] = threadExitEvent_;
                waitHandles[1] = tcpListenerAcceptEvent_;

                while(!threadExitEvent_.WaitOne(0, false))
                {
                    tcpListenerAcceptEvent_.Reset();
                    tcpListener_.BeginAcceptSocket(new AsyncCallback(OnAcceptSocket), null);

                    if(WaitHandle.WaitAny(waitHandles, Timeout.Infinite, false) == 0)
                    {
                        break;
                    }
                }

                tcpListener_.Stop();
            }
            catch(Exception e)
            {
                OnError(null, null, e, e.Message);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void RunNotifier()
        {
            WaitHandle[] waitHandles = new WaitHandle[2];
            waitHandles[0] = threadExitEvent_;
            waitHandles[1] = threadNotifyEvent_;

            while(!threadExitEvent_.WaitOne(0, false))
            {
                object notifyObject = null;

                if(WaitHandle.WaitAny(waitHandles, Timeout.Infinite, false) == 0)
                {
                    break;
                }
                
                notifyQueueMutex_.WaitOne();
                if(notifyQueue_.Count > 0)
                {
                    notifyObject = notifyQueue_.Dequeue();
                }
                else
                {
                    threadNotifyEvent_.Reset();
                }
                notifyQueueMutex_.ReleaseMutex();

                if(notifyObject != null)
                {
                    if(notifyObject is ProxyRequest)
                    {
                        ProxyRequest notifyRequest = (ProxyRequest)notifyObject;
                        if(PreRequest != null)
                        {
                            PreRequest(notifyRequest);
                        }

                        if(Request != null)
                        {
                            Request(notifyRequest);
                        }
                        else
                        {
                            Pass(notifyRequest);
                        }
                    }
                    else if(notifyObject is ProxyResponse)
                    {
                        ProxyResponse notifyResponse = (ProxyResponse)notifyObject;
                        if(PreResponse != null)
                        {
                            PreResponse(notifyResponse.request_, notifyResponse);
                        }

                        if(Response != null)
                        {
                            Response(notifyResponse.request_, notifyResponse);
                        }
                        else
                        {
                            Pass(notifyResponse);
                        }
                    }
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////