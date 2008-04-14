///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxy
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Plugins.cs
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
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace WwwProxy
{
    class Plugins : MarshalByRefObject
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private Exception crossDomainException_ = null;
        private AppDomain defaultAppDomain_ = null;
        private bool loaded_ = false;

        private List<Assembly> loadedAssemblies_ = null;
        private List<IPlugin> loadedPlugins_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public Plugins()
        {
            defaultAppDomain_ = AppDomain.CurrentDomain;
            loadedAssemblies_ = new List<Assembly>();
            loadedPlugins_ = new List<IPlugin>();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Enable()
        {
            foreach(IPlugin i in loadedPlugins_)
            {
                i.Enable();
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Disable()
        {
            foreach(IPlugin i in loadedPlugins_)
            {
                i.Disable();
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Load()
        {
            if(loaded_)
            {
                return;
            }
            loaded_ = true;

            AppDomain queryPluginAppDomain = AppDomain.CreateDomain("WwwProxy_queryPluginAppDomain");
            queryPluginAppDomain.DoCallBack(new CrossAppDomainDelegate(CrossAppDomainLoad));
            AppDomain.Unload(queryPluginAppDomain);

            if(crossDomainException_ != null)
            {
                WwwProxy.Instance.OnError(null, null, crossDomainException_, crossDomainException_.Message);
            }

            foreach(Assembly a in loadedAssemblies_)
            {
                IPlugin plugin = null;

                foreach(Type t in a.GetTypes())
                {
                    if(t.IsClass)
                    {
                        foreach(Type i in t.GetInterfaces())
                        {
                            if(i.FullName == "WwwProxy.IPlugin")
                            {
                                try
                                {
                                    plugin = (IPlugin)System.Activator.CreateInstance(t);
                                    plugin.Initialise();
                                }
                                catch(Exception e)
                                {
                                    plugin = null;
                                    WwwProxy.Instance.OnError(null, null, e, e.Message);
                                }
                                break;
                            }
                        }
                    }

                    if(plugin != null)
                    {
                        loadedPlugins_.Add(plugin);
                        break;
                    }
                }
            }
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
                            if(i.FullName == "WwwProxy.IPlugin")
                            {
                                Assembly loadedPluginAssembly = defaultAppDomain_.Load(pluginAssembly.GetName());
                                loadedAssemblies_.Add(loadedPluginAssembly);
                                return;
                            }
                        }
                    }
                }
            }
            catch(ReflectionTypeLoadException e)
            {

            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}
