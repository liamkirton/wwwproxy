// ========================================================================================================================
// WwwProxy\WwwProxyNtlm
//
// Copyright �2008 Liam Kirton <liam@int3.ws>
// ========================================================================================================================
// WwwProxyNtlm.h
//
// Created: 24/02/2008
// ========================================================================================================================
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
// ========================================================================================================================

#pragma once

// ========================================================================================================================

#define SECURITY_WIN32
#include <windows.h>
#include <security.h>

// ========================================================================================================================

namespace WwwProxyNtlm
{
	// ====================================================================================================================

	private struct NtlmSspi
	{
		CredHandle hCredHandle_;
		CtxtHandle hCtxtHandle_;

		unsigned long cbMaxToken;
	};

	// ====================================================================================================================
	
	public ref class WwwProxyNtlm : public WwwProxy::INtlm
	{
	public:
		WwwProxyNtlm();
		~WwwProxyNtlm();
		
		virtual System::String ^Continue(System::String ^token);
		virtual void Initialise(System::String ^domain, System::String ^user, System::String ^password);
		virtual void Reset();

		property System::String ^Site
		{
			virtual System::String ^get()
			{
				return site_;
			}
			virtual void set(System::String ^s)
			{
				site_ = s;
			}
		}

		property System::String ^Path
		{
			virtual System::String ^get()
			{
				return path_;
			}
			virtual void set(System::String ^s)
			{
				path_ = s;
			}
		}

		property System::String ^Type
		{
			virtual System::String ^get()
			{
				return type_;
			}
			virtual void set(System::String ^s)
			{
				type_ = s;
			}
		}

		property System::String ^Basic
		{
			virtual System::String ^get()
			{
				return basic_;
			}
			virtual void set(System::String ^s)
			{
				basic_ = s;
			}
		}

	private:
		NtlmSspi *pNtlmSspi_;

		System::String ^site_;
		System::String ^path_;
		System::String ^type_;
		System::String ^basic_;
	};

	// ====================================================================================================================
}

// ========================================================================================================================