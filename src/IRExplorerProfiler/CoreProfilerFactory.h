// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
#pragma once

class CoreProfilerFactory : public IClassFactory {
 public:
  // Inherited via IClassFactory
  HRESULT __stdcall QueryInterface(REFIID riid, void** ppvObject) override;
  ULONG __stdcall AddRef(void) override;
  ULONG __stdcall Release(void) override;
  HRESULT __stdcall CreateInstance(IUnknown* pUnkOuter,
                                   REFIID riid,
                                   void** ppvObject) override;
  HRESULT __stdcall LockServer(BOOL fLock) override { return E_NOTIMPL; }
};
