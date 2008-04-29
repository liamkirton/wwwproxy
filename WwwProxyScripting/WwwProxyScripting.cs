///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxyScripting
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxyScripting.cs
//
// Created: 09/04/2008
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
using System.Text;
using System.Threading;

using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Runtime;

using WwwProxy;

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace WwwProxyScripting
{
    public class WwwProxyScripting : IPlugin
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private ScriptEngine scriptEngine_ = null;
        private ScriptScope scriptScope_ = null;
        private ScriptSource scriptSource_ = null;

        private object wwwProxyFilterInstance_ = null;

        private string scriptEngineName_ = null;
        private string scriptFileName_ = null;

        private Log debugLog_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public WwwProxyScripting()
        {
            debugLog_ = new Log("WwwProxyScripting_Debug.log");
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Initialise()
        {
            
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Enable()
        {
            try
            {
                scriptEngineName_ = "";
                scriptFileName_ = "";

                StreamReader iniReader = new StreamReader("Config\\WwwProxyScripting.ini");
                while(!iniReader.EndOfStream)
                {
                    string l = iniReader.ReadLine().Trim().ToLower();
                    if(l.StartsWith("#"))
                    {
                        continue;
                    }

                    string[] nameValue = l.Split('=');
                    if(nameValue.Length != 2)
                    {
                        continue;
                    }

                    string name = nameValue[0];
                    string value = nameValue[1];

                    if(name == "debug")
                    {
                        if(value == "true")
                        {
                            debugLog_.Start();
                        }
                    }
                    else if(name == "engine")
                    {
                        scriptEngineName_ = value;
                    }
                    else if(name == "file")
                    {
                        scriptFileName_ = value;
                    }
                }
                iniReader.Close();

                if((scriptEngineName_ == "") || (scriptFileName_ == ""))
                {
                    return;
                }

                scriptEngine_ = ScriptRuntime.Create().GetEngine(scriptEngineName_);
                scriptScope_ = scriptEngine_.CreateScope();

                scriptSource_ = scriptEngine_.CreateScriptSourceFromFile(scriptFileName_);
                scriptSource_.Execute(scriptScope_);

                object wwwProxyFilterClass = scriptScope_.GetVariable<object>("WwwProxyFilter");
                wwwProxyFilterInstance_ = scriptEngine_.Operations.Call(wwwProxyFilterClass);

                WwwProxy.WwwProxy.Instance.PreRequest += new WwwProxyRequestEvent(WwwProxy_PreRequest);
                WwwProxy.WwwProxy.Instance.PostRequest += new WwwProxyRequestEvent(WwwProxy_PostRequest);
                WwwProxy.WwwProxy.Instance.PreResponse += new WwwProxyResponseEvent(WwwProxy_PreResponse);
                WwwProxy.WwwProxy.Instance.PostResponse += new WwwProxyResponseEvent(WwwProxy_PostResponse);
            }
            catch(Exception e)
            {
                scriptEngineName_ = "";
                scriptFileName_ = "";

                debugLog_.Write("WwwProxyScripting.Enable()", e.ToString());
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Disable()
        {
            if((scriptEngineName_ == "") || (scriptFileName_ == ""))
            {
                return;
            }

            try
            {
                WwwProxy.WwwProxy.Instance.PreRequest -= new WwwProxyRequestEvent(WwwProxy_PreRequest);
                WwwProxy.WwwProxy.Instance.PostRequest -= new WwwProxyRequestEvent(WwwProxy_PostRequest);
                WwwProxy.WwwProxy.Instance.PreResponse -= new WwwProxyResponseEvent(WwwProxy_PreResponse);
                WwwProxy.WwwProxy.Instance.PostResponse -= new WwwProxyResponseEvent(WwwProxy_PostResponse);

                wwwProxyFilterInstance_ = null;

                scriptSource_ = null;
                scriptScope_ = null;
                
                if(scriptEngine_ != null)
                {
                    scriptEngine_.Shutdown();
                    scriptEngine_ = null;
                }
            }
            catch(Exception e)
            {
                debugLog_.Write("WwwProxyScripting.Disable()", e.ToString());
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void WwwProxy_PreRequest(ProxyRequest request)
        {
            try
            {
                object requestFilter = scriptEngine_.Operations.GetMember<object>(wwwProxyFilterInstance_, "pre_request_filter");

                object[] parameters = { request };
                scriptEngine_.Operations.Call(requestFilter, parameters);
            }
            catch(Exception e)
            {
                debugLog_.Write("WwwProxyScripting.WwwProxy_PreRequest()", e.ToString());
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void WwwProxy_PostRequest(ProxyRequest request)
        {
            try
            {
                object requestFilter = scriptEngine_.Operations.GetMember<object>(wwwProxyFilterInstance_, "post_request_filter");

                object[] parameters = { request };
                scriptEngine_.Operations.Call(requestFilter, parameters);
            }
            catch(Exception e)
            {
                debugLog_.Write("WwwProxyScripting.WwwProxy_PostRequest()", e.ToString());
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void WwwProxy_PreResponse(ProxyRequest request, ProxyResponse response)
        {
            try
            {
                object responseFilter = scriptEngine_.Operations.GetMember<object>(wwwProxyFilterInstance_, "pre_response_filter");

                object[] parameters = { request, response };
                scriptEngine_.Operations.Call(responseFilter, parameters);
            }
            catch(Exception e)
            {
                debugLog_.Write("WwwProxyScripting.WwwProxy_PreResponse()", e.ToString());
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void WwwProxy_PostResponse(ProxyRequest request, ProxyResponse response)
        {
            try
            {
                object responseFilter = scriptEngine_.Operations.GetMember<object>(wwwProxyFilterInstance_, "post_response_filter");

                object[] parameters = { request, response };
                scriptEngine_.Operations.Call(responseFilter, parameters);
            }
            catch(Exception e)
            {
                debugLog_.Write("WwwProxyScripting.WwwProxy_PostResponse()", e.ToString());
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
