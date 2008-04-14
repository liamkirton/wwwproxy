// ========================================================================================================================
// WwwProxy\WwwProxyNtlm
//
// Copyright ©2008 Liam Kirton <liam@int3.ws>
// ========================================================================================================================
// WwwProxyNtlm.cpp
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

#include "WwwProxyNtlm.h"

#include <vcclr.h>

// ========================================================================================================================

namespace WwwProxyNtlm
{
	// ====================================================================================================================

	WwwProxyNtlm::WwwProxyNtlm() : site_(nullptr),
								   path_(nullptr),
								   basic_(nullptr)
	{
		pNtlmSspi_ = new NtlmSspi;
		RtlZeroMemory(&pNtlmSspi_->hCredHandle_, sizeof(CredHandle));
		RtlZeroMemory(&pNtlmSspi_->hCtxtHandle_, sizeof(CtxtHandle));
		pNtlmSspi_->cbMaxToken = 0;
	}

	// ====================================================================================================================
	
	WwwProxyNtlm::~WwwProxyNtlm()
	{
		Reset();

		delete pNtlmSspi_;
		pNtlmSspi_ = NULL;
	}

	// ====================================================================================================================

	System::String ^WwwProxyNtlm::Continue(System::String ^token)
	{
		unsigned char *tokenInputBuffer = NULL;
		unsigned char *tokenOutputBuffer = new unsigned char[pNtlmSspi_->cbMaxToken];

		int tokenInputBufferLength = 0;
		int tokenOutputBufferLength = 0;
		
		if(token != nullptr)
		{
			array<System::Byte>^ tokenInputByteArray = System::Convert::FromBase64String(token);

			tokenInputBufferLength = tokenInputByteArray->Length;
			tokenInputBuffer = new unsigned char[tokenInputBufferLength];
			System::Runtime::InteropServices::Marshal::Copy(tokenInputByteArray, 0, static_cast<System::IntPtr>(tokenInputBuffer), tokenInputBufferLength);
		}

		SecBufferDesc inSecBufferDesc;
		SecBufferDesc outSecBufferDesc;
		SecBuffer inSecBuffer;
		SecBuffer outSecBuffer;

		inSecBufferDesc.ulVersion = 0;
		inSecBufferDesc.cBuffers = 1;
		inSecBufferDesc.pBuffers = &inSecBuffer;
		inSecBuffer.cbBuffer = tokenInputBufferLength;
		inSecBuffer.BufferType = SECBUFFER_TOKEN;
		inSecBuffer.pvBuffer = tokenInputBuffer;
		
		outSecBufferDesc.ulVersion = 0;
		outSecBufferDesc.cBuffers = 1;
		outSecBufferDesc.pBuffers = &outSecBuffer;
		outSecBuffer.cbBuffer = pNtlmSspi_->cbMaxToken;
		outSecBuffer.BufferType = SECBUFFER_TOKEN;
		outSecBuffer.pvBuffer = tokenOutputBuffer;

		ULONG contextAttributes;
		SECURITY_STATUS scRet = InitializeSecurityContext(&pNtlmSspi_->hCredHandle_,
														  (token != nullptr) ? &pNtlmSspi_->hCtxtHandle_ : NULL,
														  L"InetSvcs",
														  0,
														  0,
														  SECURITY_NATIVE_DREP,
														  (token != nullptr) ? &inSecBufferDesc : NULL,
														  0,
														  &pNtlmSspi_->hCtxtHandle_,
														  &outSecBufferDesc,
														  &contextAttributes,
														  NULL);

		if((scRet == SEC_I_COMPLETE_NEEDED) ||
		   (scRet == SEC_I_COMPLETE_AND_CONTINUE))
		{
			CompleteAuthToken(&pNtlmSspi_->hCtxtHandle_, &outSecBufferDesc);
		}

		tokenOutputBufferLength = outSecBuffer.cbBuffer;

		System::String ^tokenResult = nullptr;
		if(SUCCEEDED(scRet))
		{
			array<System::Byte>^ tokenOutputByteArray = gcnew array<System::Byte>(tokenOutputBufferLength);
			System::Runtime::InteropServices::Marshal::Copy(static_cast<System::IntPtr>(tokenOutputBuffer), tokenOutputByteArray, 0, tokenOutputBufferLength);
			tokenResult = System::Convert::ToBase64String(tokenOutputByteArray);
		}
		
		delete [] tokenInputBuffer;
		delete [] tokenOutputBuffer;
		
		if(FAILED(scRet))
		{
			throw gcnew System::Exception("InitializeSecurityContext() Failed.");
		}

		return tokenResult;
	}

	// ====================================================================================================================

	void WwwProxyNtlm::Initialise(System::String ^domain, System::String ^user, System::String ^password)
	{
		pin_ptr<const wchar_t> pwchDomain = PtrToStringChars(domain);
		pin_ptr<const wchar_t> pwchUser = PtrToStringChars(user);
		pin_ptr<const wchar_t> pwchPassword = PtrToStringChars(password);

		SEC_WINNT_AUTH_IDENTITY secWinntAuthIdentity;
		RtlZeroMemory(&secWinntAuthIdentity, sizeof(SEC_WINNT_AUTH_IDENTITY));

		secWinntAuthIdentity.Flags = SEC_WINNT_AUTH_IDENTITY_UNICODE;
		secWinntAuthIdentity.Domain = const_cast<unsigned short *>(reinterpret_cast<const unsigned short *>(pwchDomain));
		secWinntAuthIdentity.DomainLength = domain->Length;
		secWinntAuthIdentity.User = const_cast<unsigned short *>(reinterpret_cast<const unsigned short *>(pwchUser));
		secWinntAuthIdentity.UserLength = user->Length;
		secWinntAuthIdentity.Password = const_cast<unsigned short *>(reinterpret_cast<const unsigned short *>(pwchPassword));
		secWinntAuthIdentity.PasswordLength = password->Length;

		if(AcquireCredentialsHandle(NULL, L"NTLM", SECPKG_CRED_OUTBOUND, NULL, &secWinntAuthIdentity, NULL, NULL, &pNtlmSspi_->hCredHandle_, NULL) != SEC_E_OK)
		{
			throw gcnew System::Exception("AcquireCredentialsHandle() Failed.");
		}

		PSecPkgInfo pSecPkgInfo = NULL;
		if(QuerySecurityPackageInfo(L"NTLM", &pSecPkgInfo) == SEC_E_OK)
		{
			pNtlmSspi_->cbMaxToken = pSecPkgInfo->cbMaxToken;
			FreeContextBuffer(pSecPkgInfo);
		}
		else
		{
			throw gcnew System::Exception("QuerySecurityPackageInfo() Failed.");
		}
	}

	// ====================================================================================================================

	void WwwProxyNtlm::Reset()
	{
		basic_ = nullptr;
		
		FreeCredentialsHandle(&pNtlmSspi_->hCredHandle_);
		
		RtlZeroMemory(&pNtlmSspi_->hCredHandle_, sizeof(CredHandle));
		RtlZeroMemory(&pNtlmSspi_->hCtxtHandle_, sizeof(CtxtHandle));
		pNtlmSspi_->cbMaxToken = 0;
	}

	// ====================================================================================================================
}

// ========================================================================================================================
