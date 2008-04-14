///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxy
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ProxyRequest.cs
//
// Created: 17/09/2007
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
using System.Text;
using System.Threading;

namespace WwwProxy
{
    public class ProxyRequest
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal static int counter_ = 0;
        internal int id_ = 0;

        internal Encoding encoding_ = null;
        internal Inbound inbound_ = null;

        internal bool sent_ = false;
        internal string header_ = null;
        internal string completedHeader_ = null; 
        internal string data_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public ProxyRequest()
        {
            id_ = Interlocked.Increment(ref counter_);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public int Id
        {
            get
            {
                return id_;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public string Header
        {
            get
            {
                return header_;
            }
            set
            {
                header_ = value;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public string Data
        {
            get
            {
                return data_;
            }
            set
            {
                data_ = value;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
