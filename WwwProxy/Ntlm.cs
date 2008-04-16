///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxy
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Ntlm.cs
//
// Created: 15/04/2008
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
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace WwwProxy
{
    class Ntlm : MarshalByRefObject
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private Exception crossDomainException_ = null;
        private AppDomain defaultAppDomain_ = null;

        private Assembly loadedAssembly_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public Ntlm()
        {
            defaultAppDomain_ = AppDomain.CurrentDomain;
            loadedAssembly_ = null;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public bool Load()
        {
            if(loadedAssembly_ != null)
            {
                return true;
            }

            AppDomain queryPluginAppDomain = AppDomain.CreateDomain("WwwProxy_queryNtlm");
            queryPluginAppDomain.DoCallBack(new CrossAppDomainDelegate(CrossAppDomainLoad));
            AppDomain.Unload(queryPluginAppDomain);

            if(crossDomainException_ != null)
            {
                WwwProxy.Instance.OnError(null, null, crossDomainException_, crossDomainException_.Message);
            }

            return (loadedAssembly_ != null);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public INtlm CreateInstance()
        {
            INtlm ntlm = null;

            if(loadedAssembly_ != null)
            {
                foreach(Type t in loadedAssembly_.GetTypes())
                {
                    if(t.IsClass)
                    {
                        foreach(Type i in t.GetInterfaces())
                        {
                            if(i.FullName == "WwwProxy.INtlm")
                            {
                                try
                                {
                                    ntlm = (INtlm)System.Activator.CreateInstance(t);
                                }
                                catch(Exception e)
                                {
                                    ntlm = null;
                                    WwwProxy.Instance.OnError(null, null, e, e.Message);
                                }
                                break;
                            }
                        }
                    }

                    if(ntlm != null)
                    {
                        break;
                    }
                }
            }

            return ntlm;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void CrossAppDomainLoad()
        {
            try
            {
                Match pathMatch = Regex.Match(Assembly.GetExecutingAssembly().Location, @"(.+\\)");
                if(pathMatch.Success)
                {
                    foreach(string dll in Directory.GetFiles(pathMatch.Groups[1].Value, "*.dll"))
                    {
                        CrossAppDomainLoadAssembly(dll);
                    }
                }
            }
            catch(Exception e)
            {
                crossDomainException_ = e;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private void CrossAppDomainLoadAssembly(string dll)
        {
            try
            {
                Assembly pluginAssembly = Assembly.ReflectionOnlyLoadFrom(dll);
                foreach(Type t in pluginAssembly.GetTypes())
                {
                    if(t.IsClass)
                    {
                        foreach(Type i in t.GetInterfaces())
                        {
                            if(i.FullName == "WwwProxy.INtlm")
                            {
                                loadedAssembly_ = defaultAppDomain_.Load(pluginAssembly.GetName());
                                return;
                            }
                        }
                    }
                }
            }
            catch(ReflectionTypeLoadException)
            {

            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}
