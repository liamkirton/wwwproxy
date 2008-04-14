///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WwwProxy\WwwProxy
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ProxyResponse.cs
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

namespace WwwProxy
{
    public class ProxyResponse
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal Encoding encoding_ = null; 
        internal Outbound outbound_ = null;
        internal ProxyRequest request_ = null;

        internal bool completable_ = true;
        internal string header_ = null;
        internal string contents_ = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public int Id
        {
            get
            {
                return (request_ != null) ? request_.id_ : -1;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public bool Completable
        {
            get
            {
                return completable_;
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

        public string Contents
        {
            get
            {
                return contents_;
            }
            set
            {
                contents_ = value;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
